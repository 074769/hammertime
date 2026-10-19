using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// The "Reference Box": a box pinned from the Select tool's sidebar that stays visible in every 2D and 3D
    /// viewport whatever tool is active, as a measuring aid. It draws only the outline and its size in units.
    /// It is purely visual: it has no handles, never takes mouse input and never selects anything, so every
    /// tool keeps working through and around it exactly as before.
    ///
    /// The box is kept per document (in memory only, it is not saved in the map).
    /// </summary>
    [Export(typeof(IMapDocumentOverlayRenderable))]
    public class ReferenceBoxOverlay : IMapDocumentOverlayRenderable
    {
        private class BoxState
        {
            public Box Box;

            /// <summary>The checkbox is on but there was nothing to pin yet; pins the selection's box as soon as there is one.</summary>
            public bool Armed;
        }

        private static readonly Color LineColour = Color.FromArgb(255, 0, 220, 255);
        private const float LineWidth = 1.5f;

        private static readonly ConditionalWeakTable<MapDocument, BoxState> States = new ConditionalWeakTable<MapDocument, BoxState>();
        private static readonly object Lock = new object();

        private WeakReference<MapDocument> _document = new WeakReference<MapDocument>(null);

        // --- Public API (used by the Select tool) ---

        /// <summary>True if the document has a pinned reference box, or is waiting to pin one.</summary>
        public static bool IsOn(MapDocument doc)
        {
            if (doc == null) return false;
            lock (Lock) return States.TryGetValue(doc, out var s) && (s.Box != null || s.Armed);
        }

        /// <summary>Pin a box as the document's reference box. A null or empty box arms it instead (it pins the next selection).</summary>
        public static void Pin(MapDocument doc, Box box)
        {
            if (doc == null) return;
            lock (Lock)
            {
                var s = States.GetOrCreateValue(doc);
                if (box == null || box.IsEmpty())
                {
                    s.Box = null;
                    s.Armed = true;
                }
                else
                {
                    s.Box = new Box(box.Start, box.End);
                    s.Armed = false;
                }
            }
        }

        /// <summary>Remove the document's reference box.</summary>
        public static void Clear(MapDocument doc)
        {
            if (doc == null) return;
            lock (Lock)
            {
                if (States.TryGetValue(doc, out var s))
                {
                    s.Box = null;
                    s.Armed = false;
                }
            }
        }

        // --- Overlay ---

        public void SetActiveDocument(MapDocument doc)
        {
            _document = new WeakReference<MapDocument>(doc);
        }

        private Box GetBox()
        {
            if (!_document.TryGetTarget(out var doc) || doc == null) return null;
            lock (Lock)
            {
                if (!States.TryGetValue(doc, out var s)) return null;

                if (s.Box == null && s.Armed && !doc.Selection.IsEmpty)
                {
                    var sel = doc.Selection.GetSelectionBoundingBox();
                    if (sel != null && !sel.IsEmpty())
                    {
                        s.Box = new Box(sel.Start, sel.End);
                        s.Armed = false;
                    }
                }

                return s.Box;
            }
        }

        private static string Units(float value) => Math.Abs((float) Math.Round(value, 1)).ToString("0.##");

        public void Render(IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            var box = GetBox();
            if (box == null) return;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in Corners(box))
            {
                var s = camera.WorldToScreen(corner);
                minX = Math.Min(minX, s.X);
                maxX = Math.Max(maxX, s.X);
                minY = Math.Min(minY, s.Y);
                maxY = Math.Max(maxY, s.Y);
            }

            if (maxX < 0 || maxY < 0 || minX > camera.Width || minY > camera.Height) return;

            im.AddLine(new Vector2(minX, minY), new Vector2(maxX, minY), LineColour, LineWidth, false);
            im.AddLine(new Vector2(maxX, minY), new Vector2(maxX, maxY), LineColour, LineWidth, false);
            im.AddLine(new Vector2(maxX, maxY), new Vector2(minX, maxY), LineColour, LineWidth, false);
            im.AddLine(new Vector2(minX, maxY), new Vector2(minX, minY), LineColour, LineWidth, false);

            // Size of the box as seen in this view
            var st = camera.Flatten(box.Start);
            var en = camera.Flatten(box.End);
            var widthText = Units(en.X - st.X);
            var heightText = Units(en.Y - st.Y);

            var wSize = im.CalcTextSize(FontType.Large, widthText);
            var hSize = im.CalcTextSize(FontType.Large, heightText);
            const float pad = 6;

            // Width under the box, height to its right, both kept inside the viewport
            var wPos = new Vector2((minX + maxX - wSize.X) / 2, maxY + pad);
            wPos.X = Math.Max(0, Math.Min(wPos.X, camera.Width - wSize.X - pad));
            wPos.Y = Math.Max(0, Math.Min(wPos.Y, camera.Height - wSize.Y - pad));

            var hPos = new Vector2(maxX + pad, (minY + maxY - hSize.Y) / 2);
            hPos.X = Math.Max(0, Math.Min(hPos.X, camera.Width - hSize.X - pad));
            hPos.Y = Math.Max(0, Math.Min(hPos.Y, camera.Height - hSize.Y - pad));

            im.AddText(wPos, LineColour, FontType.Large, widthText);
            im.AddText(hPos, LineColour, FontType.Large, heightText);
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            var box = GetBox();
            if (box == null) return;

            var cameraPos = camera.Position;
            var cameraDir = camera.Direction;

            var corners = Corners(box);
            var screen = new Vector2[8];
            var visible = new bool[8];
            for (var i = 0; i < 8; i++)
            {
                visible[i] = Vector3.Dot(corners[i] - cameraPos, cameraDir) > 1f;
                if (!visible[i]) continue;
                var s = camera.WorldToScreen(corners[i]);
                screen[i] = new Vector2(s.X, s.Y);
            }

            // Corner index bits: 1 = X, 2 = Y, 4 = Z. For each axis, remember the edge nearest the camera for its label.
            var bits = new[] { 1, 2, 4 };
            var sizes = new[] { box.Width, box.Length, box.Height };
            var bestDistance = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var bestMid = new Vector2[3];
            var hasLabel = new bool[3];

            for (var i = 0; i < 8; i++)
            {
                for (var a = 0; a < 3; a++)
                {
                    if ((i & bits[a]) != 0) continue;
                    var j = i | bits[a];
                    if (!visible[i] || !visible[j]) continue;

                    im.AddLine(screen[i], screen[j], LineColour, LineWidth, false);

                    var distance = ((corners[i] + corners[j]) / 2 - cameraPos).LengthSquared();
                    if (distance < bestDistance[a])
                    {
                        bestDistance[a] = distance;
                        bestMid[a] = (screen[i] + screen[j]) / 2;
                        hasLabel[a] = true;
                    }
                }
            }

            for (var a = 0; a < 3; a++)
            {
                if (!hasLabel[a]) continue;
                var mid = bestMid[a];
                if (mid.X < -50 || mid.Y < -50 || mid.X > camera.Width + 50 || mid.Y > camera.Height + 50) continue;

                var text = Units(sizes[a]);
                var size = im.CalcTextSize(FontType.Large, text);
                im.AddText(new Vector2(mid.X - size.X / 2, mid.Y - size.Y / 2), LineColour, FontType.Large, text);
            }
        }

        private static Vector3[] Corners(Box box)
        {
            var s = box.Start;
            var e = box.End;
            var c = new Vector3[8];
            for (var i = 0; i < 8; i++)
            {
                c[i] = new Vector3(
                    (i & 1) == 0 ? s.X : e.X,
                    (i & 2) == 0 ? s.Y : e.Y,
                    (i & 4) == 0 ? s.Z : e.Z);
            }
            return c;
        }
    }
}
