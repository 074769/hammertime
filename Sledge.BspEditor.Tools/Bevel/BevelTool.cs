using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;

namespace Sledge.BspEditor.Tools.Bevel
{
    /// <summary>
    /// Bevel tool: pick edges of the selected solids, set a distance, press Bevel.
    ///
    /// Click an edge to select it (Ctrl+click adds or removes edges). Click a solid in the 3D view to select it first.
    /// The cut that will be made is previewed while edges are selected. Enter applies, Escape clears the edges.
    /// All the geometry (and the safeguards against invalid solids) is in <see cref="BevelGeometry"/>.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("PA")]
    public class BevelTool : BaseTool
    {
        public static readonly Capability BevelToolCapability = Capability.Create("BevelTool");

        /// <summary>Screen-space grab distance for an edge in the 3D view.</summary>
        private const float PickPixels3D = 7f;

        /// <summary>Screen-space grab distance for an edge in a 2D view.</summary>
        private const float PickPixels2D = 5f;

        private sealed class PickedEdge
        {
            public Solid Solid { get; }
            public Vector3 Start { get; }
            public Vector3 End { get; }

            public PickedEdge(Solid solid, Vector3 start, Vector3 end)
            {
                Solid = solid;
                Start = start;
                End = end;
            }

            public bool Is(PickedEdge other)
            {
                return ReferenceEquals(Solid, other.Solid)
                       && ((BevelGeometry.Same(Start, other.Start) && BevelGeometry.Same(End, other.End))
                           || (BevelGeometry.Same(Start, other.End) && BevelGeometry.Same(End, other.Start)));
            }
        }

        private sealed class SolidPreview
        {
            public Solid Solid { get; set; }
            public BevelGeometry.Result Result { get; set; }
        }

        private readonly object _lock = new object();
        private readonly List<PickedEdge> _edges = new List<PickedEdge>();
        private PickedEdge _hover;
        private List<SolidPreview> _preview = new List<SolidPreview>();
        private bool _previewDirty = true;
        private string _status;

        private float _distance = 16;
        private bool _clamp = true;

        /// <summary>How far to cut back along each face (units).</summary>
        public float Distance
        {
            get => _distance;
            set
            {
                lock (_lock)
                {
                    _distance = value;
                    _previewDirty = true;
                    _status = null;
                }
            }
        }

        /// <summary>Shrink the distance until the solid stays valid. On by default.</summary>
        public bool Clamp
        {
            get => _clamp;
            set
            {
                lock (_lock)
                {
                    _clamp = value;
                    _previewDirty = true;
                    _status = null;
                }
            }
        }

        public override Capability ToolCapability => BevelToolCapability;

        public BevelTool()
        {
            Usage = ToolUsage.Both;
        }

        public override Image GetIcon()
        {
            return IconLoader.Load(typeof(Resources), nameof(Resources.Tool_Bevel));
        }

        public override string GetName()
        {
            return "Bevel Tool";
        }

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<IDocument>("MapDocument:SelectionChanged", x =>
            {
                if (x == GetDocument()) Refresh();
            });
            // Undo, redo or another tool may have changed the solids: drop edges that are no longer there
            yield return Oy.Subscribe<Change>("MapDocument:Changed", c =>
            {
                if (c.Document == GetDocument()) Refresh();
            });
        }

        public override Task ToolSelected()
        {
            ClearEdges();
            return base.ToolSelected();
        }

        public override Task ToolDeselected()
        {
            ClearEdges();
            return base.ToolDeselected();
        }

        protected override void DocumentChanged()
        {
            ClearEdges();
        }

        #region State

        private static List<BevelGeometry.FaceInput> ToInputs(Solid solid)
        {
            return solid.Faces.Select(f => new BevelGeometry.FaceInput(f.Plane, f.Vertices.ToList())).ToList();
        }

        private IEnumerable<Solid> SelectedSolids(MapDocument document)
        {
            return document.Selection.OfType<Solid>().ToList();
        }

        private void ClearEdges()
        {
            lock (_lock)
            {
                _edges.Clear();
                _hover = null;
                _preview = new List<SolidPreview>();
                _previewDirty = true;
                _status = null;
            }
        }

        /// <summary>Drops edges whose solid is no longer selected or no longer has that edge, and rebuilds the preview.</summary>
        private void Refresh()
        {
            var document = GetDocument();
            lock (_lock)
            {
                if (document == null)
                {
                    _edges.Clear();
                }
                else
                {
                    var selected = new HashSet<Solid>(SelectedSolids(document));
                    var cache = new Dictionary<Solid, List<BevelGeometry.FaceInput>>();
                    _edges.RemoveAll(e =>
                    {
                        if (!selected.Contains(e.Solid)) return true;
                        if (!cache.TryGetValue(e.Solid, out var inputs)) cache[e.Solid] = inputs = ToInputs(e.Solid);
                        return !BevelGeometry.TryFindEdge(inputs, e.Start, e.End, out _);
                    });
                }

                _hover = null;
                _previewDirty = true;
                _status = null;
            }
        }

        private void EnsurePreview()
        {
            lock (_lock)
            {
                if (!_previewDirty) return;
                _previewDirty = false;

                var preview = new List<SolidPreview>();
                foreach (var group in _edges.GroupBy(e => e.Solid))
                {
                    var result = BevelGeometry.Bevel(ToInputs(group.Key), group.Select(e => (e.Start, e.End)).ToList(), _distance, _clamp);
                    preview.Add(new SolidPreview { Solid = group.Key, Result = result });
                }
                _preview = preview;
            }
        }

        /// <summary>One line for the sidebar: what is selected, what will happen, or why it can't.</summary>
        public string GetInfoText()
        {
            EnsurePreview();
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_status)) return _status;

                var document = GetDocument();
                if (document == null) return "";
                if (!SelectedSolids(document).Any()) return "Select a solid (click it in the 3D view), then click its edges.";
                if (_edges.Count == 0) return "Click an edge to select it. Ctrl+click adds or removes edges.";

                var text = _edges.Count + (_edges.Count == 1 ? " edge selected." : " edges selected.");
                var failed = _preview.FirstOrDefault(p => !p.Result.Success);
                if (failed != null) return text + " " + failed.Result.Message;

                var clamped = _preview.FirstOrDefault(p => p.Result.Clamped);
                if (clamped != null) text += " " + clamped.Result.Message;
                return text;
            }
        }

        #endregion

        #region Picking

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len = ab.LengthSquared();
            if (len < 1e-6f) return (p - a).Length();
            var t = Math.Max(0, Math.Min(1, Vector2.Dot(p - a, ab) / len));
            return (p - (a + ab * t)).Length();
        }

        /// <summary>Clips a segment to the front of the camera. False if all of it is behind.</summary>
        private static bool ClipToFront(PerspectiveCamera camera, ref Vector3 a, ref Vector3 b)
        {
            const float near = 1f;
            var da = Vector3.Dot(a - camera.Position, camera.Direction);
            var db = Vector3.Dot(b - camera.Position, camera.Direction);
            if (da < near && db < near) return false;
            if (da < near) a = a + (b - a) * ((near - da) / (db - da));
            else if (db < near) b = b + (a - b) * ((near - db) / (da - db));
            return true;
        }

        private static Vector2 ToVector2(Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        private List<PickedEdge> Pick3D(MapDocument document, PerspectiveCamera camera, int x, int y)
        {
            var mouse = new Vector2(x, y);
            PickedEdge best = null;
            var bestDistance = PickPixels3D;
            var bestDepth = float.MaxValue;

            foreach (var solid in SelectedSolids(document))
            {
                var inputs = ToInputs(solid);
                foreach (var edge in BevelGeometry.FindEdges(inputs))
                {
                    var mid = (edge.Start + edge.End) / 2;
                    var toEdge = mid - camera.Position;

                    // Only edges that touch a face turned towards the camera can be seen
                    var front = Vector3.Dot(inputs[edge.FaceA].Plane.Normal, toEdge) < 0
                                || Vector3.Dot(inputs[edge.FaceB].Plane.Normal, toEdge) < 0;
                    if (!front) continue;

                    var a = edge.Start;
                    var b = edge.End;
                    if (!ClipToFront(camera, ref a, ref b)) continue;

                    var d = DistanceToSegment(mouse, ToVector2(camera.WorldToScreen(a)), ToVector2(camera.WorldToScreen(b)));
                    var depth = toEdge.Length();
                    if (d < bestDistance - 0.5f || (d <= bestDistance + 0.5f && depth < bestDepth))
                    {
                        best = new PickedEdge(solid, edge.Start, edge.End);
                        bestDistance = Math.Min(d, bestDistance);
                        bestDepth = depth;
                    }
                }
            }

            return best == null ? new List<PickedEdge>() : new List<PickedEdge> { best };
        }

        /// <summary>In a 2D view edges overlap, so every edge under the cursor is picked together (like the vertex tool does with points).</summary>
        private List<PickedEdge> Pick2D(MapDocument document, OrthographicCamera camera, int x, int y)
        {
            var mouse = new Vector2(x, y);
            var found = new List<(PickedEdge Edge, float Distance)>();

            foreach (var solid in SelectedSolids(document))
            {
                foreach (var edge in BevelGeometry.FindEdges(ToInputs(solid)))
                {
                    var d = DistanceToSegment(mouse, ToVector2(camera.WorldToScreen(edge.Start)), ToVector2(camera.WorldToScreen(edge.End)));
                    if (d <= PickPixels2D) found.Add((new PickedEdge(solid, edge.Start, edge.End), d));
                }
            }

            if (found.Count == 0) return new List<PickedEdge>();
            var min = found.Min(f => f.Distance);
            return found.Where(f => f.Distance <= min + 0.75f).Select(f => f.Edge).ToList();
        }

        private void ApplyPick(List<PickedEdge> picks, bool add)
        {
            lock (_lock)
            {
                if (!add) _edges.Clear();

                foreach (var pick in picks)
                {
                    var existing = _edges.FirstOrDefault(e => e.Is(pick));
                    if (existing == null) _edges.Add(pick);
                    else if (add) _edges.Remove(existing);
                }

                _previewDirty = true;
                _status = null;
            }
        }

        #endregion

        #region Mouse and keys

        private void SelectSolid(MapDocument document, Solid solid)
        {
            if (solid == null) return;

            var operation = new Transaction();
            if (!KeyboardState.Ctrl)
            {
                operation.Add(new Deselect(document.Selection.Where(x => !ReferenceEquals(x, solid)).ToList()));
            }
            if (!solid.IsSelected) operation.Add(new Select(solid));
            if (!operation.IsEmpty) MapDocumentOperation.Perform(document, operation);
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            var picks = Pick3D(document, camera, e.X, e.Y);
            if (picks.Count > 0)
            {
                ApplyPick(picks, KeyboardState.Ctrl);
                e.Handled = true;
                return;
            }

            // Not on an edge: a click on a solid selects that solid, so its edges can be picked next
            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var closest = document.Map.Root.GetIntersectionsForVisibleObjects(new Line(start, end))
                .Where(x => x.Object is Solid)
                .Select(x => (Solid) x.Object)
                .FirstOrDefault();

            if (closest != null) SelectSolid(document, closest);
            else if (!KeyboardState.Ctrl) ApplyPick(new List<PickedEdge>(), false);
            e.Handled = true;
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            var picks = Pick3D(document, camera, e.X, e.Y);
            lock (_lock) _hover = picks.FirstOrDefault();
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            lock (_lock) _hover = null;
        }

        protected override void MouseClick(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            var picks = Pick2D(document, camera, e.X, e.Y);
            if (picks.Count == 0 && KeyboardState.Ctrl) return;
            ApplyPick(picks, KeyboardState.Ctrl);
            e.Handled = true;
        }

        private void HandleKey(ViewportEvent e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                var unused = Apply();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                ApplyPick(new List<PickedEdge>(), false);
                e.Handled = true;
            }
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e) => HandleKey(e);
        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e) => HandleKey(e);

        #endregion

        #region Apply

        public void ClearSelectedEdges()
        {
            ApplyPick(new List<PickedEdge>(), false);
        }

        /// <summary>Bevels the selected edges. All solids go into one transaction, so one undo reverses the lot. A solid that can't be bevelled safely is left exactly as it was.</summary>
        public async Task Apply()
        {
            var document = GetDocument();
            if (document == null) return;

            List<PickedEdge> edges;
            lock (_lock) edges = _edges.ToList();

            if (edges.Count == 0)
            {
                SetStatus("Select one or more edges first.");
                return;
            }

            var transaction = new Transaction();
            var done = 0;
            var clamped = false;
            var applied = 0f;
            var failures = new List<string>();

            foreach (var group in edges.GroupBy(e => e.Solid))
            {
                var solid = group.Key;
                var original = solid.Faces.ToList();
                var result = BevelGeometry.Bevel(ToInputs(solid), group.Select(e => (e.Start, e.End)).ToList(), Distance, Clamp);
                if (!result.Success)
                {
                    failures.Add(result.Message);
                    continue;
                }

                var faces = new List<Face>();
                foreach (var rf in result.Faces)
                {
                    var source = original[rf.IsNew ? rf.ParentFace : rf.SourceFace];
                    var face = new Face(document.Map.NumberGenerator.Next("Face"))
                    {
                        Texture = source.Texture.Clone(),
                        LightmapScale = source.LightmapScale
                    };
                    face.Vertices.AddRange(rf.Polygon.Vertices);
                    face.Plane = rf.Polygon.Plane;
                    if (rf.IsNew) face.Texture.AlignToNormal(face.Plane.Normal);
                    faces.Add(face);
                }

                transaction.Add(new RemoveMapObjectData(solid.ID, solid.Faces));
                transaction.Add(new AddMapObjectData(solid.ID, faces));

                done++;
                applied = result.Applied;
                clamped |= result.Clamped;
            }

            if (transaction.IsEmpty)
            {
                SetStatus("Nothing changed. " + failures.FirstOrDefault());
                return;
            }

            ClearEdges();
            await MapDocumentOperation.Perform(document, transaction);

            var text = "Bevelled " + done + (done == 1 ? " solid" : " solids") + " by "
                       + applied.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " units"
                       + (clamped ? " (clamped)." : ".");
            if (failures.Count > 0) text += " " + failures.Count + " skipped: " + failures[0];
            SetStatus(text);
        }

        private void SetStatus(string text)
        {
            lock (_lock) _status = text;
        }

        #endregion

        #region Rendering

        private static readonly Color EdgeColour = Color.OrangeRed;
        private static readonly Color HoverColour = Color.Yellow;
        private static readonly Color PreviewColour = Color.Cyan;

        private void Draw(PerspectiveCamera camera, OrthographicCamera ortho, I2DRenderer im)
        {
            EnsurePreview();

            List<PickedEdge> edges;
            List<SolidPreview> preview;
            PickedEdge hover;
            lock (_lock)
            {
                edges = _edges.ToList();
                preview = _preview.ToList();
                hover = _hover;
            }

            void Line(Vector3 a, Vector3 b, Color colour, float width)
            {
                if (camera != null)
                {
                    if (!ClipToFront(camera, ref a, ref b)) return;
                    var sa = camera.WorldToScreen(a);
                    var sb = camera.WorldToScreen(b);
                    im.AddLine(new Vector2(sa.X, sa.Y), new Vector2(sb.X, sb.Y), colour, width, false);
                }
                else
                {
                    var sa = ortho.WorldToScreen(a);
                    var sb = ortho.WorldToScreen(b);
                    im.AddLine(new Vector2(sa.X, sa.Y), new Vector2(sb.X, sb.Y), colour, width, false);
                }
            }

            // What the bevel will cut: the new faces
            foreach (var p in preview.Where(x => x.Result.Success))
            {
                foreach (var face in p.Result.Faces.Where(f => f.IsNew))
                {
                    var verts = face.Polygon.Vertices;
                    for (var i = 0; i < verts.Count; i++) Line(verts[i], verts[(i + 1) % verts.Count], PreviewColour, 1f);
                }
            }

            // Edges that can't be bevelled at the moment are shown dimmer
            foreach (var e in edges)
            {
                var ok = preview.Any(p => ReferenceEquals(p.Solid, e.Solid) && p.Result.Success);
                Line(e.Start, e.End, ok ? EdgeColour : Color.Gray, 3f);
            }

            if (hover != null && !edges.Any(e => e.Is(hover))) Line(hover.Start, hover.End, HoverColour, 2f);
        }

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            base.Render(document, viewport, camera, worldMin, worldMax, im);
            Draw(null, camera, im);
        }

        protected override void Render(MapDocument document, IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            base.Render(document, viewport, camera, im);
            Draw(camera, null, im);
        }

        #endregion
    }
}
