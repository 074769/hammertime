using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Logging;
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
    /// Draws a rotation helper for entities that rotate, driven by its FGD
    /// definition and the entity's own keyvalues:
    ///
    ///  - (The straight movement arrow is not drawn in 2D; it is a 3D-only widget,
    ///    EntityMoveArrowWidget in Sledge.BspEditor.Tools.)
    ///
    ///  - A rotating arrow around the entity's rotation axis, for entities
    ///    whose FGD class exposes the classic "X Axis" / "Y Axis" spawnflag
    ///    options (func_rotating, func_door_rotating, func_platrot,
    ///    momentary_rot_button, ...). The axis defaults to local Z unless one
    ///    of those flags is ticked on the entity, and the local axis is then
    ///    oriented using the entity's own "angles" property - so this helper
    ///    is driven by a mix of flags and entity property.
    ///
    /// </summary>
    [Export(typeof(IMapObject2DOverlay))]
    public class EntityMovementOverlay : IMapObject2DOverlay
    {
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

        // Caches. Everything here depends only on the FGD and the entity's own data, so it is worked out once
        // and reused on every redraw. Each cache is keyed on the object it was built from, so a reloaded
        // environment or an edited entity is picked up automatically.

        private sealed class ClassRotation
        {
            public bool CanRotate;
            public int? XAxisBit;
            public int? YAxisBit;
            public int? ReverseBit;
        }

        private sealed class RotatorState
        {
            public int Flags;
            public string Angles;
            public Vector3 Origin;
            public float Radius;
            public GameDataObject Class;
            public Vector3[] Arc; // world-space points, projected to screen each redraw
        }

        private static readonly ConditionalWeakTable<GameData, Dictionary<string, GameDataObject>> ClassIndexes
            = new ConditionalWeakTable<GameData, Dictionary<string, GameDataObject>>();
        private static readonly ConditionalWeakTable<GameDataObject, ClassRotation> ClassRotations
            = new ConditionalWeakTable<GameDataObject, ClassRotation>();
        private static readonly ConditionalWeakTable<Entity, RotatorState> RotatorStates
            = new ConditionalWeakTable<Entity, RotatorState>();

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

            var classes = GetClassIndex(gameData);

            foreach (var ed in objects.OfType<Entity>())
            {
                var data = ed.EntityData;
                if (data == null || data.Name == null) continue;
                if (EntityKinds.IsSprite(data)) continue; // sprites never move or rotate
                if (!classes.TryGetValue(data.Name, out var cls)) continue;

                // Most classes never rotate, so this rejects them with one cached lookup
                if (!GetClassRotation(cls).CanRotate) continue;
                if (ed.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)) continue;

                var color = ed.Color?.Color ?? Color.White;
                var box = ed.BoundingBox;
                var origin = box.Center;
                var min = Math.Min(box.Width, Math.Min(box.Height, box.Length));
                if (min <= 0) continue;

                var arc = GetRotatorArc(ed, data, cls, origin, min * RotateArrowScale);
                DrawRotateArrow(camera, im, arc, color);
            }
        }

        // --- Cached lookups -----------------------------------------------------

        /// <summary>
        /// Same rule as GameData.GetClass (first non-base class whose name matches, ignoring case), but
        /// indexed once per GameData instead of scanning every class for every entity on every redraw.
        /// </summary>
        private static Dictionary<string, GameDataObject> GetClassIndex(GameData gameData)
        {
            return ClassIndexes.GetValue(gameData, g =>
            {
                var index = new Dictionary<string, GameDataObject>(StringComparer.InvariantCultureIgnoreCase);
                foreach (var c in g.Classes)
                {
                    if (c.ClassType == ClassType.Base || c.Name == null) continue;
                    if (!index.ContainsKey(c.Name)) index.Add(c.Name, c);
                }
                return index;
            });
        }

        private static ClassRotation GetClassRotation(GameDataObject cls)
        {
            return ClassRotations.GetValue(cls, c =>
            {
                var rot = new ClassRotation();
                var flagsProp = c.Properties.FirstOrDefault(x => string.Equals(x.Name, "spawnflags", StringComparison.OrdinalIgnoreCase));
                if (flagsProp == null) return rot;

                rot.XAxisBit = FindFlagBit(flagsProp, "x axis");
                rot.YAxisBit = FindFlagBit(flagsProp, "y axis");
                rot.ReverseBit = FindFlagBit(flagsProp, "reverse");
                // Only classes whose FGD exposes the classic axis-selection flags are rotators
                rot.CanRotate = rot.XAxisBit.HasValue || rot.YAxisBit.HasValue;
                return rot;
            });
        }

        /// <summary>
        /// Returns the world-space arc for a rotating entity. Rebuilt only when the inputs that shape it change.
        /// </summary>
        private static Vector3[] GetRotatorArc(Entity ed, EntityData data, GameDataObject cls, Vector3 origin, float radius)
        {
            var flags = data.Flags;
            var angles = data.Get<string>("angles");

            if (RotatorStates.TryGetValue(ed, out var cached)
                && cached.Flags == flags
                && cached.Angles == angles
                && cached.Origin == origin
                && cached.Radius == radius
                && ReferenceEquals(cached.Class, cls))
            {
                return cached.Arc;
            }

            using (PerfLog.Measure("MovementOverlay: arc rebuild"))
            {
                var rot = GetClassRotation(cls);
                var axis = GetRotationAxis(rot, data);
                var reversed = rot.ReverseBit.HasValue && (flags & rot.ReverseBit.Value) != 0;
                var arc = BuildArc(origin, axis, radius, reversed);

                RotatorStates.AddOrUpdate(ed, new RotatorState
                {
                    Flags = flags,
                    Angles = angles,
                    Origin = origin,
                    Radius = radius,
                    Class = cls,
                    Arc = arc
                });
                return arc;
            }
        }

        // --- Rotation helper --------------------------------------------------

        private static Vector3 GetRotationAxis(ClassRotation rot, EntityData data)
        {
            var local = Vector3.UnitZ;
            if (rot.XAxisBit.HasValue && (data.Flags & rot.XAxisBit.Value) != 0) local = Vector3.UnitX;
            else if (rot.YAxisBit.HasValue && (data.Flags & rot.YAxisBit.Value) != 0) local = Vector3.UnitY;

            // Orient the local axis by the entity's own "angles", if any, so the
            // helper also makes sense for angle-oriented rotators (e.g. a
            // momentary_rot_button that isn't axis-aligned).
            var ang = data.GetVector3("angles");
            if (!ang.HasValue) return local;

            return Vector3.Transform(local, AngleMatrix(ang.Value));
        }

        private static int? FindFlagBit(Property flagsProp, string descriptionContains)
        {
            var opt = flagsProp.Options.FirstOrDefault(x => (x.Description ?? "").IndexOf(descriptionContains, StringComparison.OrdinalIgnoreCase) >= 0);
            if (opt == null) return null;
            return int.TryParse(opt.Key, out var v) ? v : (int?) null;
        }

        // --- Shared angle helpers ----------------------------------------------

        // GoldSrc/Quake AngleMatrix (row-vector layout: forward, left, up). Same basis as
        // Billboard.geom.hlsl's Oriented sprites and MotionWidgetDrawing.AngleMatrix; kept as a
        // private copy because this assembly can't reference Sledge.BspEditor.Tools.
        private static Matrix4x4 AngleMatrix(Vector3 pitchYawRoll)
        {
            var rad = pitchYawRoll * (float) Math.PI / 180f;
            float sp = (float) Math.Sin(rad.X), cp = (float) Math.Cos(rad.X);
            float sy = (float) Math.Sin(rad.Y), cy = (float) Math.Cos(rad.Y);
            float sr = (float) Math.Sin(rad.Z), cr = (float) Math.Cos(rad.Z);

            return new Matrix4x4(
                cp * cy, cp * sy, -sp, 0,
                sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp, 0,
                cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp, 0,
                0, 0, 0, 1);
        }

        private static Vector3[] BuildArc(Vector3 origin, Vector3 axis, float radius, bool reversed)
        {
            axis = Vector3.Normalize(axis);

            var helper = Math.Abs(Vector3.Dot(axis, Vector3.UnitZ)) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            var u = Vector3.Normalize(Vector3.Cross(axis, helper));
            var v = Vector3.Cross(axis, u);

            var sweep = RotateArcDegrees * Math.PI / 180.0;
            if (reversed) sweep = -sweep;

            var arc = new Vector3[RotateArcSegments + 1];
            for (var i = 0; i <= RotateArcSegments; i++)
            {
                var t = sweep * i / RotateArcSegments;
                var offset = (u * (float) Math.Cos(t) + v * (float) Math.Sin(t)) * radius;
                arc[i] = origin + offset;
            }
            return arc;
        }

        // --- Drawing -------------------------------------------------------

        /// <summary>
        /// Rotator: a looping arc (a true 3D circle around the rotation axis,
        /// projected point-by-point per viewport) that ends in a hooked
        /// triangular head, matching the reference "redo"-style icon. Building
        /// the loop in 3D - rather than just in screen space - is what makes it
        /// read correctly in all three 2D views: it appears as a full circle
        /// when looking straight down the axis, and flattens toward a line when
        /// the axis lies in the view plane, which is the physically correct
        /// picture either way. The arc itself is cached in world space; only the
        /// projection to screen happens each redraw.
        /// </summary>
        private static void DrawRotateArrow(OrthographicCamera camera, I2DRenderer im, Vector3[] arc, Color color)
        {
            Vector2? prev = null;
            var lastFrom = Vector2.Zero;
            var lastTo = Vector2.Zero;

            foreach (var world in arc)
            {
                var screen = camera.WorldToScreen(world).ToVector2();

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
