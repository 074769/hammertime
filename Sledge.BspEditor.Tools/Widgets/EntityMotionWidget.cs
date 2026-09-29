using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.GameData;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;
using static Sledge.BspEditor.Tools.Widgets.MotionWidgetDrawing;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Widgets
{
	/// <summary>
	/// 3D-viewport counterpart of the rotation half of EntityMovementOverlay. For every
	/// selected entity whose FGD class exposes the "X Axis" / "Y Axis" spawnflag pair
	/// (func_rotating, func_door_rotating, func_platrot, momentary_rot_button, ...) it
	/// draws a 3D rotation arrow around the entity's rotation axis. When the entity has a
	/// finite rotation ("distance" or "rotation" keyvalue, in degrees) the arc runs from the
	/// start angle to the end angle exactly - the arrowhead's apex sits on the end radial
	/// line, not past it - with a straight line from the pivot to the arc's start, which
	/// lies on the entity's own rest direction. Entities that spin continuously get an open
	/// arc with only the arrow head.
	///
	/// The straight movement arrow is a separate widget (<see cref="EntityMoveArrowWidget"/>).
	///
	/// A second, smaller arrow sits at the arc's midpoint so the direction still reads when
	/// the end is occluded or off-screen. Widget size always tracks the entity's own
	/// bounding box - it does not grow with camera distance.
	///
	/// Everything is driven by the FGD and the entity's own keyvalues, nothing is
	/// hardcoded to classnames. Lines are projected and drawn as an overlay (like the
	/// Move/Rotate widgets), so they stay visible even when the entity's center is
	/// buried inside its own brushes.
	/// </summary>
	public class EntityMotionWidget : BaseTool
	{
		public static readonly Capability EntityMotionWidgetCapability = Capability.Create("EntityMotionWidget");
		public override Capability ToolCapability => EntityMotionWidgetCapability;

		private const float RotateArrowScale = 0.8f; // arc radius, as a multiple of entity size
		private const double ContinuousArcDegrees = 300; // open arc for entities that spin forever

		public EntityMotionWidget()
		{
			Usage = ToolUsage.View3D;
			Active = false;
		}

		public override Image GetIcon() { return null; }
		public override string GetName() { return "EntityMotionWidget"; }

		protected override void Render(MapDocument document, IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
		{
			if (!document.Selection.IsEmpty)
			{
				RenderSelection(document, camera, im);
			}
			base.Render(document, viewport, camera, im);
		}

		private void RenderSelection(MapDocument document, PerspectiveCamera camera, I2DRenderer im)
		{
			if (!TryGetGameData(document, out var gameData)) return;

			foreach (var ed in GetSelectedEntities(document))
			{
				var data = ed.EntityData;
				var cls = gameData.GetClass(data.Name);
				if (cls == null) continue;

				var box = ed.BoundingBox;
				if (box == null) continue;
				var min = Math.Min(box.Width, Math.Min(box.Height, box.Length));
				var max = Math.Max(box.Width, Math.Max(box.Height, box.Length));
				if (min <= 0) continue;

				var rotateAxis = GetRotationAxis(cls, data);
				if (!rotateAxis.HasValue) continue;

				var color = ed.Color?.Color ?? Color.White;
				var pivot = GetRotationPivot(ed, box.Center);
				// Use the entity's largest dimension, not the smallest - a 20x120
				// block should draw an arc sized off the 120, so it reads as the
				// actual sweep of the object rather than looking undersized.
				var radius = max * RotateArrowScale; // object-size only - never grows with camera distance
				var reversed = IsReversed(cls, data);
				var sweep = GetSweepDegrees(cls, data);
				var startDir = GetRotationStartDirection(box, pivot, rotateAxis.Value);
				DrawRotation3D(camera, im, pivot, rotateAxis.Value, startDir, radius, color, reversed, sweep);
			}
		}

		// --- Rotation data (mirrors EntityMovementOverlay) ---------------------

		private static Vector3? GetRotationAxis(GameDataObject cls, EntityData data)
		{
			var flagsProp = FindProperty(cls, "spawnflags");
			if (flagsProp == null) return null;

			var xAxisBit = FindFlagBit(flagsProp, "x axis");
			var yAxisBit = FindFlagBit(flagsProp, "y axis");
			if (xAxisBit == null && yAxisBit == null) return null;

			var local = Vector3.UnitZ;
			if (xAxisBit.HasValue && (data.Flags & xAxisBit.Value) != 0) local = Vector3.UnitX;
			else if (yAxisBit.HasValue && (data.Flags & yAxisBit.Value) != 0) local = Vector3.UnitY;

			var ang = data.GetVector3("angles");
			if (!ang.HasValue) return local;

			return Vector3.Transform(local, AngleMatrix(ang.Value));
		}

		private static bool IsReversed(GameDataObject cls, EntityData data)
		{
			var flagsProp = FindProperty(cls, "spawnflags");
			var reverseBit = flagsProp == null ? null : FindFlagBit(flagsProp, "reverse");
			return reverseBit.HasValue && (data.Flags & reverseBit.Value) != 0;
		}

		/// <summary>
		/// How far (in degrees) the entity rotates before stopping, if the class has a
		/// finite rotation keyvalue. Null means it spins continuously / unknown.
		/// </summary>
		private static float? GetSweepDegrees(GameDataObject cls, EntityData data)
		{
			foreach (var key in new[] { "distance", "rotation" })
			{
				var prop = FindProperty(cls, key);
				if (prop == null) continue;

				float.TryParse(prop.DefaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var def);
				var value = data.Get(key, def);
				if (Math.Abs(value) >= 0.01f) return value;
			}
			return null;
		}

		/// <summary>
		/// Rotating brush entities turn around their origin brush, not their bounding box
		/// center, so use the origin brush when there is one.
		/// </summary>
		private static Vector3 GetRotationPivot(MapEntity entity, Vector3 fallback)
		{
			var originBrush = entity.FindAll().OfType<Solid>()
				.FirstOrDefault(s => s.Faces.Any() && s.Faces.All(f => string.Equals(f.Texture.Name, "origin", StringComparison.InvariantCultureIgnoreCase)));
			return originBrush?.BoundingBox?.Center ?? fallback;
		}

		/// <summary>
		/// The direction (perpendicular to the rotation axis) from the pivot toward where the
		/// entity currently sits - i.e. its rest position, which is where the swing starts.
		/// Uses the bounding box center when it is off the axis (doors, gates, arms hinged at
		/// one edge); otherwise falls back to the bounding box corner farthest from the axis.
		/// Null when the entity is perfectly symmetric around the axis (any start is as good
		/// as any other).
		/// </summary>
		private static Vector3? GetRotationStartDirection(Box box, Vector3 pivot, Vector3 axis)
		{
			axis = Vector3.Normalize(axis);
			Vector3 PerpToAxis(Vector3 d) => d - axis * Vector3.Dot(d, axis);

			var toCenter = PerpToAxis(box.Center - pivot);
			if (toCenter.Length() > 0.5f) return Vector3.Normalize(toCenter);

			Vector3? best = null;
			var bestLength = 0.5f;
			foreach (var corner in box.GetBoxPoints())
			{
				var d = PerpToAxis(corner - pivot);
				var len = d.Length();
				if (len > bestLength)
				{
					bestLength = len;
					best = d / len;
				}
			}
			return best;
		}

		private static int? FindFlagBit(Property flagsProp, string descriptionContains)
		{
			var opt = flagsProp.Options.FirstOrDefault(x => (x.Description ?? "").IndexOf(descriptionContains, StringComparison.OrdinalIgnoreCase) >= 0);
			if (opt == null) return null;
			return int.TryParse(opt.Key, out var v) ? v : (int?) null;
		}

		// --- Drawing ---------------------------------------------------------

		/// <summary>
		/// A 3D arc around the rotation axis. With a finite sweep it runs from the start
		/// angle to the end angle exactly - the arc is trimmed just short of the end so
		/// the arrowhead's apex lands precisely on the end radial line rather than past
		/// it - starting on the entity's rest direction (see GetRotationStartDirection) and
		/// joined to the pivot by a straight radial line of the same radius as the arc. A second, smaller
		/// arrow sits at the arc's midpoint. Entities without a finite sweep get an open
		/// arc with just the arrow head.
		/// </summary>
		private static void DrawRotation3D(PerspectiveCamera camera, I2DRenderer im, Vector3 pivot, Vector3 axis, Vector3? startDirection, float radius, Color color, bool reversed, float? sweepDegrees)
		{
			axis = Vector3.Normalize(axis);
			BuildBasis(axis, out var u, out var v);
			if (startDirection.HasValue)
			{
				// Start the arc on the entity's own rest direction so the swing begins where
				// the entity actually is (u x v is still +axis, so the sweep sense is unchanged).
				u = startDirection.Value;
				v = Vector3.Cross(axis, u);
			}

			var finite = sweepDegrees.HasValue;
			var sweepMagnitude = (finite ? Math.Abs(sweepDegrees.Value) : ContinuousArcDegrees) * Math.PI / 180.0;
			var sign = reversed ? -1.0 : 1.0;
			var sweep = sweepMagnitude * sign;

			Vector3 PointAt(double t, float r) => pivot + (u * (float) Math.Cos(t) + v * (float) Math.Sin(t)) * r;
			Vector3 TangentAt(double t) => Vector3.Normalize((-u * (float) Math.Sin(t) + v * (float) Math.Cos(t)) * (float) sign);

			var headLength = radius * 0.3f;
			// Trim the arc short by the head's length (in angle terms) so it meets the
			// cone's base instead of the cone overshooting past the true end angle.
			var trimAngle = Math.Min(Math.Abs(sweep) * 0.5, radius > 0.001f ? headLength / radius : 0.0);
			var arcEnd = sweep - sign * trimAngle;

			var segments = Math.Max(6, Math.Min(64, (int) Math.Ceiling(Math.Abs(arcEnd) * 180 / Math.PI / 8)));
			var prev = PointAt(0, radius);
			for (var i = 1; i <= segments; i++)
			{
				var t = arcEnd * i / segments;
				var p = PointAt(t, radius);
				DrawLine3D(camera, im, prev, p, color, StrokeWidth * 1.5f);
				prev = p;
			}

			// Arrowhead apex sits exactly on the true end angle, at the same radius as the arc.
			var tip = PointAt(sweep, radius);
			DrawCone(camera, im, tip, TangentAt(sweep), headLength, color);

			// Mid-arc arrow, so direction reads even when the end is occluded or off-screen.
			if (Math.Abs(sweep) > 0.01)
			{
				var midT = sweep * 0.5;
				DrawCone(camera, im, PointAt(midT, radius), TangentAt(midT), headLength * 0.7f, color);
			}

			// Rotation axis, so the plane the arc lives in is unambiguous
			DrawLine3D(camera, im, pivot - axis * radius * 0.5f, pivot + axis * radius * 0.5f, Color.FromArgb(150, color), 1f);

			if (finite)
			{
				// Straight line from the pivot out to where the swing starts (the entity's rest
				// direction); the arc leaves from its far end and the arrowhead marks the end angle.
				DrawLine3D(camera, im, pivot, PointAt(0, radius), color, StrokeWidth * 1.5f);

				var label = Math.Abs(sweepDegrees.Value).ToString("0.##", CultureInfo.InvariantCulture) + "\u00B0";
				var mid = PointAt(sweep / 2, radius * 1.25f);
				if (Vector3.Dot(mid - camera.EyeLocation, camera.Direction) > NearPlane)
				{
					var s = camera.WorldToScreen(mid).ToVector2();
					var size = im.CalcTextSize(FontType.Bold, label);
					im.AddText(new Vector2(s.X - size.X / 2, s.Y - size.Y / 2), color, FontType.Bold, label);
				}
			}
		}
	}
}
