using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// Shows which objects are linked, in both the 2D and 3D viewports: every linked object gets an outline
    /// in its link group's colour and a label with the group's name, ID, and member count.
    /// When any member of a group is selected, the whole group is drawn heavier (and tinted in 2D)
    /// so it's obvious which other objects an edit will also change.
    /// </summary>
    [Export(typeof(IMapDocumentOverlayRenderable))]
    public class LinkedObjectsOverlay : IMapDocumentOverlayRenderable
    {
        private const float MaxLabelDistance3D = 4000;
        private const int MaxLabelsBeforeCulling = 300;

        private class Entry
        {
            public Box Box;
            public string Label;
            public Color Colour;
            public bool Emphasised;
        }

        private readonly object _lock = new object();
        private MapDocument _document;
        private List<Entry> _entries = new List<Entry>();
        private bool _dirty = true;

        public LinkedObjectsOverlay()
        {
            Oy.Subscribe<Change>("MapDocument:Changed", Changed);
        }

        public void SetActiveDocument(MapDocument doc)
        {
            lock (_lock)
            {
                _document = doc;
                _dirty = true;
            }
        }

        private Task Changed(Change change)
        {
            lock (_lock)
            {
                if (change.Document != _document) return Task.CompletedTask;

                // Only rebuild when it could matter: there's something linked already, or something linked is involved
                if (_entries.Count > 0 || change.HasDataChanges || change.Added.Concat(change.Updated).Concat(change.Removed).Any(x => LinkedObjects.FindLinkedOwner(x) != null))
                {
                    _dirty = true;
                }
            }
            return Task.CompletedTask;
        }

        private List<Entry> GetEntries()
        {
            MapDocument doc;
            lock (_lock)
            {
                if (!_dirty) return _entries;
                doc = _document;
                _dirty = false;
            }

            var list = new List<Entry>();
            if (doc != null)
            {
                try
                {
                    list = Build(doc);
                }
                catch (Exception)
                {
                    // The document changed while we were reading it, try again on the next frame
                    lock (_lock) _dirty = true;
                    lock (_lock) return _entries;
                }
            }

            lock (_lock)
            {
                _entries = list;
                return _entries;
            }
        }

        private static List<Entry> Build(MapDocument doc)
        {
            var list = new List<Entry>();
            var groups = doc.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());

            foreach (var kv in LinkedObjects.GetMembers(doc))
            {
                groups.TryGetValue(kv.Key, out var group);
                var name = group?.Name ?? ("Link " + kv.Key);
                var colour = group?.Colour ?? LinkedObjects.ColourFor(kv.Key);
                var emphasised = kv.Value.Any(x => x.FindAll().Any(c => c.IsSelected));
                var label = $"{name}  #{kv.Key}  ({kv.Value.Count})";

                foreach (var o in kv.Value)
                {
                    if (o.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)) continue;
                    list.Add(new Entry { Box = o.BoundingBox, Label = label, Colour = colour, Emphasised = emphasised });
                }
            }

            return list;
        }

        // The 8 corners of a box. Bit 0 = X, bit 1 = Y, bit 2 = Z (set = the max side)
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
                    (i & 4) == 0 ? s.Z : e.Z
                );
            }
            return c;
        }

        private static Color Faded(Entry e)
        {
            return e.Emphasised ? e.Colour : Color.FromArgb(190, e.Colour);
        }

        private static void DrawLabel(I2DRenderer im, Entry e, Vector2 pos)
        {
            var font = e.Emphasised ? FontType.Bold : FontType.Normal;
            var size = im.CalcTextSize(font, e.Label);
            im.AddRectFilled(pos - new Vector2(2, 1), pos + size + new Vector2(2, 1), Color.FromArgb(190, 0, 0, 0));
            im.AddText(pos, e.Colour, font, e.Label);
        }

        public void Render(IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            var entries = GetEntries();
            if (entries.Count == 0) return;

            var labels = entries.Count <= MaxLabelsBeforeCulling;

            foreach (var e in entries)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var corner in Corners(e.Box))
                {
                    var s = camera.WorldToScreen(corner);
                    minX = Math.Min(minX, s.X);
                    maxX = Math.Max(maxX, s.X);
                    minY = Math.Min(minY, s.Y);
                    maxY = Math.Max(maxY, s.Y);
                }

                if (maxX < 0 || maxY < 0 || minX > camera.Width || minY > camera.Height) continue;

                // Sit just outside the object
                minX -= 3; minY -= 3; maxX += 3; maxY += 3;
                var a = new Vector2(minX, minY);
                var b = new Vector2(maxX, maxY);

                if (e.Emphasised) im.AddRectFilled(a, b, Color.FromArgb(30, e.Colour));

                var width = e.Emphasised ? 2.5f : 1.5f;
                var col = Faded(e);
                im.AddLine(new Vector2(minX, minY), new Vector2(maxX, minY), col, width, false);
                im.AddLine(new Vector2(maxX, minY), new Vector2(maxX, maxY), col, width, false);
                im.AddLine(new Vector2(maxX, maxY), new Vector2(minX, maxY), col, width, false);
                im.AddLine(new Vector2(minX, maxY), new Vector2(minX, minY), col, width, false);

                if (!labels && !e.Emphasised) continue;

                var font = e.Emphasised ? FontType.Bold : FontType.Normal;
                var size = im.CalcTextSize(font, e.Label);
                var pos = new Vector2(minX, minY - size.Y - 4);
                if (pos.Y < 0) pos.Y = maxY + 4; // no room above, put it underneath
                DrawLabel(im, e, pos);
            }
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            var entries = GetEntries();
            if (entries.Count == 0) return;

            var cameraPos = camera.Position;
            var cameraDir = camera.Direction;
            var labels = entries.Count <= MaxLabelsBeforeCulling;

            foreach (var e in entries)
            {
                var centre = e.Box.Center;
                var distance = (centre - cameraPos).Length();
                if (!e.Emphasised && distance > MaxLabelDistance3D * 2) continue;

                var corners = Corners(e.Box);
                var screen = new Vector2[8];
                var visible = new bool[8];
                var any = false;
                for (var i = 0; i < 8; i++)
                {
                    visible[i] = Vector3.Dot(corners[i] - cameraPos, cameraDir) > 1f;
                    if (!visible[i]) continue;
                    var s = camera.WorldToScreen(corners[i]);
                    screen[i] = new Vector2(s.X, s.Y);
                    any = true;
                }
                if (!any) continue;

                var width = e.Emphasised ? 2.5f : 1.5f;
                var col = Faded(e);
                for (var i = 0; i < 8; i++)
                {
                    foreach (var bit in new[] { 1, 2, 4 })
                    {
                        if ((i & bit) != 0) continue;
                        var j = i | bit;
                        if (!visible[i] || !visible[j]) continue;
                        im.AddLine(screen[i], screen[j], col, width, false);
                    }
                }

                if (!labels && !e.Emphasised) continue;
                if (!e.Emphasised && distance > MaxLabelDistance3D) continue;

                // Label above the middle of the top of the object
                var anchor = new Vector3(centre.X, centre.Y, e.Box.End.Z);
                if (Vector3.Dot(anchor - cameraPos, cameraDir) <= 1f) continue;
                var a = camera.WorldToScreen(anchor);
                if (a.X < -200 || a.Y < -50 || a.X > camera.Width + 200 || a.Y > camera.Height + 50) continue;

                var font = e.Emphasised ? FontType.Bold : FontType.Normal;
                var size = im.CalcTextSize(font, e.Label);
                DrawLabel(im, e, new Vector2(a.X - size.X / 2, a.Y - size.Y - 6));
            }
        }
    }
}
