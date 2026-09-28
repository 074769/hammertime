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
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Widgets
{
	/// <summary>
	/// 3D-viewport counterpart of EntityMovementOverlay. For every selected entity it draws:
	///
	///  - a 3D arrow along the entity's "movedir" keyvalue, for entities that translate
	///    along a fixed direction (func_door, func_button, trigger_push, ...). The arrow's
	///    length is the entity's actual travel distance (its size along movedir, minus
	///    "lip" if the entity has one) so it represents how far it really moves, not just
	///    a decorative icon;
	///
	///  - a 3D rotation arrow around the entity's rotation axis, for entities whose FGD
	///    class exposes the "X Axis" / "Y Axis" spawnflag pair (func_rotating,
	///    func_door_rotating, func_platrot, momentary_rot_button, ...). When the entity
	///    has a finite rotation ("distance" or "rotation" keyvalue, in degrees) the arc
	///    runs from the start angle to the end angle exactly - the arrowhead's apex sits
	///    on the end radial line, not past it - with a thin radial line at the start and
	///    a bold radial line at the end. Entities that spin continuously get an open arc
	///    with only the arrow head.
	///
	/// Both helpers also draw a second, smaller arrow at their midpoint so the direction
	/// still reads when the tip/end is occluded or off-screen.
	///
	/// Widget size always tracks the entity's own bounding box - it does not grow with
	/// camera distance like a screen-space sprite/gizmo.
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

		private const float MoveArrowScale = 0.5f;   // fallback shaft length (x entity size) when no real travel distance can be worked out
		private const float RotateArrowScale = 0.8f; // arc radius, as a multiple of entity size
		private const float NearPlane = 1f;

		private const float HeadLengthFraction = 0.3f;   // of the shaft/arc length
		private const float HeadRadiusFraction = 0.45f;  // of the head length
		private const int HeadSides = 8;

		private const double ContinuousArcDegrees = 300; // open arc for entities that spin forever
		private const float StrokeWidth = 2f;
		private const float OutlineExtraWidth = 2f;

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

		private static void RenderSelection(MapDocument document, PerspectiveCamera camera, I2DRenderer im)
		{
			GameData gameData;
			try
			{
				// Cached as an already-completed task once loaded, so this doesn't block.
				gameData = document.Environment.GetGameData().Result;
			}
			catch
			{
				return;
			}
			if (gameData == null) return;

			var entities = document.Selection.OfType<MapEntity>()
				.Where(x => x.EntityData != null)
				.Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden))
				.Distinct();

			foreach (var ed in entities)
			{
				var data = ed.EntityData;
				var cls = gameData.GetClass(data.Name);
				if (cls == null) continue;

				var box = ed.BoundingBox;
				if (box == null) continue;
				var min = Math.Min(box.Width, Math.Min(box.Height, box.Length));
				if (min <= 0) continue;

				var color = ed.Color?.Color ?? Color.White;
				var center = box.Center;

				var moveDir = GetMoveDirection(data);
				if (moveDir.HasValue)
				{
					var length = GetMoveLength(ed, data, moveDir.Value, min);
					DrawArrow3D(camera, im, center, moveDir.Value, length, color);
				}

				var rotateAxis = GetRotationAxis(cls, data);
				if (rotateAxis.HasValue)
				{
					var pivot = GetRotationPivot(ed, center);
					var radius = min * RotateArrowScale; // object-size only - never grows with camera distance
					var reversed = IsReversed(cls, data);
					var sweep = GetSweepDegrees(cls, data);
					DrawRotation3D(camera, im, pivot, rotateAxis.Value, radius, color, reversed, sweep);
				}
			}
		}

		// --- Motion data (mirrors EntityMovementOverlay) -----------------------

		private static Vector3? GetMoveDirection(EntityData data)
		{
			var md = data.GetVector3("movedir");
			if (!md.HasValue) return null;

			// Quake/Source "movedir" convention: the yaw component carries special
			// sentinel values for the "straight up" / "straight down" picks in
			// Hammer's direction control, instead of a literal angle - these two
			// cases can't be produced by ordinary pitch/yaw/roll math.
			if (Math.Abs(md.Value.Y - -1f) < 0.01f) return Vector3.UnitZ;   // up
			if (Math.Abs(md.Value.Y - -2f) < 0.01f) return -Vector3.UnitZ; // down

			var dir = AngleToDirection(md.Value);
			return dir.LengthSquared() < 0.0001f ? (Vector3?) null : dir;
		}

		/// <summary>
		/// The entity's real travel distance: its own size measured along the move
		/// direction, minus "lip" (the sliver of the entity classic Source movers like
		/// func_door leave behind instead of moving the full width). Falls back to a
		/// size-based default when there isn't enough information (e.g. point entities).
		/// </summary>
		private static float GetMoveLength(MapEntity entity, EntityData data, Vector3 direction, float entitySize)
		{
			var box = entity.BoundingBox;
			var extent = 0f;
			if (box != null)
			{
				var dim = box.Dimensions; // (Width, Length, Height) along world X, Y, Z
				extent = Math.Abs(direction.X) * dim.X + Math.Abs(direction.Y) * dim.Y + Math.Abs(direction.Z) * dim.Z;
			}

			var lip = data.Get("lip", 0f);
			var travel = extent - lip;
			return travel > 1f ? travel : entitySize * MoveArrowScale;
		}

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

		private static Property FindProperty(GameDataObject cls, string name)
		{
			return cls.Properties.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
		}

		private static int? FindFlagBit(Property flagsProp, string descriptionContains)
		{
			var opt = flagsProp.Options.FirstOrDefault(x => (x.Description ?? "").IndexOf(descriptionContains, StringComparison.OrdinalIgnoreCase) >= 0);
			if (opt == null) return null;
			return int.TryParse(opt.Key, out var v) ? v : (int?) null;
		}

		private static Matrix4x4 AngleMatrix(Vector3 pitchYawRoll)
		{
			var rad = pitchYawRoll * (float) Math.PI / 180f;
			return Matrix4x4.CreateFromYawPitchRoll(rad.X, rad.Z, rad.Y);
		}

		private static Vector3 AngleToDirection(Vector3 pitchYawRoll)
		{
			return Vector3.Transform(Vector3.UnitX, AngleMatrix(pitchYawRoll));
		}

		// --- Drawing ---------------------------------------------------------

		/// <summary>
		/// A wireframe 3D arrow: shaft plus a cone-shaped head, with a second, smaller
		/// cone at the midpoint so direction still reads if the tip is occluded.
		/// </summary>
		private static void DrawArrow3D(PerspectiveCamera camera, I2DRenderer im, Vector3 origin, Vector3 direction, float length, Color color)
		{
			direction = Vector3.Normalize(direction);
			var tip = origin + direction * length;
			var headLength = Math.Min(length * HeadLengthFraction, length * 0.5f);

			DrawLine3D(camera, im, origin, tip - direction * headLength, color, StrokeWidth);
			DrawCone(camera, im, tip, direction, headLength, color);

			if (length > headLength * 2.5f)
			{
				var midHeadLength = headLength * 0.7f;
				var midTip = origin + direction * (length * 0.5f + midHeadLength * 0.5f);
				DrawCone(camera, im, midTip, direction, midHeadLength, color);
			}
		}

		/// <summary>A cone with its apex at <paramref name="tip"/>, opening backwards along -direction.</summary>
		private static void DrawCone(PerspectiveCamera camera, I2DRenderer im, Vector3 tip, Vector3 direction, float headLength, Color color)
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

		/// <summary>
		/// A 3D arc around the rotation axis. With a finite sweep it runs from the start
		/// angle to the end angle exactly - the arc is trimmed just short of the end so
		/// the arrowhead's apex lands precisely on the end radial line rather than past
		/// it - marked by a thin radial line at the start and a bold one at the end (both
		/// on the same radius as the arc, so they meet it cleanly). A second, smaller
		/// arrow sits at the arc's midpoint. Entities without a finite sweep get an open
		/// arc with just the arrow head.
		/// </summary>
		private static void DrawRotation3D(PerspectiveCamera camera, I2DRenderer im, Vector3 pivot, Vector3 axis, float radius, Color color, bool reversed, float? sweepDegrees)
		{
			axis = Vector3.Normalize(axis);
			BuildBasis(axis, out var u, out var v);

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
				// start of the swing: thin; end of the swing: bold - both on the arc's own radius
				DrawLine3D(camera, im, pivot, PointAt(0, radius), Color.FromArgb(150, color), 1f);
				DrawLine3D(camera, im, pivot, tip, color, StrokeWidth * 1.5f);

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

		private static void BuildBasis(Vector3 axis, out Vector3 u, out Vector3 v)
		{
			var helper = Math.Abs(Vector3.Dot(axis, Vector3.UnitZ)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
			u = Vector3.Normalize(Vector3.Cross(axis, helper));
			v = Vector3.Cross(axis, u);
		}

		/// <summary>
		/// Clips the segment against the camera near plane (so points behind the camera never
		/// get projected), projects it, and draws it with a dark outline underneath.
		/// </summary>
		private static void DrawLine3D(PerspectiveCamera camera, I2DRenderer im, Vector3 a, Vector3 b, Color color, float width)
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
