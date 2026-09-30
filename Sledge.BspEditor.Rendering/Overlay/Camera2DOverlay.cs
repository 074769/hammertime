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
    /// Draws the 3D viewport's camera into every 2D viewport as a small 3D camera widget:
    /// a box for the camera body, a wireframe view pyramid in front of it, and a little
    /// "up" triangle on top of the pyramid. The widget is a real 3D object that is projected
    /// into each 2D view, so it looks right from Top, Front and Side alike - e.g. looking
    /// straight up/down gives an "X" in a rectangle in the Top view and a thin triangle in
    /// the Front/Side views.
    ///
    /// The widget has a fixed size on screen (it does not grow with zoom or with the camera
    /// movement); it only reads the camera's position and direction, so it follows the 3D
    /// camera live. The pyramid's proportions follow the camera's FOV and aspect ratio, but
    /// are normalised so it always stays small (set FollowFov to false for a completely
    /// fixed shape).
    /// </summary>
    [Export(typeof(IOverlayRenderable))]
    public class Camera2DOverlay : IOverlayRenderable
    {
        /// <summary>Master on/off switch, driven by the "Camera widget" checkbox in the Selection tool's 3D widgets panel.</summary>
        public static bool ShowCameraWidget
        {
            get => _showCameraWidget;
            set => _showCameraWidget = value;
        }
        private static volatile bool _showCameraWidget = true;

        // --- Size, in screen pixels (tweak these to taste) ---
        private const bool FollowFov = true;         // false = always the same fixed pyramid shape
        private const float BodyHalfSize = 5f;       // camera body box: half width/height
        private const float BodyLength = 11f;        // camera body box: length behind the lens
        private const float MaxFarHalfExtent = 22f;  // pyramid: largest half-extent of the far rectangle
        private const float MinDepth = 12f;          // pyramid: depth limits
        private const float MaxDepth = 26f;
        private const float FixedDepth = 20f;        // used when FollowFov is false
        private const float FixedHalfWidth = 14f;
        private const float FixedHalfHeight = 10f;

        private const float StrokeWidth = 2f;
        private const float OutlineExtraWidth = 2f;

        private static readonly Color CameraColor = Color.FromArgb(255, 255, 210, 0);
        private static readonly Color FaintColor = Color.FromArgb(170, 255, 210, 0);

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
            if (!ShowCameraWidget) return;

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

            // Everything below is laid out in screen pixels, then converted to world units so the
            // widget keeps the same size on screen at any zoom level.
            var px = view.PixelsToUnits(1);
            var pos = cam.Position;

            // Pyramid dimensions (pixels)
            float depth, halfW, halfH;
            if (FollowFov)
            {
                var aspect = cam.Height > 0 ? cam.Width / (float) cam.Height : 1f;
                if (aspect <= 0) aspect = 1f;
                var ty = (float) Math.Tan(cam.FOV * Math.PI / 360.0); // vertical FOV, in degrees
                var tx = ty * aspect;
                var m = Math.Max(tx, ty);
                if (m < 0.01f) m = 0.01f;
                depth = Math.Min(MaxDepth, Math.Max(MinDepth, MaxFarHalfExtent / m));
                halfW = Math.Min(MaxFarHalfExtent, depth * tx);
                halfH = Math.Min(MaxFarHalfExtent, depth * ty);
            }
            else
            {
                depth = FixedDepth;
                halfW = FixedHalfWidth;
                halfH = FixedHalfHeight;
            }

            // Local camera-space -> world (x = right, y = up, z = forward), sizes in pixels
            Vector2 P(float x, float y, float z)
            {
                var w = pos + (right * x + up * y + dir * z) * px;
                var s = view.WorldToScreen(w);
                return new Vector2(s.X, s.Y);
            }

            var origin = P(0, 0, 0);

            // View pyramid: apex at the camera position, far rectangle in front of it
            var fTL = P(-halfW, halfH, depth);
            var fTR = P(halfW, halfH, depth);
            var fBR = P(halfW, -halfH, depth);
            var fBL = P(-halfW, -halfH, depth);

            Line(im, origin, fTL, CameraColor);
            Line(im, origin, fTR, CameraColor);
            Line(im, origin, fBR, CameraColor);
            Line(im, origin, fBL, CameraColor);

            Line(im, fTL, fTR, FaintColor, 1f);
            Line(im, fTR, fBR, FaintColor, 1f);
            Line(im, fBR, fBL, FaintColor, 1f);
            Line(im, fBL, fTL, FaintColor, 1f);

            // "Up" marker: a small triangle sitting on top of the far rectangle
            var uL = P(-halfW * 0.4f, halfH * 1.12f, depth);
            var uR = P(halfW * 0.4f, halfH * 1.12f, depth);
            var uT = P(0, halfH * 1.5f, depth);
            Line(im, uL, uR, FaintColor, 1f);
            Line(im, uR, uT, FaintColor, 1f);
            Line(im, uT, uL, FaintColor, 1f);

            // Camera body: a small box behind the lens, oriented with the camera
            var b = BodyHalfSize;
            var l = BodyLength;
            var b0 = new[]
            {
                P(-b,  b, 0), P(b,  b, 0), P(b, -b, 0), P(-b, -b, 0)   // front face (at the lens)
            };
            var b1 = new[]
            {
                P(-b,  b, -l), P(b,  b, -l), P(b, -b, -l), P(-b, -b, -l) // back face
            };
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                Line(im, b0[i], b0[j], CameraColor);
                Line(im, b1[i], b1[j], CameraColor);
                Line(im, b0[i], b1[i], CameraColor);
            }
        }

        private static void Line(I2DRenderer im, Vector2 start, Vector2 end, Color color, float width = StrokeWidth)
        {
            im.AddLine(start, end, Color.Black, width + OutlineExtraWidth);
            im.AddLine(start, end, color, width);
        }
    }
}
