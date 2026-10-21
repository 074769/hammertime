using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sledge.Providers.Model.Mdl10.Combine
{
	/// <summary>
	/// One model entity to be baked into a combined model.
	/// </summary>
	public class Placement
	{
		public string ModelPath;
		public string Name;
		/// <summary>Entity origin in combined-model space.</summary>
		public Vector3 Origin;
		/// <summary>Entity angles in degrees, as stored in the map: pitch, yaw, roll.</summary>
		public Vector3 Angles;
		public int Body;
		public int Skin;
		public int RenderMode;
		public int RenderAmt = 255;
		public int RenderFx;
		public Vector3 RenderColor = Vector3.Zero;

		/// <summary>Members with the same key can share one combined model.</summary>
		public string RenderKey => $"{RenderMode}|{RenderAmt}|{RenderFx}|{RenderColor.X}|{RenderColor.Y}|{RenderColor.Z}";
	}

	public static class PlacementMath
	{
		/// <summary>
		/// Applies an entity placement (origin and angles only) to model-space geometry.
		/// Uses the same transform the model renderer applies to placed entities
		/// (MdlModelRenderable.GetModelTransformation), so the baked model matches the originals.
		/// Map angles are pitch, yaw, roll in degrees.
		/// </summary>
		public static BindPoseMesh Place(BindPoseMesh mesh, Vector3 origin, Vector3 anglesDeg)
		{
			var a = anglesDeg * (float)Math.PI / 180f;
			var yawMatrix = Matrix4x4.CreateRotationY(-a.X);
			var pitchMatrix = Matrix4x4.CreateRotationX(a.Z);
			var rollMatrix = Matrix4x4.CreateRotationZ(a.Y);
			var rotation = pitchMatrix * yawMatrix * rollMatrix;

			var result = new BindPoseMesh();
			foreach (var v in mesh.Vertices)
			{
				// Scale is not applied: GoldSrc model scale is set in the QC ($scale) and is already in the compiled geometry.
				result.Vertices.Add(new BindVertex
				{
					Position = Vector3.Transform(v.Position, rotation) + origin,
					Normal = BindPoseMesh.SafeNormalize(Vector3.TransformNormal(v.Normal, rotation)),
					Uv = v.Uv,
					TextureIndex = v.TextureIndex,
				});
			}
			result.RecalculateBounds();
			return result;
		}
	}

	/// <summary>
	/// A member prepared for planning: its placed geometry plus the limits it uses.
	/// </summary>
	public class PreparedMember
	{
		public Placement Placement;
		public BindPoseMesh Mesh;
		public List<int> SrcTex;
		/// <summary>Identity of each texture this member uses; shared textures count once per model.</summary>
		public List<string> TextureKeys;
		public int UniqueVertices;
	}

	public class CombinedModelPlan
	{
		public string RenderKey;
		public List<PreparedMember> Members = new List<PreparedMember>();
		public int TextureCount => Members.SelectMany(m => m.TextureKeys).Distinct().Count();
	}

	public static class CombinePlanner
	{
		/// <summary>Body parts per .mdl (MAXSTUDIOBODYPARTS).</summary>
		public const int MaxBodyParts = 32;
		/// <summary>Vertices per submodel; each member is one submodel (MAXSTUDIOVERTS).</summary>
		public const int MaxVerticesPerSubmodel = 2048;
		/// <summary>Textures per .mdl (MAXSTUDIOSKINS).</summary>
		public const int MaxTextures = 100;

		/// <summary>
		/// Groups members by render settings, then packs each group into models without
		/// exceeding the body part and texture caps. Throws if one member cannot fit on its own.
		/// </summary>
		public static List<CombinedModelPlan> Plan(IEnumerable<PreparedMember> members,
			int maxBodyParts = MaxBodyParts, int maxTextures = MaxTextures, int maxVertices = MaxVerticesPerSubmodel)
		{
			var plans = new List<CombinedModelPlan>();
			foreach (var group in members.GroupBy(m => m.Placement.RenderKey))
			{
				CombinedModelPlan current = null;
				foreach (var m in group)
				{
					if (m.UniqueVertices > maxVertices)
						throw new InvalidOperationException(
							$"'{m.Placement.Name}' has {m.UniqueVertices} vertices, above the {maxVertices} per-submodel limit.");
					if (m.SrcTex.Count > maxTextures)
						throw new InvalidOperationException(
							$"'{m.Placement.Name}' uses {m.SrcTex.Count} textures, above the {maxTextures} per-model limit.");

					var keys = current == null ? new HashSet<string>() : new HashSet<string>(current.Members.SelectMany(x => x.TextureKeys));
					int newTextures = m.TextureKeys.Count(k => !keys.Contains(k));
					bool full = current == null
						|| current.Members.Count + 1 > maxBodyParts
						|| keys.Count + newTextures > maxTextures;
					if (full)
					{
						current = new CombinedModelPlan { RenderKey = group.Key };
						plans.Add(current);
					}
					current.Members.Add(m);
				}
			}
			return plans;
		}
	}
}
