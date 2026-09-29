using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.GameData;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;
using static Sledge.BspEditor.Tools.Widgets.MotionWidgetDrawing;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Widgets
{
	/// <summary>
	/// 3D-viewport straight movement arrow for selected entities that translate along a
	/// fixed direction (func_door, func_button, trigger_push, ...). Independent of the
	/// rotation arc (<see cref="EntityMotionWidget"/>) - it has its own toggle and its
	/// own render pass.
	///
	/// Direction comes from the "movedir" keyvalue when the entity has one. GoldSrc
	/// movers (func_door and friends) have no "movedir" keyvalue - the engine derives
	/// it from "angles" / "angle" - so for classes whose FGD exposes a "movedir"
	/// property, or labels "angles" / "angle" as a direction ("Move Direction ..."),
	/// the direction is read from there instead.
	///
	/// The arrow's length is the entity's own size measured along the direction of
	/// travel (falling back to half its smallest dimension when that can't be worked out).
	/// The head is an open 45-degree "V" (no cone).
	/// </summary>
	public class EntityMoveArrowWidget : BaseTool
	{
		public static readonly Capability EntityMoveArrowWidgetCapability = Capability.Create("EntityMoveArrowWidget");
		public override Capability ToolCapability => EntityMoveArrowWidgetCapability;

		/// <summary>
		/// GoldSrc linear movers that take their travel direction from "angles"/"angle" (the engine's
		/// SetMovedir), whether or not the loaded FGD labels that key as a direction. Many FGDs (e.g.
		/// the CS 1.6 ones) just label it "Pitch Yaw Roll", which the FGD heuristic below can't recognise.
		/// </summary>
		private static readonly HashSet<string> AngleDrivenMovers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"func_door", "func_water", "func_button", "momentary_door", "trigger_push", "func_conveyor"
		};

		private const float FallbackScale = 0.5f;        // fallback shaft length (x smallest entity dimension)
		private const float HeadLengthFraction = 0.3f;   // of the shaft length

		public EntityMoveArrowWidget()
		{
			Usage = ToolUsage.View3D;
			Active = false;
		}

		public override Image GetIcon() { return null; }
		public override string GetName() { return "EntityMoveArrowWidget"; }

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
				if (min <= 0) continue;

				var dir = GetMoveDirection(cls, data);
				if (!dir.HasValue) continue;

				var color = ed.Color?.Color ?? Color.White;

				// Entity size measured along the direction of travel.
				var dim = box.Dimensions; // (Width, Length, Height) along world X, Y, Z
				var extent = Math.Abs(dir.Value.X) * dim.X + Math.Abs(dir.Value.Y) * dim.Y + Math.Abs(dir.Value.Z) * dim.Z;
				var length = extent > 1f ? extent : min * FallbackScale;

				DrawArrow3D(camera, im, box.Center, dir.Value, length, color);
			}
		}

		private static Vector3? GetMoveDirection(GameDataObject cls, EntityData data)
		{
			var movedir = data.GetVector3("movedir");
			if (movedir.HasValue) return DirectionFromAngles(movedir.Value);

			if (!UsesAnglesForMovement(cls)) return null;

			var angles = data.GetVector3("angles");
			if (angles.HasValue) return DirectionFromAngles(angles.Value);

			// Legacy single-value "angle" key: -1 = up, -2 = down, otherwise a yaw.
			var angle = data.Get("angle", float.NaN);
			// No direction set at all: the engine treats that as angles 0 0 0, i.e. +X.
			if (float.IsNaN(angle)) return AngleDrivenMovers.Contains(cls.Name) ? Vector3.UnitX : (Vector3?) null;
			if (Math.Abs(angle - -1f) < 0.01f) return Vector3.UnitZ;
			if (Math.Abs(angle - -2f) < 0.01f) return -Vector3.UnitZ;
			return DirectionFromAngles(new Vector3(0, angle, 0));
		}

		/// <summary>
		/// GoldSrc movers have no "movedir" keyvalue; the direction lives in "angles"/"angle".
		/// Only trust those on classes whose FGD says they are a movement direction, so plain
		/// entities that merely have an "angles" key don't get an arrow.
		/// </summary>
		private static bool UsesAnglesForMovement(GameDataObject cls)
		{
			if (FindProperty(cls, "movedir") != null) return true;
			if (AngleDrivenMovers.Contains(cls.Name)) return true;

			foreach (var key in new[] { "angles", "angle" })
			{
				var prop = FindProperty(cls, key);
				if (prop == null) continue;
				var text = (prop.Description ?? "") + " " + (prop.ShortDescription ?? "");
				if (text.IndexOf("direction", StringComparison.OrdinalIgnoreCase) >= 0) return true;
			}
			return false;
		}

		private static Vector3? DirectionFromAngles(Vector3 pitchYawRoll)
		{
			// This build's Hammer direction control writes pitch = 90 for straight up and
			// pitch = -90 for straight down. Handled explicitly since the generic
			// pitch/yaw/roll math doesn't reliably reduce to a clean vertical vector.
			if (Math.Abs(pitchYawRoll.X - 90f) < 0.5f) return Vector3.UnitZ;     // up
			if (Math.Abs(pitchYawRoll.X - -90f) < 0.5f) return -Vector3.UnitZ;  // down

			// Also accept the classic Quake/Source yaw sentinel (-1 up, -2 down).
			if (Math.Abs(pitchYawRoll.Y - -1f) < 0.01f) return Vector3.UnitZ;
			if (Math.Abs(pitchYawRoll.Y - -2f) < 0.01f) return -Vector3.UnitZ;

			var dir = AngleToDirection(pitchYawRoll);
			return dir.LengthSquared() < 0.0001f ? (Vector3?) null : dir;
		}

		/// <summary>A wireframe 3D arrow: a shaft ending in an open 45-degree arrowhead.</summary>
		private static void DrawArrow3D(PerspectiveCamera camera, I2DRenderer im, Vector3 origin, Vector3 direction, float length, Color color)
		{
			direction = Vector3.Normalize(direction);
			var tip = origin + direction * length;
			var headLength = Math.Min(length * HeadLengthFraction, length * 0.5f);

			DrawLine3D(camera, im, origin, tip, color, StrokeWidth);
			DrawArrowHead(camera, im, tip, direction, headLength, color);
		}
	}
}
