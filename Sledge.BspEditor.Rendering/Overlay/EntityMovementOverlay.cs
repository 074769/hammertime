using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.GameData;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// Draws helper arrows in the 2D viewports showing how an entity will move
    /// and/or rotate once the map is running.
    ///
    /// Two independent helpers can be shown for any entity, entirely driven by
    /// its FGD definition and its own keyvalues - nothing is hardcoded to
    /// specific classnames:
    ///
    ///  - A straight arrow along the entity's "movedir" keyvalue, for entities
    ///    that translate along a fixed direction (func_door, func_button,
    ///    trigger_push, momentary_door, etc). This is driven purely by the
    ///    entity property - if "movedir" isn't set, no arrow is drawn.
    ///
    ///  - A rotating arrow around the entity's rotation axis, for entities
    ///    whose FGD class exposes the classic "X Axis" / "Y Axis" spawnflag
    ///    options (func_rotating, func_door_rotating, func_platrot,
    ///    momentary_rot_button, ...). The axis defaults to local Z unless one
    ///    of those flags is ticked on the entity, and the local axis is then
    ///    oriented using the entity's own "angles" property - so this helper
    ///    is driven by a mix of flags and entity property.
    ///
    /// An entity that both moves and rotates (func_platrot, for example) shows
    /// both helpers at once, centered on the same point. The rotation helper
    /// is intentionally drawn larger than the movement arrow so it reads
    /// clearly and isn't lost underneath it.
    /// </summary>
    [Export(typeof(IMapObject2DOverlay))]
    public class EntityMovementOverlay : IMapObject2DOverlay
    {
        private const float MoveArrowScale = 0.4f;
        private const float RotateArrowScale = 0.7f;
        private const int RotateArcSegments = 28;
        private const double RotateArcDegrees = 300; // leave a gap so the arrowhead/direction reads clearly
        private const float ArrowHeadLength = 8f; // screen pixels
        private const float ArrowHeadAngle = 25f; // degrees, half-angle of the arrowhead

        public void Render(IViewport viewport, ICollection<IMapObject> objects, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im, MapDocument document)
        {
            if (camera.Zoom < 0.5f) return;
            if (document == null) return;

            GameData gameData;
            try
            {
                // The environment caches this as a synchronously-completed task once
                // loaded, so this does not block on real async work.
                gameData = document.Environment.GetGameData().Result;
            }
            catch
            {
                return;
            }
            if (gameData == null) return;

            foreach (var ed in objects.OfType<Entity>().Where(x => x.EntityData != null).Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)))
            {
                var data = ed.EntityData;
                var cls = gameData.GetClass(data.Name);
                if (cls == null) continue;

                var color = ed.Color?.Color ?? Color.White;
                var origin = ed.BoundingBox.Center;
                var min = Math.Min(ed.BoundingBox.Width, Math.Min(ed.BoundingBox.Height, ed.BoundingBox.Length));
                if (min <= 0) continue;

                var moveDir = GetMoveDirection(data);
                if (moveDir.HasValue)
                {
                    DrawMoveArrow(camera, im, origin, moveDir.Value, min * MoveArrowScale, color);
                }

                var rotateAxis = GetRotationAxis(cls, data);
                if (rotateAxis.HasValue)
                {
                    DrawRotateArrow(camera, im, origin, rotateAxis.Value, min * RotateArrowScale, color, IsReversed(cls, data));
                }
            }
        }

        // --- Movement (translate) helper ------------------------------------

        private static Vector3? GetMoveDirection(EntityData data)
        {
            var md = data.GetVector3("movedir");
            if (!md.HasValue) return null;
            var dir = AngleToDirection(md.Value);
            return dir.LengthSquared() < 0.0001f ? (Vector3?) null : dir;
        }

        // --- Rotation helper --------------------------------------------------

        private static Vector3? GetRotationAxis(GameDataObject cls, EntityData data)
        {
            var flagsProp = cls.Properties.FirstOrDefault(x => string.Equals(x.Name, "spawnflags", StringComparison.OrdinalIgnoreCase));
            if (flagsProp == null) return null;

            var xAxisBit = FindFlagBit(flagsProp, "x axis");
            var yAxisBit = FindFlagBit(flagsProp, "y axis");

            // Only treat this class as a rotator if its FGD data actually exposes
            // the classic axis-selection flag pair - this is what distinguishes a
            // rotating entity from an ordinary one without hardcoding classnames.
            if (xAxisBit == null && yAxisBit == null) return null;

            var local = Vector3.UnitZ;
            if (xAxisBit.HasValue && (data.Flags & xAxisBit.Value) != 0) local = Vector3.UnitX;
            else if (yAxisBit.HasValue && (data.Flags & yAxisBit.Value) != 0) local = Vector3.UnitY;

            // Orient the local axis by the entity's own "angles", if any, so the
            // helper also makes sense for angle-oriented rotators (e.g. a
            // momentary_rot_button that isn't axis-aligned).
            var ang = data.GetVector3("angles");
            if (!ang.HasValue) return local;

            return Vector3.Transform(local, AngleMatrix(ang.Value));
        }

        private static bool IsReversed(GameDataObject cls, EntityData data)
        {
            var flagsProp = cls.Properties.FirstOrDefault(x => string.Equals(x.Name, "spawnflags", StringComparison.OrdinalIgnoreCase));
            var reverseBit = flagsProp == null ? null : FindFlagBit(flagsProp, "reverse");
            return reverseBit.HasValue && (data.Flags & reverseBit.Value) != 0;
        }

        private static int? FindFlagBit(Property flagsProp, string descriptionContains)
        {
            var opt = flagsProp.Options.FirstOrDefault(x => (x.Description ?? "").IndexOf(descriptionContains, StringComparison.OrdinalIgnoreCase) >= 0);
            if (opt == null) return null;
            return int.TryParse(opt.Key, out var v) ? v : (int?) null;
        }

        // --- Shared angle helpers ----------------------------------------------

        private static Matrix4x4 AngleMatrix(Vector3 pitchYawRoll)
        {
            var rad = pitchYawRoll * (float) Math.PI / 180f;
            return Matrix4x4.CreateFromYawPitchRoll(rad.X, rad.Z, rad.Y);
        }

        private static Vector3 AngleToDirection(Vector3 pitchYawRoll)
        {
            return Vector3.Transform(Vector3.UnitX, AngleMatrix(pitchYawRoll));
        }

        // --- Drawing -------------------------------------------------------

        private static void DrawMoveArrow(OrthographicCamera camera, I2DRenderer im, Vector3 origin, Vector3 direction, float length, Color color)
        {
            direction = Vector3.Normalize(direction);

            var start = camera.WorldToScreen(origin).ToVector2();
            var end = camera.WorldToScreen(origin + direction * length).ToVector2();

            im.AddLine(start, end, color, 2);
            DrawArrowHead(im, start, end, color, 2);
        }

        private static void DrawRotateArrow(OrthographicCamera camera, I2DRenderer im, Vector3 origin, Vector3 axis, float radius, Color color, bool reversed)
        {
            axis = Vector3.Normalize(axis);

            // Build two vectors spanning the plane perpendicular to the axis. When
            // projected into a 2D viewport this circle will appear as a full
            // circle when looking straight down the axis, and progressively
            // flatten into a line when the axis lies in the view plane - which is
            // exactly the correct visual in either case, and needs no special
            // handling per Top/Front/Side view.
            var helper = Math.Abs(Vector3.Dot(axis, Vector3.UnitZ)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            var u = Vector3.Normalize(Vector3.Cross(axis, helper));
            var v = Vector3.Cross(axis, u);

            var sweep = RotateArcDegrees * Math.PI / 180.0;
            if (reversed) sweep = -sweep;

            Vector2? prev = null;
            var lastFrom = Vector2.Zero;
            var lastTo = Vector2.Zero;

            for (var i = 0; i <= RotateArcSegments; i++)
            {
                var t = sweep * i / RotateArcSegments;
                var offset = (u * (float) Math.Cos(t) + v * (float) Math.Sin(t)) * radius;
                var screen = camera.WorldToScreen(origin + offset).ToVector2();

                if (prev.HasValue)
                {
                    im.AddLine(prev.Value, screen, color, 2);
                    lastFrom = prev.Value;
                    lastTo = screen;
                }
                prev = screen;
            }

            DrawArrowHead(im, lastFrom, lastTo, color, 2);
        }

        private static void DrawArrowHead(I2DRenderer im, Vector2 from, Vector2 to, Color color, float width)
        {
            var dir = to - from;
            if (dir.LengthSquared() < 1f) return;
            dir = Vector2.Normalize(dir);

            var rad = ArrowHeadAngle * (float) Math.PI / 180f;

            Vector2 Rotate(Vector2 d, float a)
            {
                var cos = (float) Math.Cos(a);
                var sin = (float) Math.Sin(a);
                return new Vector2(d.X * cos - d.Y * sin, d.X * sin + d.Y * cos);
            }

            var left = to - Rotate(dir, rad) * ArrowHeadLength;
            var right = to - Rotate(dir, -rad) * ArrowHeadLength;

            im.AddLine(to, left, color, width);
            im.AddLine(to, right, color, width);
        }
    }
}
