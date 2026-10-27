using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Sledge.DataStructures.Geometric;
using Sledge.Providers.Model.Mdl10.Format;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Veldrid;
using Buffer = Sledge.Rendering.Resources.Buffer;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Sledge.Providers.Model.Mdl10
{
	public class MdlModel : IModel
	{
		private const int TEXTURE_MARGIN = 2;
		public MdlFile Model { get; }
		public VertexFlags Flags { get; set; } = 0;

		private readonly Guid _guid;
		private uint[][] _bodyPartIndices;

		private Rendering.Resources.Texture _textureResource;
		private List<Rectangle> _originalRectangles;
		private Buffer _buffer;
		private uint _numTexturedIndices;
		private uint _numWireframeIndices;
		private uint[][] _skins;
		private readonly List<MeshRange> _meshRanges = new List<MeshRange>();

		/// <summary>True when any texture of the model has the GoldSrc additive flag.</summary>
		public bool HasAdditive { get; private set; }

		private string TextureName => $"{nameof(MdlModel)}::{_guid}";

		private struct MeshRange
		{
			public readonly int BodyPart;
			public readonly int Model;
			public readonly uint Start;
			public readonly uint Count;
			public readonly bool Additive;

			public MeshRange(int bodyPart, int model, uint start, uint count, bool additive)
			{
				BodyPart = bodyPart;
				Model = model;
				Start = start;
				Count = count;
				Additive = additive;
			}
		}

		public MdlModel(MdlFile model)
		{
			Model = model;
			_guid = Guid.NewGuid();
		}

		public List<string> GetSequences()
		{
			return Model.Sequences.Select(x => x.Header.Name).ToList();
		}

		public (Vector3, Vector3) GetBoundingBox(int sequence, int frame, float subframe)
		{
			var transforms = new Matrix4x4[Model.Bones.Count];
			Model.GetTransforms(sequence, frame, subframe, ref transforms);

			var list =
				from part in Model.BodyParts
				from mesh in part.Models[0].Meshes
				from vertex in mesh.Vertices
				let transform = transforms[vertex.VertexBone]
				select Vector3.Transform(vertex.Vertex, transform);

			var box = new Box(list);
			return (box.Start, box.End);
		}

		/// <summary>
		/// Triangles of the model at the first frame of a sequence, in model space (bone transforms
		/// applied, entity origin and angles not applied). Used for wireframe picking.
		/// </summary>
		public List<Vector3[]> GetPickTriangles(int sequence)
		{
			var result = new List<Vector3[]>();
			if (Model.Bones.Count == 0) return result;

			var seq = sequence >= 0 && sequence < Model.Sequences.Count ? sequence : 0;
			var transforms = new Matrix4x4[Model.Bones.Count];
			Model.GetTransforms(seq, 0, 0, ref transforms);

			foreach (var part in Model.BodyParts)
			{
				if (part.Models == null || part.Models.Length == 0) continue;
				foreach (var mesh in part.Models[0].Meshes)
				{
					// Mesh vertices are already expanded to a plain triangle list (3 per triangle)
					var verts = mesh.Vertices;
					for (var i = 0; i + 2 < verts.Length; i += 3)
					{
						result.Add(new[]
						{
							Vector3.Transform(verts[i].Vertex, transforms[verts[i].VertexBone]),
							Vector3.Transform(verts[i + 1].Vertex, transforms[verts[i + 1].VertexBone]),
							Vector3.Transform(verts[i + 2].Vertex, transforms[verts[i + 2].VertexBone]),
						});
					}
				}
			}
			return result;
		}

		private static Bitmap CreateBitmap(int width, int height, byte[] data, byte[] palette, bool lastTextureIsTransparent)
		{
			var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

			// Set palette
			var pal = bmp.Palette;
			for (var j = 0; j <= byte.MaxValue; j++)
			{
				var k = j * 3;
				pal.Entries[j] = Color.FromArgb(255, palette[k], palette[k + 1], palette[k + 2]);
			}

			if (lastTextureIsTransparent)
			{
				pal.Entries[pal.Entries.Length - 1] = Color.Transparent;
			}
			bmp.Palette = pal;

			// Write entries
			var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat);
			Marshal.Copy(data, 0, bmpData.Scan0, data.Length);
			bmp.UnlockBits(bmpData);

			return bmp;
		}
		private List<Rectangle> CreateTexuture(EngineInterface engine, RenderContext context)
		{
			if (!Model.Textures.Any()) return new List<Rectangle>();
			// Combine all the textures into one long texture
			var textures = Model.Textures.Select(x => CreateBitmap(x.Header.Width, x.Header.Height, x.Data, x.Palette, x.Header.Flags.HasFlag(TextureFlags.Masked))).ToList();

			var width = textures.Max(x => x.Width);
			var height = textures.Max(x => x.Height);

			var rectangles = new List<Rectangle>();
			_originalRectangles = new List<Rectangle>(textures.Count);

			var data = new byte[textures.Count][];
			var i = 0;
			foreach (var bitmap in textures)
			{
				var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
				using (var g = Graphics.FromImage(bmp))
				{
					g.DrawImage(bitmap, new Rectangle(0, 0, width, height));

					_originalRectangles.Add(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
					var lb = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					data[i] = new byte[lb.Stride * lb.Height];
					Marshal.Copy(lb.Scan0, data[i], 0, data[i].Length);
					bmp.UnlockBits(lb);
				}

				i++;
				bmp.Dispose();
			}

			rectangles = _originalRectangles;

			_textureResource = engine.UploadTexture(TextureName, width, height, data, TextureSampleType.Standard, (uint)textures.Count);

			return rectangles;
		}
		public void CreateResources(EngineInterface engine, RenderContext context)
		{
			_originalRectangles = CreateTexuture(engine, context);
			_buffer = engine.CreateBuffer();
			Init();
		}
		private void Init()
		{
			_skins = Model.Skins.Select(x => x.Textures.Select(x => (uint)x).ToArray()).ToArray();

			_meshRanges.Clear();
			HasAdditive = false;
			var texHeight = _originalRectangles.Max(x => x.Bottom);
			var texWidth = _originalRectangles.Max(x => x.Right);
			var maxTexSize = new Vector2(texWidth, texHeight);
			var vertices = new List<VertexModel3>();
			var indices = new List<uint>();

			var wireframeIndices = new List<uint>();


			var _bodyPartIndices1 = new uint[Model.BodyParts.Count][];

			uint vi = 0;
			var skinMax = 0;

			var skin = Model.Skins[skinMax].Textures;
			for (var bpi = 0; bpi < Model.BodyParts.Count; bpi++)
			{
				var part = Model.BodyParts[bpi];
				_bodyPartIndices1[bpi] = new uint[part.Models.Length];

				for (var mdlIndex = 0; mdlIndex < part.Models.Length; mdlIndex++)
				{
					var model = part.Models[mdlIndex];
					_bodyPartIndices1[bpi][mdlIndex] = (uint)model.Meshes.Sum(x => x.Vertices.Length);
					foreach (var mesh in model.Meshes)
					{
						var texId = skin[mesh.Header.SkinRef];
						var meshStart = vi;
						var additive = texId >= 0 && texId < Model.Textures.Count && Model.Textures[texId].Header.Flags.HasFlag(TextureFlags.Additive);
						for (var i = 0; i < mesh.Vertices.Length; i++)
						{
							var x = mesh.Vertices[i];
							x.Texture = new Vector2(float.IsNaN(x.Texture.X) ? x.Vertex.X : x.Texture.X, float.IsNaN(x.Texture.Y) ? x.Vertex.Z : x.Texture.Y);
							var origRect = _originalRectangles.Count > texId ? _originalRectangles[texId] : Rectangle.Empty;

							var coeff = maxTexSize / new Vector2(origRect.Width, origRect.Height);
							var texturePosition = new Vector2((x.Texture.X / maxTexSize.X) * coeff.X, (x.Texture.Y / maxTexSize.Y) * coeff.Y);
							vertices.Add(new VertexModel3
							{
								Position = x.Vertex,
								Normal = x.Normal,
								Texture = texturePosition,
								TextureLayer = (uint)mesh.Header.SkinRef,
								Bone = (uint)x.VertexBone,
								Flags = Flags // Always 0 during ctor
							});
							indices.Add(vi);
							wireframeIndices.Add(vi);
							wireframeIndices.Add(i % 3 == 2 ? vi - 2 : vi + 1);
							vi++;
						}
						_meshRanges.Add(new MeshRange(bpi, mdlIndex, (uint)meshStart, (uint)mesh.Vertices.Length, additive));
						HasAdditive |= additive;
					}
				}

			}
			_bodyPartIndices = _bodyPartIndices1;

			var flatIndices = new uint[vi + wireframeIndices.Count];
			var currentIndexCount = 0;
			Array.Copy(indices.ToArray(), 0, flatIndices, 0, indices.Count);
			currentIndexCount = indices.Count;
			Array.Copy(wireframeIndices.ToArray(), 0, flatIndices, currentIndexCount, wireframeIndices.Count);

			_buffer.Update(vertices, flatIndices);

			_numTexturedIndices = (uint)(flatIndices.Length - wireframeIndices.Count);
			_numWireframeIndices = (uint)wireframeIndices.Count;
		}

		public void Render(RenderContext context, IPipeline pipeline, IViewport viewport, CommandList cl, int skinId, int bodyGroup)
		{
			_buffer.Bind(cl, 0);

			if (pipeline.Type == PipelineType.TexturedModel || pipeline.Type == PipelineType.TexturedModelAdditive)
			{
				_textureResource.BindTo(cl, 1);

				// Pick one model per body part, as the body group says, then draw its meshes that belong to this pass
				var additive = pipeline.Type == PipelineType.TexturedModelAdditive;
				var selected = new HashSet<(int, int)>();
				var bodyPartId = bodyGroup;
				for (var bp = 0; bp < _bodyPartIndices.Length; bp++)
				{
					var count = _bodyPartIndices[bp].Length;
					if (count == 0) continue;
					selected.Add((bp, bodyPartId % count));
					bodyPartId /= count;
				}

				foreach (var mesh in _meshRanges)
				{
					if (mesh.Additive != additive || !selected.Contains((mesh.BodyPart, mesh.Model))) continue;
					cl.DrawIndexed(mesh.Count, 1, mesh.Start, 0, 0);
				}
			}
			else if (pipeline.Type == PipelineType.WireframeModel)
			{
				cl.DrawIndexed(_numWireframeIndices, 1, _numTexturedIndices, 0, 0);
			}
		}

		public void DestroyResources()
		{
			_buffer?.Dispose();
			_textureResource?.Dispose();
		}

		public void Dispose()
		{
			//
		}

		public uint[] GetLayerSet(int skinId)
		{
			var skin = skinId % Model.Skins.Count;

			return _skins[skin];
		}
		// This required to solve source engine textures loading issue. Source engie has separate material for each model
		public string[] GetTextureName()
		{
			return null ;
		}

		public void SetTexture(Rendering.Resources.Texture[] textures)
		{
			return;
		}
	}
}