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
		/// Applies an entity placement (origin and angles only) to model-space geometry using the engine's AngleMatrix
		/// convention: yaw about Z, pitch as elevation (positive is nose down), roll about the forward axis.
		/// </summary>
		/// <param name="negatePitch">Flip the pitch sign. Only for checking the in-game result.</param>
		public static BindPoseMesh Place(BindPoseMesh mesh, Vector3 origin, Vector3 anglesDeg, bool negatePitch = false)
		{
			var pitch = (negatePitch ? -anglesDeg.X : anglesDeg.X) * (float)Math.PI / 180f;
			var yaw = anglesDeg.Y * (float)Math.PI / 180f;
			var roll = anglesDeg.Z * (float)Math.PI / 180f;

			float cp = (float)Math.Cos(pitch), sp = (float)Math.Sin(pitch);
			float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
			float cr = (float)Math.Cos(roll), sr = (float)Math.Sin(roll);

			// Columns of the rotation: where the model's X, Y and Z axes end up.
			var fwd = new Vector3(cp * cy, cp * sy, -sp);
			var right = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
			var up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);

			var result = new BindPoseMesh();
			foreach (var v in mesh.Vertices)
			{
				// Scale is not applied: GoldSrc model scale is set in the QC ($scale) and is already in the compiled geometry.
				var p = v.Position.X * fwd + v.Position.Y * right + v.Position.Z * up + origin;
				var n = v.Normal.X * fwd + v.Normal.Y * right + v.Normal.Z * up;
				result.Vertices.Add(new BindVertex
				{
					Position = p,
					Normal = BindPoseMesh.SafeNormalize(n),
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
		public int UniqueVertices;
	}

	public class CombinedModelPlan
	{
		public string RenderKey;
		public List<PreparedMember> Members = new List<PreparedMember>();
		public int TextureCount => Members.Sum(m => m.SrcTex.Count);
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

					bool full = current == null
						|| current.Members.Count + 1 > maxBodyParts
						|| current.TextureCount + m.SrcTex.Count > maxTextures;
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
