using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands.Clipboard;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Rendering.Cameras;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;
using Plane = Sledge.DataStructures.Geometric.Plane;
using Sledge.Common.Shell.Documents;

namespace Sledge.BspEditor.Tools.Clip
{
    [Export(typeof(ITool))]
    [OrderHint("N")]
    [DefaultHotkey("Shift+X")]
    public class ClipTool : BaseTool
    {
        public static readonly Capability ClipToolCapability = Capability.Create("ClipTool");
		public enum ClipState
        {
            None,
            Drawing,
            Drawn,
            MovingPoint1,
            MovingPoint2
        }

        public enum ClipSide
        {
            Both,
            Front,
            Back
        }

        private Vector3? _clipPlanePoint1;
        private Vector3? _clipPlanePoint2;
        private Vector3? _clipPlanePoint3;
        private Vector3? _drawingPoint;
        private Vector3 _planeDir = new Vector3(0, 0, 128); // third plane point is always point 1 + this (not a handle)
        private ClipState _prevState;
        private ClipState _state;
        private ClipSide _side;

        public ClipTool()
        {
            Usage = ToolUsage.Both;
            _clipPlanePoint1 = _clipPlanePoint2 = _clipPlanePoint3 = _drawingPoint = null;
            _state = _prevState = ClipState.None;
            _side = ClipSide.Both;
        }

        public override Image GetIcon()
        {
            return IconLoader.Load(typeof(Resources), nameof(Resources.Tool_Clip));
        }

        public override string GetName()
        {
            return "Clip Tool";
        }
        public override Capability ToolCapability => ClipToolCapability;

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<ClipTool>("Tool:Activated", t => CycleClipSide());
            yield return Oy.Subscribe<string>("ClipTool:SetClipSide", v => SetClipSide(v));
            yield return Oy.Subscribe<RightClickMenuBuilder>("MapViewport:RightClick", b =>
            {
                // Right click switches the clip mode instead of opening the context menu
                b.Intercepted = true;
                CycleClipSide();
            });
        }

        private void SetClipSide(string visiblePoints)
        {
            if (Enum.TryParse(visiblePoints, true, out ClipSide s) && s != _side)
            {
                _side = s;
            }
        }
        
        private void CycleClipSide()
        {
            var side = (int) _side;
            side = (side + 1) % Enum.GetValues(typeof (ClipSide)).Length;
            _side = (ClipSide) side;
        }

        private ClipState GetStateAtPoint(int x, int y, OrthographicCamera camera)
        {
            if (_clipPlanePoint1 == null || _clipPlanePoint2 == null) return ClipState.None;

            var p = camera.Flatten(camera.ScreenToWorld(new Vector3(x, y, 0)));
            var p1 = camera.Flatten(_clipPlanePoint1.Value);
            var p2 = camera.Flatten(_clipPlanePoint2.Value);

            var d = 5 / camera.Zoom;

            if (p.X >= p1.X - d && p.X <= p1.X + d && p.Y >= p1.Y - d && p.Y <= p1.Y + d) return ClipState.MovingPoint1;
            if (p.X >= p2.X - d && p.X <= p2.X + d && p.Y >= p2.Y - d && p.Y <= p2.Y + d) return ClipState.MovingPoint2;

            return ClipState.None;
        }

        protected override void MouseDown(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            _prevState = _state;

            var point = SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
            var st = GetStateAtPoint(e.X, e.Y, camera);
            if (_state == ClipState.None || st == ClipState.None)
            {
                _state = ClipState.Drawing;
                _drawingPoint = point;
                _planeDir = camera.GetUnusedCoordinate(new Vector3(128, 128, 128));
            }
            else if (_state == ClipState.Drawn)
            {
                _state = st;
            }
        }

        protected override void MouseUp(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            _state = _state == ClipState.Drawing ? _prevState : ClipState.Drawn;
        }

        protected override void MouseMove(MapDocument document, MapViewport vp, OrthographicCamera camera, ViewportEvent e)
        {
            var viewport = vp;

            var point = SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
            var st = GetStateAtPoint(e.X, e.Y, camera);
            if (_state == ClipState.Drawing)
            {
                _state = ClipState.MovingPoint2;
                _clipPlanePoint1 = _drawingPoint;
                _clipPlanePoint2 = point;
                UpdatePoint3();
            }
            else if (_state == ClipState.MovingPoint1)
            {
                // Move point 1
                var cp1 = camera.GetUnusedCoordinate(_clipPlanePoint1.Value) + point;
                if (KeyboardState.Ctrl)
                {
                    var diff = _clipPlanePoint1 - cp1;
                    _clipPlanePoint2 -= diff;
                }
                _clipPlanePoint1 = cp1;
                UpdatePoint3();
            }
            else if (_state == ClipState.MovingPoint2)
            {
                // Move point 2
                var cp2 = camera.GetUnusedCoordinate(_clipPlanePoint2.Value) + point;
                if (KeyboardState.Ctrl)
                {
                    var diff = _clipPlanePoint2 - cp2;
                    _clipPlanePoint1 -= diff;
                }
                _clipPlanePoint2 = cp2;
                UpdatePoint3();
            }

            if (st != ClipState.None || _state != ClipState.None && _state != ClipState.Drawn)
            {
                viewport.Control.Cursor = Cursors.Cross;
            }
            else
            {
                viewport.Control.Cursor = Cursors.Default;
            }
        }

        private void UpdatePoint3()
        {
            if (_clipPlanePoint1 != null) _clipPlanePoint3 = _clipPlanePoint1.Value + _planeDir;
        }

        private bool TryGetClipPlane(out Plane plane)
        {
            plane = null;
            if (_clipPlanePoint1 == null || _clipPlanePoint2 == null || _clipPlanePoint3 == null) return false;
            var p1 = _clipPlanePoint1.Value;
            var p2 = _clipPlanePoint2.Value;
            var p3 = _clipPlanePoint3.Value;
            // Points too close together or in a line: no usable plane
            if (Vector3.Cross(p2 - p1, p3 - p1).LengthSquared() < 0.01f) return false;
            plane = new Plane(p1, p2, p3);
            return true;
        }

        private string GetSideDescription()
        {
            switch (_side)
            {
                case ClipSide.Front: return "Clip: keep front (right click to change)";
                case ClipSide.Back: return "Clip: keep back (right click to change)";
                default: return "Clip: keep both (right click to change)";
            }
        }

        // Preview of the result: which polygons stay and which get cut away. Cached because 2D views ask every frame.
        private List<Polygon> _keptPolys = new List<Polygon>();
        private List<Polygon> _removedPolys = new List<Polygon>();
        private string _previewKey;

        private void GetPreview(MapDocument document, out List<Polygon> kept, out List<Polygon> removed)
        {
            kept = _keptPolys;
            removed = _removedPolys;

            if (!TryGetClipPlane(out var plane) || document.Selection.IsEmpty)
            {
                _keptPolys = kept = new List<Polygon>();
                _removedPolys = removed = new List<Polygon>();
                _previewKey = null;
                return;
            }

            var solids = document.Selection.OfType<Solid>().ToList();
            var key = new System.Text.StringBuilder();
            key.Append(plane.Normal).Append(plane.DistanceFromOrigin).Append(_side);
            foreach (var solid in solids)
            {
                var bb = solid.BoundingBox;
                key.Append('|').Append(solid.ID).Append(bb.Start).Append(bb.End);
            }
            var k = key.ToString();
            if (k == _previewKey) return;
            _previewKey = k;

            var keptList = new List<Polygon>();
            var removedList = new List<Polygon>();
            var pp = plane.ToPrecisionPlane();
            foreach (var solid in solids)
            {
                var s = solid.ToPolyhedron().ToPrecisionPolyhedron();
                s.Split(pp, out var back, out var front);

                // Mirrors PerformClip: Front keeps only the front part, Back keeps only the back part
                if (back != null) (_side != ClipSide.Front ? keptList : removedList).AddRange(back.Polygons.Select(x => x.ToStandardPolygon()));
                if (front != null) (_side != ClipSide.Back ? keptList : removedList).AddRange(front.Polygons.Select(x => x.ToStandardPolygon()));
            }
            _keptPolys = kept = keptList;
            _removedPolys = removed = removedList;
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            OnKeyDown(document, viewport, e);
            base.KeyDown(document, viewport, camera, e);
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            OnKeyDown(document, viewport, e);
            base.KeyDown(document, viewport, camera, e);
        }

        private void OnKeyDown(MapDocument document, MapViewport viewport, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Enter && _state != ClipState.None)
            {
                if (TryGetClipPlane(out _)) // Don't clip if the points are too close together
                {
                    PerformClip(document);
                }
            }
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) // Escape cancels, Enter commits and resets
            {
                _clipPlanePoint1 = _clipPlanePoint2 = _clipPlanePoint3 = _drawingPoint = null;
                _state = _prevState = ClipState.None;
            }
        }

        private void PerformClip(MapDocument document)
        {
            var objects = document.Selection.OfType<Solid>().ToList();
            if (!objects.Any()) return;

            if (!TryGetClipPlane(out var plane)) return;
            var clip = new Transaction();
            var found = false;
            var removedOnly = new List<IMapObject>(); // solids that vanish without a replacement
            foreach (var solid in objects)
            {
                solid.Split(document.Map.NumberGenerator, plane, out var backSolid, out var frontSolid);
                found = true;
                
                // Remove the clipped solid
                clip.Add(new Detatch(solid.Hierarchy.Parent.ID, solid));

                var keepsFront = _side != ClipSide.Back && frontSolid != null;
                var keepsBack = _side != ClipSide.Front && backSolid != null;
                if (!keepsFront && !keepsBack) removedOnly.Add(solid);
                
                if (_side != ClipSide.Back && frontSolid != null)
                {
                    // Add front solid
                    clip.Add(new Attach(solid.Hierarchy.Parent.ID, frontSolid));
                }
                
                if (_side != ClipSide.Front && backSolid != null)
                {
                    // Add back solid
                    clip.Add(new Attach(solid.Hierarchy.Parent.ID, backSolid));
                }
            }
            if (found)
            {
                // Groups left with no children (everything in them was clipped away) go too
                clip.AddRange(GroupCleanup.DetatchEmptiedGroups(removedOnly));
                MapDocumentOperation.Perform(document, clip);
            }
        }

        private static void AppendWireframe(BufferBuilder builder, List<Polygon> polygons, Vector4 colour)
        {
            var verts = new List<VertexStandard>();
            var indices = new List<int>();

            foreach (var polygon in polygons)
            {
                var c = verts.Count;
                verts.AddRange(polygon.Vertices.Select(x => new VertexStandard { Position = x, Colour = colour, Tint = Vector4.One }));
                for (var i = 0; i < polygon.Vertices.Count; i++)
                {
                    indices.Add(c + i);
                    indices.Add(c + (i + 1) % polygon.Vertices.Count);
                }
            }

            if (indices.Count == 0) return;
            builder.Append(
                verts, indices.Select(x => (uint) x),
                new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, 0, (uint) indices.Count) }
            );
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            if (_state == ClipState.None || _clipPlanePoint1 == null || _clipPlanePoint2 == null) return;

            var p1 = _clipPlanePoint1.Value;
            var p2 = _clipPlanePoint2.Value;

            // Draw the clip line
            builder.Append(
                new[]
                {
                    new VertexStandard { Position = p1, Colour = Vector4.One, Tint = Vector4.One },
                    new VertexStandard { Position = p2, Colour = Vector4.One, Tint = Vector4.One },
                },
                new uint[] { 0, 1 },
                new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, 2) }
            );

            if (!TryGetClipPlane(out var plane) || document.Selection.IsEmpty) return;
            var pp = plane.ToPrecisionPlane();

            // Draw the result: red = gets cut away, white = stays
            GetPreview(document, out var kept, out var removed);
            AppendWireframe(builder, removed, new Vector4(1, 0, 0, 1));
            AppendWireframe(builder, kept, Vector4.One);

            // Draw the clipping plane
            var poly = new DataStructures.Geometric.Precision.Polygon(pp);
            var bbox = document.Selection.GetSelectionBoundingBox();
            var point = bbox.Center;
            foreach (var boxPlane in bbox.GetBoxPlanes())
            {
                var proj = boxPlane.Project(point);
                var dist = (point - proj).Length() * 0.1f;
                var pln = new Plane(boxPlane.Normal, proj + boxPlane.Normal * Math.Max(dist, 100)).ToPrecisionPlane();
                if (poly.Split(pln, out var b, out _)) poly = b;
            }

            var verts = new List<VertexStandard>();
            var indices = new List<int>();

            var clipPoly = poly.ToStandardPolygon();
            var colour = Color.FromArgb(64, Color.Turquoise).ToVector4();

            // Add the face in both directions so it renders on both sides
            var polies = new[] { clipPoly.Vertices.ToList(), clipPoly.Vertices.Reverse().ToList() };
            foreach (var p in polies)
            {
                var offs = verts.Count;
                verts.AddRange(p.Select(x => new VertexStandard
                {
                    Position = x,
                    Colour = Vector4.One,
                    Tint = colour,
                    Flags = VertexFlags.FlatColour
                }));

                for (var i = 2; i < clipPoly.Vertices.Count; i++)
                {
                    indices.Add(offs);
                    indices.Add(offs + i - 1);
                    indices.Add(offs + i);
                }
            }

            builder.Append(
                verts, indices.Select(x => (uint)x),
                new[] { new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, p1, 0, (uint)indices.Count) }
            );
        }

        private static void DrawPolygons2D(OrthographicCamera camera, I2DRenderer im, List<Polygon> polygons, Color colour, float width)
        {
            foreach (var polygon in polygons)
            {
                var count = polygon.Vertices.Count;
                for (var i = 0; i < count; i++)
                {
                    var a = camera.WorldToScreen(polygon.Vertices[i]);
                    var b = camera.WorldToScreen(polygon.Vertices[(i + 1) % count]);
                    im.AddLine(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y), colour, width, false);
                }
            }
        }

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            base.Render(document, viewport, camera, worldMin, worldMax, im);

            // Always show which mode is active
            im.AddText(new Vector2(8, 26), Color.White, FontType.Bold, GetSideDescription());

            if (_state != ClipState.None && _clipPlanePoint1 != null && _clipPlanePoint2 != null)
            {
                var p1 = _clipPlanePoint1.Value;
                var p2 = _clipPlanePoint2.Value;

                // Result preview: red = gets cut away, white = stays (drawn on top of the selection wireframe)
                if (TryGetClipPlane(out _) && !document.Selection.IsEmpty)
                {
                    GetPreview(document, out var kept, out var removed);
                    DrawPolygons2D(camera, im, removed, Color.Red, 1.5f);
                    DrawPolygons2D(camera, im, kept, Color.White, 1.5f);
                }

                // Clip line
                var s1 = camera.WorldToScreen(p1);
                var s2 = camera.WorldToScreen(p2);
                im.AddLine(new Vector2(s1.X, s1.Y), new Vector2(s2.X, s2.Y), Color.Yellow, 1, false);

                // Only two handles: the plane's third point is derived, not draggable
                foreach (var p in new[] { p1, p2 })
                {
                    const int size = 4;
                    var spos = camera.WorldToScreen(p);

                    im.AddRectOutlineOpaque(new Vector2(spos.X - size, spos.Y - size), new Vector2(spos.X + size, spos.Y + size), Color.Black, Color.White);
                }
            }
        }
    }
}
