using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    [Export(typeof(IMapObject2DOverlay))]
    public class EntityNamesOverlay : IMapObject2DOverlay
    {
        private struct Label
        {
            public float Depth;
            public Vector2 Position;
            public Vector2 Size;
            public Color Colour;
            public FontType Font;
            public string Text;
        }

        public void Render(IViewport viewport, ICollection<IMapObject> objects, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im, MapDocument document)
        {
            if (camera.Zoom < 1) return;

            // Escape hatch in case there's too many entities on screen
            var ents = objects.OfType<Entity>().Where(x => x.EntityData != null).Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)).ToList();
            if (ents.Count <= 0 || ents.Count > 1000) return;

            var renderNames = camera.Zoom > 2 && ents.Count < 50;

            var labels = new List<Label>();
            foreach (var ed in ents)
            {
                var c = ed.Color?.Color ?? Color.White;

                var loc = camera.WorldToScreen(ed.BoundingBox.Center);

                var box = ed.BoundingBox;
                var dim = camera.Flatten(box.Dimensions / 2);
                loc.Y -= camera.UnitsToPixels(dim.Y);

                // Higher on the unused axis is nearer the viewer (the 2D draw order puts it on top).
                var depth = camera.GetUnusedValue(box.Center);

                var str = ed.EntityData.Name;
                var targetname = ed.EntityData.Get<string>("targetname")?.Trim() ?? "";

                var size = im.CalcTextSize(FontType.Normal, str);
                var pos = new Vector2(loc.X - size.X / 2, loc.Y - size.Y - 2);
                labels.Add(new Label { Depth = depth, Position = pos, Size = size, Colour = c, Font = FontType.Normal, Text = str });

                if (renderNames && targetname.Length > 0)
                {
                    var nmms = im.CalcTextSize(FontType.Bold, targetname);
                    labels.Add(new Label { Depth = depth, Position = new Vector2(loc.X - nmms.X / 2, loc.Y + 2), Size = nmms, Colour = c, Font = FontType.Bold, Text = targetname });
                }
            }

            // Front to back. A label is drawn only if it doesn't overlap one already drawn, so labels behind
            // another one are culled, and entities stacked on one origin show a single line of text.
            var grid = new LabelGrid();
            foreach (var label in labels.OrderByDescending(l => l.Depth))
            {
                var rect = new RectangleF(label.Position.X, label.Position.Y, label.Size.X, label.Size.Y);
                if (grid.Overlaps(rect)) continue;
                grid.Add(rect);
                im.AddText(label.Position, label.Colour, label.Font, label.Text);
            }
        }

        /// <summary>
        /// Uniform grid of label rectangles, so an overlap test only looks at labels in the cells it touches.
        /// </summary>
        private sealed class LabelGrid
        {
            private const float CellSize = 64f;
            private const float Padding = 1f;

            private readonly Dictionary<(int X, int Y), List<RectangleF>> _cells = new Dictionary<(int X, int Y), List<RectangleF>>();

            public bool Overlaps(RectangleF rect)
            {
                var padded = RectangleF.Inflate(rect, Padding, Padding);
                foreach (var key in CellsFor(padded))
                {
                    if (!_cells.TryGetValue(key, out var list)) continue;
                    foreach (var other in list)
                    {
                        if (other.IntersectsWith(padded)) return true;
                    }
                }
                return false;
            }

            public void Add(RectangleF rect)
            {
                foreach (var key in CellsFor(rect))
                {
                    if (!_cells.TryGetValue(key, out var list))
                    {
                        list = new List<RectangleF>();
                        _cells[key] = list;
                    }
                    list.Add(rect);
                }
            }

            private static IEnumerable<(int X, int Y)> CellsFor(RectangleF rect)
            {
                var x0 = (int) Math.Floor(rect.Left / CellSize);
                var x1 = (int) Math.Floor(rect.Right / CellSize);
                var y0 = (int) Math.Floor(rect.Top / CellSize);
                var y1 = (int) Math.Floor(rect.Bottom / CellSize);
                for (var x = x0; x <= x1; x++)
                {
                    for (var y = y0; y <= y1; y++)
                    {
                        yield return (x, y);
                    }
                }
            }
        }
    }
}
