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
	/// independent of each other without duplicating the projection/cone code.
	/// </summary>
	internal static class MotionWidgetDrawing
	{
		public const float NearPlane = 1f;
		public const float HeadRadiusFraction = 0.45f;  // of the head length
		public const int HeadSides = 8;
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

		/// <summary>A cone with its apex at <paramref name="tip"/>, opening backwards along -direction.</summary>
		public static void DrawCone(PerspectiveCamera camera, I2DRenderer im, Vector3 tip, Vector3 direction, float headLength, Color color)
		{
			BuildBasis(direction, out var u, out var v);

			var baseCenter = tip - direction * headLength;
			var radius = headLength * HeadRadiusFraction;

			Vector3 prev = baseCenter + u * radius;
			for (var i = 1; i <= HeadSides; i++)
			{
				var a = 2 * Math.PI * i / HeadSides;
				var p = baseCenter + (u * (float) Math.Cos(a) + v * (float) Math.Sin(a)) * radius;

				DrawLine3D(camera, im, prev, p, color, StrokeWidth);  // base ring
				if (i % 2 == 0) DrawLine3D(camera, im, p, tip, color, StrokeWidth); // ribs to the apex
				prev = p;
			}
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
