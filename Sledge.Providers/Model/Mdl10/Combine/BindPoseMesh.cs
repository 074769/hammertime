using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.Providers.Model.Mdl10.Format;
// Aliases avoid clashes with the Sledge.Providers.Model and Sledge.Providers.Texture namespaces.
using SubModel = Sledge.Providers.Model.Mdl10.Format.Model;
using MdlTexture = Sledge.Providers.Model.Mdl10.Format.Texture;

namespace Sledge.Providers.Model.Mdl10.Combine
{
	/// <summary>
	/// One corner of a triangle, in model space, in the bind (reference) pose.
	/// </summary>
	public struct BindVertex
	{
		public Vector3 Position;
		public Vector3 Normal;
		/// <summary>UV normalised to 0..1 over the source texture.</summary>
		public Vector2 Uv;
		/// <summary>Index into MdlFile.Textures of the texture this corner uses.</summary>
		public int TextureIndex;
	}

	/// <summary>
	/// Static triangle list extracted from one .mdl for a given body and skin.
	/// Vertices are stored as a flat triangle list: 3 consecutive entries per triangle.
	/// </summary>
	public class BindPoseMesh
	{
		public List<BindVertex> Vertices { get; } = new List<BindVertex>();

		public int TriangleCount => Vertices.Count / 3;

		public Vector3 BoundsMin { get; private set; }
		public Vector3 BoundsMax { get; private set; }

		public IEnumerable<int> TextureIndices => Vertices.Select(v => v.TextureIndex).Distinct();

		internal void RecalculateBounds()
		{
			if (Vertices.Count == 0)
			{
				BoundsMin = BoundsMax = Vector3.Zero;
				return;
			}
			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);
			foreach (var v in Vertices)
			{
				min = Vector3.Min(min, v.Position);
				max = Vector3.Max(max, v.Position);
			}
			BoundsMin = min;
			BoundsMax = max;
		}

		/// <summary>
		/// Returns a copy with positions and normals transformed by <paramref name="transform"/>.
		/// Normals use the upper 3x3 only, so this is correct for rotation plus uniform scale,
		/// which is all GoldSrc entities use.
		/// </summary>
		public BindPoseMesh ApplyTransform(Matrix4x4 transform)
		{
			var result = new BindPoseMesh();
			foreach (var v in Vertices)
			{
				result.Vertices.Add(new BindVertex
				{
					Position = Vector3.Transform(v.Position, transform),
					Normal = SafeNormalize(Vector3.TransformNormal(v.Normal, transform)),
					Uv = v.Uv,
					TextureIndex = v.TextureIndex,
				});
			}
			result.RecalculateBounds();
			return result;
		}

		internal static Vector3 SafeNormalize(Vector3 v)
		{
			var len = v.Length();
			return len > 1e-6f ? v / len : Vector3.UnitZ;
		}
	}

	/// <summary>
	/// Extracts the bind-pose geometry of a GoldSrc .mdl for one body and skin choice.
	/// Only the reference pose is used, so animation frames are ignored by design.
	/// </summary>
	public static class BindPoseExtractor
	{
		/// <param name="model">A parsed GoldSrc .mdl.</param>
		/// <param name="body">Body value as used by the entity's "body" key (mixed-radix over body parts, same as MdlModel.Render).</param>
		/// <param name="skin">Skin family index, as used by the entity's "skin" key.</param>
		public static BindPoseMesh Extract(MdlFile model, int body = 0, int skin = 0, int sequence = -1)
		{
			var result = new BindPoseMesh();
			if (model == null || model.Bones == null || model.BodyParts == null) return result;

			var boneWorld = BuildPose(model, sequence);
			var skinFamily = model.Skins != null && model.Skins.Count > 0
				? model.Skins[skin % model.Skins.Count].Textures
				: new short[0];

			foreach (var part in model.BodyParts)
			{
				if (!ChooseSubmodel(part, ref body, out var chosen)) continue;

				foreach (var mesh in chosen.Meshes)
				{
					var skinRef = mesh.Header.SkinRef;
					var textureIndex = skinRef >= 0 && skinRef < skinFamily.Length ? skinFamily[skinRef] : skinRef;
					var texture = model.Textures != null && textureIndex >= 0 && textureIndex < model.Textures.Count
						? model.Textures[textureIndex]
						: default(MdlTexture);
					var texW = texture.Header.Width > 0 ? texture.Header.Width : 1;
					var texH = texture.Header.Height > 0 ? texture.Header.Height : 1;

					// The reader stores triangles expanded (3 corners each), so walk them in threes.
					var corners = mesh.Vertices.Length - (mesh.Vertices.Length % 3);
					for (var i = 0; i < corners; i += 3)
					{
						var tri = new BindVertex[3];
						for (var c = 0; c < 3; c++)
						{
							var v = mesh.Vertices[i + c];
							tri[c] = new BindVertex
							{
								Position = Vector3.Transform(v.Vertex, boneWorld[v.VertexBone]),
								Normal = BindPoseMesh.SafeNormalize(Vector3.TransformNormal(v.Normal, boneWorld[v.NormalBone])),
								Uv = new Vector2(v.Texture.X / texW, v.Texture.Y / texH),
								TextureIndex = textureIndex,
							};
						}

						// Zero-area triangles include the default-filled tail when the header over-counts.
						var cross = Vector3.Cross(tri[1].Position - tri[0].Position, tri[2].Position - tri[0].Position);
						if (cross.LengthSquared() < 1e-12f) continue;

						result.Vertices.AddRange(tri);
					}
				}
			}

			result.RecalculateBounds();
			return result;
		}

		/// <summary>
		/// Picks the submodel of a body part. Mirrors the selection in MdlModel.Render so the
		/// baked result matches what the viewer shows for the same body value.
		/// </summary>
		private static bool ChooseSubmodel(BodyPart part, ref int bodyValue, out SubModel chosen)
		{
			chosen = default(SubModel);
			if (part.Models == null || part.Models.Length == 0) return false;
			var index = bodyValue % part.Models.Length;
			bodyValue /= part.Models.Length;
			chosen = part.Models[index];
			return true;
		}

		/// <summary>
		/// World-space bone matrices for the reference pose. Uses the same rotation convention
		/// as MdlFile.GetTransforms (angles swapped between yaw and pitch), without frame deltas.
		/// </summary>
		/// <summary>
		/// Index of the sequence the renderer starts an entity on: "idle", else the first sequence starting with "idle", else 0.
		/// </summary>
		public static int DefaultSequence(MdlFile model)
		{
			if (model.Sequences == null || model.Sequences.Count == 0) return -1;
			for (var i = 0; i < model.Sequences.Count; i++)
				if (string.Equals(model.Sequences[i].Header.Name, "idle", StringComparison.InvariantCultureIgnoreCase)) return i;
			for (var i = 0; i < model.Sequences.Count; i++)
				if (model.Sequences[i].Header.Name != null && model.Sequences[i].Header.Name.StartsWith("idle", StringComparison.InvariantCultureIgnoreCase)) return i;
			return 0;
		}

		/// <summary>
		/// Bone transforms for frame 0 of a sequence, the pose an entity shows when it starts. Falls back to the reference pose.
		/// </summary>
		private static Matrix4x4[] BuildPose(MdlFile model, int sequence)
		{
			if (sequence < 0 || sequence >= (model.Sequences?.Count ?? 0)) sequence = DefaultSequence(model);
			if (sequence < 0 || model.Sequences[sequence].Header.NumFrames <= 0) return BuildBindPose(model);

			var transforms = new Matrix4x4[model.Bones.Count];
			model.GetTransforms(sequence, 0, 0, ref transforms);
			return transforms;
		}

		private static Matrix4x4[] BuildBindPose(MdlFile model)
		{
			var count = model.Bones.Count;
			var local = new Matrix4x4[count];
			for (var i = 0; i < count; i++)
			{
				var bone = model.Bones[i];
				var q = Quaternion.CreateFromYawPitchRoll(bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z);
				q = new Quaternion(q.Y, q.X, q.Z, q.W);
				var mat = Matrix4x4.CreateFromQuaternion(q);
				mat.Translation = bone.Position;
				local[i] = mat;
			}

			var world = new Matrix4x4[count];
			for (var i = 0; i < count; i++)
			{
				var mat = local[i];
				var parent = model.Bones[i].Parent;
				var guard = 0;
				while (parent >= 0 && parent < count && guard++ < count)
				{
					mat = mat * local[parent];
					parent = model.Bones[parent].Parent;
				}
				world[i] = mat;
			}
			return world;
		}
	}
}
