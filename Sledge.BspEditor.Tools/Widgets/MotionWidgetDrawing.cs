using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.DataStructures.GameData;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Widgets
{
	/// <summary>
	/// Drawing and lookup helpers shared by <see cref="EntityMoveArrowWidget"/> and
	/// <see cref="EntityMotionWidget"/> (the rotation arc), so the two widgets stay
	/// independent of each other without duplicating the projection/arrowhead code.
	/// </summary>
	internal static class MotionWidgetDrawing
	{
		public const float NearPlane = 1f;
		public const double HeadAngleDegrees = 45;  // angle between each arrowhead barb and the shaft
		public const float HeadScale = 0.25f;         // arrowhead barbs are drawn at 25% of the requested length (75% smaller)
		public const float StrokeWidth = 2f;
		public const float OutlineExtraWidth = 2f;

		public static bool TryGetGameData(MapDocument document, out GameData gameData)
		{
			try
			{
				// Cached as an already-completed task once loaded, so this doesn't block.
				gameData = document.Environment.GetGameData().Result;
			}
			catch
			{
				gameData = null;
			}
			return gameData != null;
		}

		public static IEnumerable<MapEntity> GetSelectedEntities(MapDocument document)
		{
			return document.Selection.OfType<MapEntity>()
				.Where(x => x.EntityData != null)
				.Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden))
				.Distinct();
		}

		public static Property FindProperty(GameDataObject cls, string name)
		{
			return cls.Properties.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// GoldSrc/Quake AngleMatrix for an entity's "angles" (pitch yaw roll, degrees), laid out as
		/// a System.Numerics row-vector matrix: row 1 = forward, row 2 = left, row 3 = up, so
		/// Vector3.Transform(local, m) takes a vector from entity space into world space.
		/// This is the same basis Billboard.geom.hlsl builds for Oriented sprites, which
		/// oriented_sprite_debbuging.map's 13 reference arrows confirm. Positive pitch looks DOWN
		/// (angles "-90 0 0" is straight up, "90 0 0" straight down - what AngleControl writes).
		/// The old CreateFromYawPitchRoll version applied yaw, roll, pitch in the wrong order about
		/// world axes, so any combined pitch+yaw came out wrong.
		/// </summary>
		public static Matrix4x4 AngleMatrix(Vector3 pitchYawRoll)
		{
			var rad = pitchYawRoll * (float) Math.PI / 180f;
			float sp = (float) Math.Sin(rad.X), cp = (float) Math.Cos(rad.X);
			float sy = (float) Math.Sin(rad.Y), cy = (float) Math.Cos(rad.Y);
			float sr = (float) Math.Sin(rad.Z), cr = (float) Math.Cos(rad.Z);

			var forward = new Vector3(cp * cy, cp * sy, -sp);
			var left = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
			var up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);

			return new Matrix4x4(
				forward.X, forward.Y, forward.Z, 0,
				left.X, left.Y, left.Z, 0,
				up.X, up.Y, up.Z, 0,
				0, 0, 0, 1);
		}

		public static Vector3 AngleToDirection(Vector3 pitchYawRoll)
		{
			return Vector3.Transform(Vector3.UnitX, AngleMatrix(pitchYawRoll));
		}

		/// <summary>
		/// An open, wireframe arrowhead (no cone): four barbs of length <paramref name="barbLength"/>
		/// running back from <paramref name="tip"/> at 45 degrees to the shaft. Two barbs lie along
		/// <paramref name="sideA"/> and two along <paramref name="sideB"/> (unit vectors perpendicular
		/// to <paramref name="direction"/> and to each other), so the head reads as a "V" from either
		/// the top or the side and as an "X" when looked at along the shaft at an angle.
		/// </summary>
		public static void DrawArrowHead(PerspectiveCamera camera, I2DRenderer im, Vector3 tip, Vector3 direction, Vector3 sideA, Vector3 sideB, float barbLength, Color color)
		{
			barbLength *= HeadScale;
			var angle = HeadAngleDegrees * Math.PI / 180.0;
			var back = -direction * (float) Math.Cos(angle) * barbLength;
			var spread = (float) Math.Sin(angle) * barbLength;

			DrawLine3D(camera, im, tip, tip + back + sideA * spread, color, StrokeWidth);
			DrawLine3D(camera, im, tip, tip + back - sideA * spread, color, StrokeWidth);
			DrawLine3D(camera, im, tip, tip + back + sideB * spread, color, StrokeWidth);
			DrawLine3D(camera, im, tip, tip + back - sideB * spread, color, StrokeWidth);
		}

		/// <summary>Arrowhead whose barb planes are picked from the direction alone (horizontal + vertical for a horizontal direction).</summary>
		public static void DrawArrowHead(PerspectiveCamera camera, I2DRenderer im, Vector3 tip, Vector3 direction, float barbLength, Color color)
		{
			BuildBasis(direction, out var u, out var v);
			DrawArrowHead(camera, im, tip, direction, u, v, barbLength, color);
		}

		public static void BuildBasis(Vector3 axis, out Vector3 u, out Vector3 v)
		{
			var helper = Math.Abs(Vector3.Dot(axis, Vector3.UnitZ)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
			u = Vector3.Normalize(Vector3.Cross(axis, helper));
			v = Vector3.Cross(axis, u);
		}

		/// <summary>
		/// Clips the segment against the camera near plane (so points behind the camera never
		/// get projected), projects it, and draws it with a dark outline underneath.
		/// </summary>
		public static void DrawLine3D(PerspectiveCamera camera, I2DRenderer im, Vector3 a, Vector3 b, Color color, float width)
		{
			var eye = camera.EyeLocation;
			var fwd = camera.Direction;

			var da = Vector3.Dot(a - eye, fwd) - NearPlane;
			var db = Vector3.Dot(b - eye, fwd) - NearPlane;
			if (da < 0 && db < 0) return;
			if (da < 0) a = a + (b - a) * (da / (da - db));
			else if (db < 0) b = b + (a - b) * (db / (db - da));

			var sa = camera.WorldToScreen(a).ToVector2();
			var sb = camera.WorldToScreen(b).ToVector2();

			im.AddLine(sa, sb, Color.FromArgb(color.A, Color.Black), width + OutlineExtraWidth);
			im.AddLine(sa, sb, color, width);
		}
	}
}
