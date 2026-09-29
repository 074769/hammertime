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

		public static Matrix4x4 AngleMatrix(Vector3 pitchYawRoll)
		{
			var rad = pitchYawRoll * (float) Math.PI / 180f;
			return Matrix4x4.CreateFromYawPitchRoll(rad.X, rad.Z, rad.Y);
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
