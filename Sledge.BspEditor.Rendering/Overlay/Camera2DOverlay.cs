using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// Draws the 3D viewport's camera into every 2D viewport: a small square at the camera
    /// position, plus lines showing the edges of its field of view. The camera is read every
    /// time the overlay is built, so it follows the 3D camera live while it moves.
    ///
    /// In the Top view the lines are the horizontal FOV, in the Front/Side views they are the
    /// vertical FOV. If the camera is pitched, the outermost of the four projected frustum
    /// corner rays are used, so the widget always shows the real footprint in that view.
    /// </summary>
    [Export(typeof(IOverlayRenderable))]
    public class Camera2DOverlay : IOverlayRenderable
    {
        private const float BodyHalfSize = 7f;      // screen pixels
        private const float RayLength = 140f;       // screen pixels
        private const float StrokeWidth = 2f;
        private const float OutlineExtraWidth = 2f;
        private const float MinFlatLength = 0.02f;  // ignore rays that point (almost) straight at the view

        private static readonly Color CameraColor = Color.FromArgb(255, 255, 210, 0);

        private readonly object _lock = new object();
        private readonly List<MapViewport> _viewports = new List<MapViewport>();

        public Camera2DOverlay()
        {
            Oy.Subscribe<MapViewport>("MapViewport:Created", ViewportCreated);
            Oy.Subscribe<MapViewport>("MapViewport:Destroyed", ViewportDestroyed);
        }

        private Task ViewportCreated(MapViewport viewport)
        {
            lock (_lock)
            {
                if (!_viewports.Contains(viewport)) _viewports.Add(viewport);
            }
            return Task.CompletedTask;
        }

        private Task ViewportDestroyed(MapViewport viewport)
        {
            lock (_lock)
            {
                _viewports.Remove(viewport);
            }
            return Task.CompletedTask;
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            // 2D only
        }

        public void Render(IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            List<PerspectiveCamera> cameras;
            lock (_lock)
            {
                // Only cameras of 3D viewports that are actually showing (i.e. the active document's tab).
                cameras = _viewports
                    .Where(x => x.Viewport != null && x.Control != null && !x.Control.IsDisposed && x.Control.Visible)
                    .Select(x => x.Viewport.Camera as PerspectiveCamera)
                    .Where(x => x != null)
                    .ToList();
            }

            foreach (var cam in cameras)
            {
                DrawCamera(camera, im, cam);
            }
        }

        private static void DrawCamera(OrthographicCamera view, I2DRenderer im, PerspectiveCamera cam)
        {
            var dir = cam.Direction;
            if (dir.LengthSquared() < 0.0001f) return;
            dir = Vector3.Normalize(dir);
            var right = cam.GetRight();
            var up = Vector3.Normalize(Vector3.Cross(right, dir));

            var aspect = cam.Height > 0 ? cam.Width / (float) cam.Height : 1f;
            if (aspect <= 0) aspect = 1f;
            var ty = (float) Math.Tan(cam.FOV * Math.PI / 360.0); // FOV is the vertical FOV, in degrees
            var tx = ty * aspect;

            var pos = view.WorldToScreen(cam.Position);
            var origin = new Vector2(pos.X, pos.Y);

            // Screen-space (y down) direction of a world-space vector as seen by this 2D view
            bool ToScreenDir(Vector3 world, out Vector2 result)
            {
                var flat = view.Flatten(world);
                result = new Vector2(flat.X, -flat.Y);
                var len = result.Length();
                if (len < MinFlatLength) return false;
                result /= len;
                return true;
            }

            // The four frustum corner rays
            var corners = new[]
            {
                dir + right * tx + up * ty,
                dir - right * tx + up * ty,
                dir + right * tx - up * ty,
                dir - right * tx - up * ty
            };

            var hasForward = ToScreenDir(dir, out var fwd);
            var rays = new List<Vector2>();
            foreach (var c in corners)
            {
                if (ToScreenDir(c, out var r)) rays.Add(r);
            }

            var lineDirs = new List<Vector2>();
            if (hasForward && rays.Count > 0)
            {
                // Pick the two rays furthest to either side of the forward direction
                float Angle(Vector2 v) => (float) Math.Atan2(fwd.X * v.Y - fwd.Y * v.X, Vector2.Dot(fwd, v));
                var ordered = rays.OrderBy(Angle).ToList();
                lineDirs.Add(ordered.First());
                if (ordered.Count > 1) lineDirs.Add(ordered.Last());
            }
            else
            {
                // Looking (almost) straight along this view's depth axis - just show the footprint
                lineDirs.AddRange(rays);
            }

            // FOV lines
            var ends = new List<Vector2>();
            foreach (var d in lineDirs)
            {
                var end = origin + d * RayLength;
                ends.Add(end);
                DrawLine(im, origin, end, CameraColor, StrokeWidth);
            }

            // Close the far edge if it is a sensible triangle
            if (hasForward && lineDirs.Count == 2 && Vector2.Dot(lineDirs[0], lineDirs[1]) > -0.95f)
            {
                DrawLine(im, ends[0], ends[1], Color.FromArgb(160, CameraColor), 1f);
            }
            else if (!hasForward && ends.Count == 4)
            {
                // footprint: order the four ends around the origin and join them up
                var sorted = ends.OrderBy(e => Math.Atan2(e.Y - origin.Y, e.X - origin.X)).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    DrawLine(im, sorted[i], sorted[(i + 1) % sorted.Count], Color.FromArgb(160, CameraColor), 1f);
                }
            }

            // Camera body: a small square, rotated to face the camera direction
            var f = hasForward ? fwd : new Vector2(0, -1);
            var s = new Vector2(-f.Y, f.X);
            var h = BodyHalfSize;
            var p0 = origin + f * h + s * h;
            var p1 = origin + f * h - s * h;
            var p2 = origin - f * h - s * h;
            var p3 = origin - f * h + s * h;
            DrawLine(im, p0, p1, CameraColor, StrokeWidth);
            DrawLine(im, p1, p2, CameraColor, StrokeWidth);
            DrawLine(im, p2, p3, CameraColor, StrokeWidth);
            DrawLine(im, p3, p0, CameraColor, StrokeWidth);
        }

        private static void DrawLine(I2DRenderer im, Vector2 start, Vector2 end, Color color, float width)
        {
            im.AddLine(start, end, Color.Black, width + OutlineExtraWidth);
            im.AddLine(start, end, color, width);
        }
    }
}
