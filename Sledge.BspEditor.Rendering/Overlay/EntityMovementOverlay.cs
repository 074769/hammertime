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
        private const float MoveArrowScale = 0.45f;
        private const float RotateArrowScale = 0.75f;
        private const int RotateArcSegments = 28;
        private const double RotateArcDegrees = 300; // leave a gap so the hooked head reads clearly

        // Block-arrow head, in screen pixels - stays a fixed icon size regardless
        // of zoom/entity size, only the shaft length grows with the arrow.
        private const float HeadLength = 16f;
        private const float HeadHalfWidth = 9f;
        private const float ShaftHalfWidth = 4f;

        private const float StrokeWidth = 2f; // screen pixels
        private const float OutlineExtraWidth = 2f; // added on top of StrokeWidth for the outline pass

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

        /// <summary>
        /// Straight mover: a hollow block-arrow outline (thin shaft rectangle
        /// that steps out to a wider flared head, then comes to a point) -
        /// projecting only the shaft's start/end from 3D and building the icon
        /// shape directly in screen space is correct in every 2D viewport,
        /// since the icon is meant to read as a flat overlay glyph regardless
        /// of which axis is being viewed.
        /// </summary>
        private static void DrawMoveArrow(OrthographicCamera camera, I2DRenderer im, Vector3 origin, Vector3 direction, float length, Color color)
        {
            direction = Vector3.Normalize(direction);

            var start = camera.WorldToScreen(origin).ToVector2();
            var end = camera.WorldToScreen(origin + direction * length).ToVector2();

            var dir = end - start;
            if (dir.LengthSquared() < 1f) return;
            dir = Vector2.Normalize(dir);
            var perp = new Vector2(-dir.Y, dir.X);

            var poly = BuildBlockArrowPolygon(start, dir, perp, Vector2.Distance(start, end));
            DrawPolygonOutline(im, poly, color);
        }

        /// <summary>
        /// Rotator: a looping arc (a true 3D circle around the rotation axis,
        /// projected point-by-point per viewport) that ends in a hooked
        /// triangular head, matching the reference "redo"-style icon. Building
        /// the loop in 3D - rather than just in screen space - is what makes it
        /// read correctly in all three 2D views: it appears as a full circle
        /// when looking straight down the axis, and flattens toward a line when
        /// the axis lies in the view plane, which is the physically correct
        /// picture either way.
        /// </summary>
        private static void DrawRotateArrow(OrthographicCamera camera, I2DRenderer im, Vector3 origin, Vector3 axis, float radius, Color color, bool reversed)
        {
            axis = Vector3.Normalize(axis);

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
                    DrawOutlinedLine(im, prev.Value, screen, color, ShaftHalfWidth * 2);
                    lastFrom = prev.Value;
                    lastTo = screen;
                }
                prev = screen;
            }

            var hookDir = lastTo - lastFrom;
            if (hookDir.LengthSquared() < 1f) return;
            hookDir = Vector2.Normalize(hookDir);
            var hookPerp = new Vector2(-hookDir.Y, hookDir.X);

            var apex = lastTo;
            var baseCenter = apex - hookDir * HeadLength;
            var baseLeft = baseCenter + hookPerp * HeadHalfWidth;
            var baseRight = baseCenter - hookPerp * HeadHalfWidth;

            DrawPolygonOutline(im, new[] { apex, baseLeft, baseRight }, color);
        }

        /// <summary>
        /// The 7-point hollow block-arrow silhouette (shaft rectangle + a
        /// stepped-out triangular head), built directly in screen space around
        /// a unit direction/perpendicular pair.
        /// </summary>
        private static Vector2[] BuildBlockArrowPolygon(Vector2 tail, Vector2 dir, Vector2 perp, float totalLength)
        {
            var headLength = Math.Min(HeadLength, totalLength);
            var shaftLength = totalLength - headLength;

            Vector2 P(float along, float across) => tail + dir * along + perp * across;

            return new[]
            {
                P(0, ShaftHalfWidth),
                P(shaftLength, ShaftHalfWidth),
                P(shaftLength, HeadHalfWidth),
                P(totalLength, 0),
                P(shaftLength, -HeadHalfWidth),
                P(shaftLength, -ShaftHalfWidth),
                P(0, -ShaftHalfWidth),
            };
        }

        private static void DrawPolygonOutline(I2DRenderer im, IReadOnlyList<Vector2> points, Color color)
        {
            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                DrawOutlinedLine(im, a, b, color, StrokeWidth);
            }
        }

        /// <summary>
        /// Draws a line with a slightly wider black line underneath it, so the
        /// helper stays readable against any background color or texture.
        /// </summary>
        private static void DrawOutlinedLine(I2DRenderer im, Vector2 start, Vector2 end, Color color, float width)
        {
            im.AddLine(start, end, Color.Black, width + OutlineExtraWidth);
            im.AddLine(start, end, color, width);
        }
    }
}
