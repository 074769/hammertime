using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Tools.DragSelect
{
    /// <summary>
    /// Blender-style circle selection, shared by the Select and Vertex tools.
    /// C toggles it, the mouse wheel resizes the circle (and never zooms or moves the camera),
    /// left button adds what is inside the circle, middle button removes it, right button cancels.
    /// Only one of left/middle can be held at a time; the second press is ignored.
    /// </summary>
    public class CircleSelectMode
    {
        public const float MinRadius = 6f;
        public const float MaxRadius = 600f;
        private const float WheelStep = 1.1f;
        private const double MinIntervalMs = 40;

        public enum HeldButton { None, Add, Remove }

        public bool Active { get; private set; }
        public HeldButton Held { get; private set; } = HeldButton.None;
        public float Radius { get; private set; } = 40f;
        public MapViewport Viewport { get; private set; }
        public Vector2 Center { get; private set; }

        private Vector2 _lastAppliedCenter = new Vector2(float.MinValue, float.MinValue);
        private float _lastAppliedRadius;
        private long _lastAppliedTicks;
        private double _lastCostMs;

        public void Toggle(MapViewport viewport)
        {
            if (Active) { Exit(); return; }
            Active = true;
            Viewport = viewport;
            var p = viewport.Viewport.Control.PointToClient(Cursor.Position);
            Center = new Vector2(p.X, p.Y);
            ResetApplied();
        }

        public void Exit()
        {
            Active = false;
            Held = HeldButton.None;
            Viewport = null;
            ResetApplied();
        }

        public void ResetApplied()
        {
            _lastAppliedCenter = new Vector2(float.MinValue, float.MinValue);
            _lastAppliedRadius = 0;
            _lastAppliedTicks = 0;
            _lastCostMs = 0;
        }

        /// <summary>Follow the mouse; the circle moves to whichever viewport the mouse is in.</summary>
        public void Move(MapViewport viewport, float x, float y)
        {
            if (!Active) return;
            if (!ReferenceEquals(viewport, Viewport)) { Viewport = viewport; ResetApplied(); }
            Center = new Vector2(x, y);
        }

        public void Resize(int wheelDelta)
        {
            if (wheelDelta == 0) return;
            var r = wheelDelta > 0 ? Radius * WheelStep : Radius / WheelStep;
            Radius = Math.Max(MinRadius, Math.Min(MaxRadius, r));
        }

        /// <summary>Claims the held state for a button. Returns false if another button already holds it.</summary>
        public bool TryBegin(bool add)
        {
            if (Held != HeldButton.None) return false;
            Held = add ? HeldButton.Add : HeldButton.Remove;
            return true;
        }

        /// <summary>Releases the held state if this button owns it.</summary>
        public bool End(bool add)
        {
            if (Held == HeldButton.None || (Held == HeldButton.Add) != add) return false;
            Held = HeldButton.None;
            return true;
        }

        /// <summary>Clears a held state whose button was released where no MouseUp reached us.</summary>
        public void ReleaseIfNotHeld()
        {
            if (Held == HeldButton.Add && !LeftHeld) Held = HeldButton.None;
            else if (Held == HeldButton.Remove && !MiddleHeld) Held = HeldButton.None;
        }

        /// <summary>
        /// Limits how often the hit test runs while dragging. The interval grows when applying is slow,
        /// so a heavy map backs off instead of stacking up work. The final position is never skipped.
        /// </summary>
        public bool NeedsApply(bool final = false)
        {
            var changed = Vector2.Distance(Center, _lastAppliedCenter) >= 3f || Math.Abs(Radius - _lastAppliedRadius) > 0.01f;
            if (!changed) return false;
            if (final) return true;
            return MsSince(_lastAppliedTicks) >= Math.Max(MinIntervalMs, 2 * _lastCostMs);
        }

        /// <summary>Call before applying. Returns the start time to pass to EndApply.</summary>
        public long BeginApply()
        {
            _lastAppliedCenter = Center;
            _lastAppliedRadius = Radius;
            return Stopwatch.GetTimestamp();
        }

        /// <summary>Call after applying with the value BeginApply returned.</summary>
        public void EndApply(long started)
        {
            var now = Stopwatch.GetTimestamp();
            _lastCostMs = (now - started) * 1000.0 / Stopwatch.Frequency;
            _lastAppliedTicks = now;
        }

        private static double MsSince(long ticks) => (Stopwatch.GetTimestamp() - ticks) * 1000.0 / Stopwatch.Frequency;

        public static bool LeftHeld => (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
        public static bool MiddleHeld => (Control.MouseButtons & MouseButtons.Middle) == MouseButtons.Middle;

        public void Draw(IViewport viewport, I2DRenderer im)
        {
            if (!Active || Viewport == null || !ReferenceEquals(viewport, Viewport.Viewport)) return;

            var removing = Held == HeldButton.Remove;
            var line = removing ? Color.FromArgb(230, 255, 90, 90) : Color.FromArgb(230, 255, 255, 255);
            var fill = removing ? Color.FromArgb(40, 255, 60, 60) : Color.FromArgb(32, 255, 255, 255);

            im.AddCircleFilled(Center, Radius, fill);
            im.AddCircle(Center, Radius, line);
            im.AddCircleFilled(Center, 2f, line);
        }

        #region Hit testing

        /// <summary>
        /// 2D views: objects whose outline (or centre) is touched by the circle.
        /// Only objects whose selection state would change (add: unselected, remove: selected) are tested,
        /// so the geometry of objects that are already in the right state is never built.
        /// </summary>
        public static List<IMapObject> FindInOrtho(MapDocument document, OrthographicCamera camera, Vector2 centre, float radius, bool add,
            Func<IMapObject, IEnumerable<Polygon>> geometry, Func<IMapObject, Vector3> objectCentre)
        {
            return document.Map.Root.Collect(
                x => x is Root || (x.BoundingBox != null && BoxTouchesCircle(camera, x.BoundingBox, centre, radius)),
                x => x.Hierarchy.Parent != null && !x.Hierarchy.HasChildren && x.IsSelected != add
                     && ObjectTouchesCircle(camera, x, centre, radius, geometry, objectCentre)
            );
        }

        /// <summary>
        /// 3D view: objects visible through the circle. A grid of rays is cast through the circle and the nearest
        /// hit of each is taken, so objects hidden behind others are not picked.
        /// </summary>
        public static HashSet<IMapObject> FindInPerspective(MapDocument document, PerspectiveCamera camera, Vector2 centre, float radius,
            MapObjectExtensions.IgnoreOptions ignore)
        {
            var found = new HashSet<IMapObject>();
            var step = Math.Max(6f, radius / 10f);
            var r2 = radius * radius;

            for (var dy = -radius; dy <= radius; dy += step)
            {
                for (var dx = -radius; dx <= radius; dx += step)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    var (start, end) = camera.CastRayFromScreen(new Vector3(centre.X + dx, centre.Y + dy, 0));
                    var hit = document.Map.Root.GetIntersectionsForVisibleObjects(new Line(start, end), ignore).FirstOrDefault();
                    if (hit != null) found.Add(hit.Object);
                }
            }
            return found;
        }

        /// <summary>Screen-space test used for vertex handles: in front of the camera and inside the circle.</summary>
        public static bool ScreenPointInside(Vector3 screen, Vector2 centre, float radius)
        {
            var dx = screen.X - centre.X;
            var dy = screen.Y - centre.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool BoxTouchesCircle(OrthographicCamera camera, Box box, Vector2 centre, float radius)
        {
            var s = box.Start;
            var e = box.End;
            var minX = float.MaxValue; var minY = float.MaxValue;
            var maxX = float.MinValue; var maxY = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? s.X : e.X, (i & 2) == 0 ? s.Y : e.Y, (i & 4) == 0 ? s.Z : e.Z);
                var p = camera.WorldToScreen(corner);
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            var cx = Math.Max(minX, Math.Min(centre.X, maxX));
            var cy = Math.Max(minY, Math.Min(centre.Y, maxY));
            var dx = centre.X - cx;
            var dy = centre.Y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        private static bool ObjectTouchesCircle(OrthographicCamera camera, IMapObject obj, Vector2 centre, float radius,
            Func<IMapObject, IEnumerable<Polygon>> geometry, Func<IMapObject, Vector3> objectCentre)
        {
            // The centre handle counts, like it does for box selection
            var c = camera.WorldToScreen(objectCentre(obj));
            if (ScreenPointInside(c, centre, radius)) return true;

            var polygons = geometry(obj);
            if (polygons == null) return false;

            foreach (var polygon in polygons)
            {
                var verts = polygon.Vertices;
                for (var i = 0; i < verts.Count; i++)
                {
                    var a = camera.WorldToScreen(verts[i]);
                    var b = camera.WorldToScreen(verts[(i + 1) % verts.Count]);
                    if (DistanceToSegment(centre, a, b) <= radius) return true;
                }
            }
            return false;
        }

        private static float DistanceToSegment(Vector2 p, Vector3 a, Vector3 b)
        {
            var abX = b.X - a.X;
            var abY = b.Y - a.Y;
            var lenSq = abX * abX + abY * abY;
            var t = 0f;
            if (lenSq > 0)
            {
                t = ((p.X - a.X) * abX + (p.Y - a.Y) * abY) / lenSq;
                t = Math.Max(0f, Math.Min(1f, t));
            }
            var dx = p.X - (a.X + t * abX);
            var dy = p.Y - (a.Y + t * abY);
            return (float) Math.Sqrt(dx * dx + dy * dy);
        }

        #endregion
    }
}
