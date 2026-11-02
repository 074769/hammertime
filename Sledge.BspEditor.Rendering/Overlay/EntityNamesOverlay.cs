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
using Sledge.BspEditor.Rendering.Scene;
using Sledge.Common.Logging;
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

        // Text sizes depend only on font and text, and stacked entities usually share a name, so each
        // size is measured once instead of every frame. Cleared if it grows too large.
        private const int MaxCachedSizes = 4096;
        private static readonly Dictionary<(FontType Font, string Text), Vector2> SizeCache
            = new Dictionary<(FontType Font, string Text), Vector2>();

        // Labels whose anchor is this far outside the viewport are skipped before any text is measured.
        private const float OffscreenMargin = 512f;

        private static Vector2 MeasureText(I2DRenderer im, FontType font, string text)
        {
            if (SizeCache.TryGetValue((font, text), out var size)) return size;
            if (SizeCache.Count >= MaxCachedSizes) SizeCache.Clear();
            size = im.CalcTextSize(font, text);
            SizeCache[(font, text)] = size;
            return size;
        }

        /// <summary>
        /// The labels a view drew the last time, and what they were worked out for. Nothing is recalculated
        /// while the map, the visible objects and the camera stay the same, which is the case for most frames
        /// (for example the whole time a selection is being dragged, since the map doesn't change until release).
        /// </summary>
        private sealed class ViewLabels
        {
            public MapDocument Document;
            public long Revision;
            public ICollection<IMapObject> Objects;
            public Vector3 Position;
            public float Zoom;
            public int Width;
            public int Height;
            public OrthographicType ViewType;
            public Label[] Drawn = new Label[0];
        }

        private static readonly ConditionalWeakTable<IViewport, ViewLabels> Views = new ConditionalWeakTable<IViewport, ViewLabels>();

        public void Render(IViewport viewport, ICollection<IMapObject> objects, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im, MapDocument document)
        {
            if (camera.Zoom < 1) return;

            var revision = DocumentRevision.Get(document);
            var view = Views.GetOrCreateValue(viewport);
            var upToDate = ReferenceEquals(view.Document, document)
                           && view.Revision == revision
                           && ReferenceEquals(view.Objects, objects)
                           && view.Position == camera.Position
                           && view.Zoom == camera.Zoom
                           && view.Width == camera.Width
                           && view.Height == camera.Height
                           && view.ViewType == camera.ViewType;

            if (!upToDate)
            {
                PerfLog.CountForView("Labels: rebuilt", viewport.ID);
                view.Drawn = BuildLabels(objects, camera, im);
                view.Document = document;
                view.Revision = revision;
                view.Objects = objects;
                view.Position = camera.Position;
                view.Zoom = camera.Zoom;
                view.Width = camera.Width;
                view.Height = camera.Height;
                view.ViewType = camera.ViewType;
            }
            else
            {
                PerfLog.CountForView("Labels: cached", viewport.ID);
            }

            var drawn = view.Drawn;
            for (var i = 0; i < drawn.Length; i++)
            {
                im.AddText(drawn[i].Position, drawn[i].Colour, drawn[i].Font, drawn[i].Text);
            }
        }

        /// <summary>Works out which labels to draw for the objects in a view.</summary>
        private static Label[] BuildLabels(ICollection<IMapObject> objects, OrthographicCamera camera, I2DRenderer im)
        {
            // Escape hatch in case there's too many entities on screen
            var ents = objects.OfType<Entity>().Where(x => x.EntityData != null).Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)).ToList();
            if (ents.Count <= 0 || ents.Count > 1000) return new Label[0];

            var renderNames = camera.Zoom > 2 && ents.Count < 50;

            var labels = new List<Label>();
            foreach (var ed in ents)
            {
                var c = ed.Color?.Color ?? Color.White;

                var loc = camera.WorldToScreen(ed.BoundingBox.Center);
                if (loc.X < -OffscreenMargin || loc.X > camera.Width + OffscreenMargin
                    || loc.Y < -OffscreenMargin || loc.Y > camera.Height + OffscreenMargin) continue;

                var box = ed.BoundingBox;
                var dim = camera.Flatten(box.Dimensions / 2);
                loc.Y -= camera.UnitsToPixels(dim.Y);

                // Higher on the unused axis is nearer the viewer (the 2D draw order puts it on top).
                var depth = camera.GetUnusedValue(box.Center);

                var str = ed.EntityData.Name;
                var targetname = ed.EntityData.Get<string>("targetname")?.Trim() ?? "";

                var size = MeasureText(im, FontType.Normal, str);
                var pos = new Vector2(loc.X - size.X / 2, loc.Y - size.Y - 2);
                labels.Add(new Label { Depth = depth, Position = pos, Size = size, Colour = c, Font = FontType.Normal, Text = str });

                if (renderNames && targetname.Length > 0)
                {
                    var nmms = MeasureText(im, FontType.Bold, targetname);
                    labels.Add(new Label { Depth = depth, Position = new Vector2(loc.X - nmms.X / 2, loc.Y + 2), Size = nmms, Colour = c, Font = FontType.Bold, Text = targetname });
                }
            }

            // Front to back. A label is drawn only if it doesn't overlap one already drawn, so labels behind
            // another one are culled, and entities stacked on one origin show a single line of text.
            var grid = new LabelGrid();
            var drawn = new List<Label>();
            foreach (var label in labels.OrderByDescending(l => l.Depth))
            {
                var rect = new RectangleF(label.Position.X, label.Position.Y, label.Size.X, label.Size.Y);
                if (grid.Overlaps(rect)) continue;
                grid.Add(rect);
                drawn.Add(label);
            }
            return drawn.ToArray();
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
