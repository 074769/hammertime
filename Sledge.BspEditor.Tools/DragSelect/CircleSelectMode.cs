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
    /// C toggles it, the mouse wheel resizes the circle (and no longer zooms/moves the camera),
    /// left button adds what is inside the circle, middle button removes it.
    /// The state lives per tool; the circle is only shown in the viewport it was started in.
    /// </summary>
    public class CircleSelectMode
    {
        public const float MinRadius = 6f;
        public const float MaxRadius = 600f;
        private const float WheelStep = 1.1f;

        public bool Active { get; private set; }
        public float Radius { get; private set; } = 40f;
        public MapViewport Viewport { get; private set; }
        public Vector2 Center { get; private set; }

        // Used by the tools to avoid re-running the (expensive) hit test when nothing changed
        public Vector2 LastAppliedCenter { get; set; } = new Vector2(float.MinValue, float.MinValue);
        public float LastAppliedRadius { get; set; }

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
            Viewport = null;
            ResetApplied();
        }

        public void ResetApplied()
        {
            LastAppliedCenter = new Vector2(float.MinValue, float.MinValue);
            LastAppliedRadius = 0;
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

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastAppliedMs = long.MinValue / 2;
        private const long MinIntervalMs = 40;

        /// <summary>
        /// The hit test runs at most ~25 times a second while dragging, and only if the circle moved or resized.
        /// The final position (button release) always runs, so nothing is lost by the throttle.
        /// </summary>
        public bool NeedsApply(bool final = false)
        {
            var changed = Vector2.Distance(Center, LastAppliedCenter) >= 3f || Math.Abs(Radius - LastAppliedRadius) > 0.01f;
            if (!changed) return false;
            return final || _clock.ElapsedMilliseconds - _lastAppliedMs >= MinIntervalMs;
        }

        public void MarkApplied()
        {
            LastAppliedCenter = Center;
            LastAppliedRadius = Radius;
            _lastAppliedMs = _clock.ElapsedMilliseconds;
        }

        public static bool LeftHeld => (Control.MouseButtons & MouseButtons.Left) == MouseButtons.Left;
        public static bool MiddleHeld => (Control.MouseButtons & MouseButtons.Middle) == MouseButtons.Middle;

        public void Draw(IViewport viewport, I2DRenderer im)
        {
            if (!Active || Viewport == null || !ReferenceEquals(viewport, Viewport.Viewport)) return;

            var removing = MiddleHeld;
            var line = removing ? Color.FromArgb(230, 255, 90, 90) : Color.FromArgb(230, 255, 255, 255);
            var fill = removing ? Color.FromArgb(40, 255, 60, 60) : Color.FromArgb(32, 255, 255, 255);

            im.AddCircleFilled(Center, Radius, fill);
            im.AddCircle(Center, Radius, line);
            im.AddCircleFilled(Center, 2f, line);
        }

        #region Hit testing

        /// <summary>
        /// 2D views: objects whose outline (or centre) is touched by the circle.
        /// geometry/centre let the caller substitute pick geometry for model and sprite entities.
        /// </summary>
        public static List<IMapObject> FindInOrtho(MapDocument document, OrthographicCamera camera, Vector2 centre, float radius,
            Func<IMapObject, IEnumerable<Polygon>> geometry, Func<IMapObject, Vector3> objectCentre)
        {
            return document.Map.Root.Collect(
                x => x is Root || (x.BoundingBox != null && BoxTouchesCircle(camera, x.BoundingBox, centre, radius)),
                x => x.Hierarchy.Parent != null && !x.Hierarchy.HasChildren && ObjectTouchesCircle(camera, x, centre, radius, geometry, objectCentre)
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
            var step = Math.Max(6f, radius / 5f);
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
