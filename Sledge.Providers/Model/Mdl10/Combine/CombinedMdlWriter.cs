using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Sledge.Providers.Model.Mdl10.Format;
using MdlTexture = Sledge.Providers.Model.Mdl10.Format.Texture;

namespace Sledge.Providers.Model.Mdl10.Combine
{
	public class CombineMember
	{
		public string Path;
		public string Name;
		public MdlFile Src;
		public BindPoseMesh Mesh;      // already transformed into combined origin space
		public List<int> SrcTex;       // source texture indices used by skin 0, in order
		public int TexBase;            // first combined texture index for this member
	}

	public static class CombineWriter
	{
		const int HeaderSize = 244, BoneSize = 112, HitboxSize = 32, AnimSize = 16, SeqSize = 176, SeqGroupSize = 104;
		const int BodyPartSize = 76, ModelSize = 112, MeshSize = 20, TexSize = 80;

		static void FixedString(BinaryWriter bw, string s, int len)
		{
			var b = new byte[len];
			var src = Encoding.ASCII.GetBytes(s ?? "");
			Array.Copy(src, b, Math.Min(src.Length, len - 1));
			bw.Write(b);
		}

		static void Pad4(MemoryStream ms)
		{
			while (ms.Position % 4 != 0) ms.WriteByte(0);
		}

		public static byte[] Build(List<CombineMember> members, string modelName)
		{
			// Source bones, animation, sequence and sequence group blocks are copied from member 0.
			var srcBytes = File.ReadAllBytes(members[0].Path);
			byte[] boneBlk = srcBytes.Skip(244).Take(BoneSize).ToArray();
			byte[] animBlk = srcBytes.Skip(388).Take(AnimSize).ToArray();
			byte[] seqBlk = srcBytes.Skip(404).Take(SeqSize).ToArray();
			byte[] seqgBlk = srcBytes.Skip(580).Take(SeqGroupSize).ToArray();

			// Combined texture table: each member contributes the textures its skin 0 uses.
			var textures = new List<MdlTexture>();
			foreach (var m in members)
			{
				m.TexBase = textures.Count;
				foreach (var idx in m.SrcTex) textures.Add(m.Src.Textures[idx]);
			}
			int nt = textures.Count;

			// Bounds over all members.
			var all = members.SelectMany(m => m.Mesh.Vertices).ToList();
			var bbMin = new Vector3(all.Min(v => v.Position.X), all.Min(v => v.Position.Y), all.Min(v => v.Position.Z));
			var bbMax = new Vector3(all.Max(v => v.Position.X), all.Max(v => v.Position.Y), all.Max(v => v.Position.Z));

			int bodyPartIndex = 684;
			int modelsStart = bodyPartIndex + BodyPartSize * members.Count;
			int blobStart = modelsStart + ModelSize * members.Count;

			var blob = new MemoryStream();
			var modelHeaders = new List<(int meshIndex, int numMesh, int numVerts, int viOff, int vOff, int numNorms, int niOff, int nOff, float radius)>();
			var blobBw = new BinaryWriter(blob);

			foreach (var m in members)
			{
				blobBw.Flush();
				var corners = m.Mesh.Vertices;
				int triCount = corners.Count / 3;

				// Unique positions (model-wide) and normals (one contiguous block per mesh).
				var posIdx = new Dictionary<(float, float, float), int>();
				var posList = new List<Vector3>();
				int Pos(Vector3 p)
				{
					var k = (p.X, p.Y, p.Z);
					if (!posIdx.TryGetValue(k, out var i)) { i = posList.Count; posIdx[k] = i; posList.Add(p); }
					return i;
				}

				// Group triangles by combined texture, in member texture order.
				var groups = new SortedDictionary<int, List<int>>();
				for (int t = 0; t < triCount; t++)
				{
					var tex = m.TexBase + m.SrcTex.IndexOf(corners[t * 3].TextureIndex);
					if (!groups.TryGetValue(tex, out var list)) { list = new List<int>(); groups[tex] = list; }
					list.Add(t);
				}
				// Assign indices in output order so vertex/normal pools are stable.
				var meshes = groups.ToList();
				var vertIndex = new int[triCount * 3];
				var normIndex = new int[triCount * 3];
				// The engine (and HLAM) expect each mesh's normals as a contiguous block, in mesh order,
				// and sum(mesh.numnorms) == model.numnorms. Normals are therefore deduped per mesh, not model-wide.
				var nrmList = new List<Vector3>();
				var meshNormCount = new List<int>();
				foreach (var g in meshes)
				{
					var local = new Dictionary<(float, float, float), int>();
					int blockStart = nrmList.Count;
					foreach (var t in g.Value)
						for (int c = 0; c < 3; c++)
						{
							var v = corners[t * 3 + c];
							vertIndex[t * 3 + c] = Pos(v.Position);
							var k = (v.Normal.X, v.Normal.Y, v.Normal.Z);
							if (!local.TryGetValue(k, out var li)) { li = nrmList.Count - blockStart; local[k] = li; nrmList.Add(v.Normal); }
							normIndex[t * 3 + c] = blockStart + li;
						}
					meshNormCount.Add(nrmList.Count - blockStart);
				}

				int numVerts = posList.Count, numNorms = nrmList.Count, numMesh = meshes.Count;

				// Offsets (absolute file positions).
				long start = blobStart + blob.Position;
				int viOff = (int)start;
				int niOff = viOff + ((numVerts + 3) & ~3);
				int vOff = niOff + ((numNorms + 3) & ~3);
				int nOff = vOff + numVerts * 12;
				int meshOff = nOff + numNorms * 12;
				int triBase = meshOff + numMesh * MeshSize;

				// Triangle commands, built first so mesh headers know their offsets.
				var triMs = new MemoryStream();
				var triBw = new BinaryWriter(triMs);
				var meshHeaders = new List<(int numTris, int triIndex, int skinRef, int numNormsUsed)>();
				for (int mi = 0; mi < meshes.Count; mi++)
				{
					var g = meshes[mi];
					int triIndex = triBase + (int)triMs.Position;
					foreach (var t in g.Value)
					{
						var c0 = corners[t * 3];
						triBw.Write((short)3);
						for (int c = 0; c < 3; c++)
						{
							var v = corners[t * 3 + c];
							int s = (int)Math.Round(v.Uv.X * TextureWidth(textures, g.Key));
							int tt = (int)Math.Round(v.Uv.Y * TextureHeight(textures, g.Key));
							triBw.Write((short)vertIndex[t * 3 + c]);
							triBw.Write((short)normIndex[t * 3 + c]);
							triBw.Write((short)s);
							triBw.Write((short)tt);
						}
					}
					triBw.Write((short)0);
					meshHeaders.Add((g.Value.Count, triIndex, g.Key, meshNormCount[mi]));
				}

				// Model blob: vertinfo, norminfo, verts, norms, mesh headers, triangle commands.
				blobBw.Write(new byte[(numVerts + 3) & ~3]);                 // vertex bone info (all bone 0)
				blobBw.Write(new byte[(numNorms + 3) & ~3]);                 // normal bone info (all bone 0)
				foreach (var p in posList) { blobBw.Write(p.X); blobBw.Write(p.Y); blobBw.Write(p.Z); }
				foreach (var n in nrmList) { blobBw.Write(n.X); blobBw.Write(n.Y); blobBw.Write(n.Z); }
				foreach (var mh in meshHeaders)
				{
					blobBw.Write(mh.numTris);
					blobBw.Write(mh.triIndex);
					blobBw.Write(mh.skinRef);
					blobBw.Write(mh.numNormsUsed);
					blobBw.Write(0); // normindex (not used by the engine's lighting path; matches source files)
				}
				blobBw.Write(triMs.ToArray());

				float radius = posList.Count == 0 ? 0 : posList.Max(p => p.Length());
				modelHeaders.Add((meshOff, numMesh, numVerts, viOff, vOff, numNorms, niOff, nOff, radius));
			}
			blobBw.Flush();

			int texTableOff = blobStart + (int)blob.Length;
			int skinOff = texTableOff + TexSize * nt;
			int texDataOff = skinOff + 2 * nt;

			// Texture data, with absolute indices.
			var texData = new MemoryStream();
			var texIndex = new int[nt];
			for (int i = 0; i < nt; i++)
			{
				texIndex[i] = texDataOff + (int)texData.Position;
				texData.Write(textures[i].Data, 0, textures[i].Data.Length);
				texData.Write(textures[i].Palette, 0, textures[i].Palette.Length);
			}
			long total = texDataOff + texData.Length;

			var ms = new MemoryStream();
			var bw = new BinaryWriter(ms, Encoding.ASCII);

			// Header
			bw.Write(0x54534449);          // IDST
			bw.Write(10);                  // version
			FixedString(bw, modelName, 64);
			bw.Write((int)total);
			WriteVec(bw, Vector3.Zero);    // eye
			WriteVec(bw, bbMin);           // hull min
			WriteVec(bw, bbMax);           // hull max
			WriteVec(bw, bbMin);           // bbox min
			WriteVec(bw, bbMax);           // bbox max
			bw.Write(0);                   // flags
			bw.Write(1); bw.Write(244);    // bones
			bw.Write(0); bw.Write(356);    // bone controllers
			bw.Write(1); bw.Write(356);    // hitboxes
			bw.Write(1); bw.Write(404);    // sequences
			bw.Write(1); bw.Write(580);    // sequence groups
			bw.Write(nt); bw.Write(texTableOff); // textures
			bw.Write(texDataOff);          // texture data
			bw.Write(nt); bw.Write(1); bw.Write(skinOff); // skin refs, families, index
			bw.Write(members.Count); bw.Write(bodyPartIndex); // body parts
			bw.Write(0); bw.Write(356);    // attachments
			bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0); // sound
			bw.Write(0); bw.Write(684);    // transitions

			// Bones (copied), hitbox, animation, sequence, sequence group
			bw.Write(boneBlk);
			// Hitbox covering the whole combined model, bone 0.
			bw.Write(0); bw.Write(0); WriteVec(bw, bbMin); WriteVec(bw, bbMax);
			bw.Write(animBlk);
			bw.Write(seqBlk);
			bw.Write(seqgBlk);

			// Body parts (one model each, so every member renders together).
			for (int i = 0; i < members.Count; i++)
			{
				FixedString(bw, members[i].Name, 64);
				bw.Write(1);                       // nummodels
				bw.Write(1);                       // base
				bw.Write(modelsStart + ModelSize * i);
			}

			// Models
			for (int i = 0; i < members.Count; i++)
			{
				var h = modelHeaders[i];
				FixedString(bw, members[i].Name, 64);
				bw.Write(0);                       // type
				bw.Write(h.radius);
				bw.Write(h.numMesh);
				bw.Write(h.meshIndex);
				bw.Write(h.numVerts);
				bw.Write(h.viOff);
				bw.Write(h.vOff);
				bw.Write(h.numNorms);
				bw.Write(h.niOff);
				bw.Write(h.nOff);
				bw.Write(0); bw.Write(0);          // groups
			}

			bw.Write(blob.ToArray());

			// Texture headers
			for (int i = 0; i < nt; i++)
			{
				var t = textures[i];
				FixedString(bw, t.Header.Name, 64);
				bw.Write((int)t.Header.Flags);
				bw.Write(t.Header.Width);
				bw.Write(t.Header.Height);
				bw.Write(texIndex[i]);
			}

			// Skin table: one family, identity over all textures.
			for (int i = 0; i < nt; i++) bw.Write((short)i);

			bw.Write(texData.ToArray());
			bw.Flush();
			if (ms.Length != total) throw new Exception($"layout mismatch: wrote {ms.Length}, expected {total}");
			return ms.ToArray();
		}

		static void WriteVec(BinaryWriter bw, Vector3 v) { bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
		static int TextureWidth(List<MdlTexture> t, int i) => t[i].Header.Width;
		static int TextureHeight(List<MdlTexture> t, int i) => t[i].Header.Height;
	}
}
