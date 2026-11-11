using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// Draws the area of the compile log entry that is currently selected in the Compile Errors window:
    /// a wireframe box, or a polygon for leaf portals. Nothing is drawn when no entry is selected.
    /// </summary>
    [Export(typeof(IMapDocumentOverlayRenderable))]
    public class CompileErrorsOverlay : IMapDocumentOverlayRenderable
    {
        private static readonly Color LineColour = Color.FromArgb(255, 255, 64, 64);
        private const float LineWidth = 2f;
        private const float MarkerSize = 16f;

        private static readonly object Lock = new object();
        private static WeakReference<MapDocument> _target = new WeakReference<MapDocument>(null);
        private static Box _box;
        private static Vector3[] _points = new Vector3[0];

        private WeakReference<MapDocument> _document = new WeakReference<MapDocument>(null);

        public static void Show(MapDocument doc, Box box, IEnumerable<Vector3> points)
        {
            lock (Lock)
            {
                _target = new WeakReference<MapDocument>(doc);
                _box = box;
                _points = points == null ? new Vector3[0] : new List<Vector3>(points).ToArray();
            }
        }

        public static void Clear()
        {
            lock (Lock)
            {
                _target = new WeakReference<MapDocument>(null);
                _box = null;
                _points = new Vector3[0];
            }
        }

        public void SetActiveDocument(MapDocument doc)
        {
            _document = new WeakReference<MapDocument>(doc);
        }

        public void Render(IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            var lines = BuildLines();
            foreach (var l in lines)
            {
                var a = camera.WorldToScreen(l.Item1);
                var b = camera.WorldToScreen(l.Item2);
                im.AddLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), LineColour, LineWidth, false);
            }
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            var lines = BuildLines();
            var cameraPos = camera.Position;
            var cameraDir = camera.Direction;
            foreach (var l in lines)
            {
                // Skip anything behind the camera, the same way the reference box does
                if (Vector3.Dot(l.Item1 - cameraPos, cameraDir) <= 1f) continue;
                if (Vector3.Dot(l.Item2 - cameraPos, cameraDir) <= 1f) continue;

                var a = camera.WorldToScreen(l.Item1);
                var b = camera.WorldToScreen(l.Item2);
                im.AddLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), LineColour, LineWidth, false);
            }
        }

        /// <summary>Returns the segments to draw for the current shape, or none if this document isn't the target.</summary>
        private List<(Vector3, Vector3)> BuildLines()
        {
            var result = new List<(Vector3, Vector3)>();
            Box box;
            Vector3[] points;

            lock (Lock)
            {
                if (!_document.TryGetTarget(out var doc) || doc == null) return result;
                if (!_target.TryGetTarget(out var target) || target != doc) return result;
                box = _box;
                points = _points;
            }

            if (box != null)
            {
                AddBoxEdges(result, box.Start, box.End);
            }
            else if (points.Length == 1)
            {
                var p = points[0];
                AddBoxEdges(result, p - Vector3.One * MarkerSize, p + Vector3.One * MarkerSize);
            }
            else if (points.Length >= 2)
            {
                // Portals are closed polygons; anything with two points is just a line
                var closed = points.Length >= 3;
                var count = closed ? points.Length : points.Length - 1;
                for (var i = 0; i < count; i++)
                {
                    result.Add((points[i], points[(i + 1) % points.Length]));
                }
            }

            return result;
        }

        private static void AddBoxEdges(List<(Vector3, Vector3)> result, Vector3 s, Vector3 e)
        {
            // Corner index bits: 1 = X, 2 = Y, 4 = Z. Corners that differ by one bit share an edge.
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                corners[i] = new Vector3(
                    (i & 1) == 0 ? s.X : e.X,
                    (i & 2) == 0 ? s.Y : e.Y,
                    (i & 4) == 0 ? s.Z : e.Z);
            }

            for (var i = 0; i < 8; i++)
            {
                foreach (var bit in new[] { 1, 2, 4 })
                {
                    var j = i ^ bit;
                    if (j > i) result.Add((corners[i], corners[j]));
                }
            }
        }
    }
}
