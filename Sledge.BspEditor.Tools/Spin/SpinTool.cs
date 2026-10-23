using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
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

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Spin tool: builds an arch, ring or bent pipe by sweeping an extruded profile around an axis.
    ///
    /// Start and end pieces (the normal way): select the piece the arch starts with and press Set start,
    /// select the piece it ends with and press Set end, then press Spin. Each piece is an extruded
    /// profile (a solid or a group of solids that is, say, 72 units thick). The axis is square to both
    /// pieces, the pivot is where their outer end caps (the caps facing away from each other) meet, and
    /// the arch runs from the start piece's outer cap to the end piece's outer cap, replacing both
    /// pieces. The number of pieces follows the start piece's thickness, or is set by hand.
    ///
    /// Manual (no start or end set): the selection is swept around the rotation pivot by Angle, in
    /// the 2D view's axis.
    ///
    /// Clicking selects objects (like the Select tool: groups and entities are selected whole, Ctrl
    /// adds or removes), so the pieces can be picked without leaving this tool.
    ///
    /// Either way every piece keeps the profile exactly: seam vertices lie on the profile's own circles,
    /// the first and last seam are the profile itself (so straight pieces that continue it join exactly),
    /// and neighbouring pieces share their seam faces exactly, which is what the map compiler needs.
    ///
    /// Textures: the first piece keeps the mapping of the original faces. Every second piece is its
    /// neighbour's mapping reflected across the seam between them, which makes the texture continuous
    /// across every seam (and the first piece continue the straight piece before it).
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("M")]
    public class SpinTool : BaseTool
    {
        public static readonly Capability SpinToolCapability = Capability.Create("SpinTool");

        /// <summary>Largest number of pieces a spin can make.</summary>
        public const int MaxSteps = 64;

        /// <summary>A piece can't span this many degrees or more.</summary>
        private const float MaxPieceDegrees = 170f;

        /// <summary>Mouse movement (in pixels) before a press counts as a drag.</summary>
        private const float DragThresholdPixels = 4f;

        /// <summary>Screen-space grab radius for the pivot (gizmo) handle.</summary>
        private const int PivotDragSize = 8;

        /// <summary>Number of pieces when Auto steps is off.</summary>
        public int Steps { get; set; } = 8;

        /// <summary>Total sweep in degrees for a manual spin.</summary>
        public float AngleDegrees { get; set; } = 90f;

        /// <summary>When true, every second piece gets its texture mapping mirrored so the pattern tiles seamlessly.</summary>
        public bool MirrorUvs { get; set; } = true;

        /// <summary>
        /// When true, the faces of each piece are unfolded into one continuous, undistorted layout (as the UV tool's
        /// Unfold does). Independent of <see cref="MirrorUvs"/>: the mirroring of every second piece stays.
        /// </summary>
        public bool UnfoldUvs { get; set; }

        /// <summary>With a start and an end piece: take the number of pieces from the start piece's thickness.</summary>
        public bool AutoSteps { get; set; } = true;

        /// <summary>Manual mode only: drag in a 2D view to set the angle and move the pivot. Off by default.</summary>
        public bool DragRotate { get; set; }

        // The objects (as selected: solids, groups or entities) that were set as the start and end piece
        private List<long> _startIds = new List<long>();
        private List<long> _endIds = new List<long>();

        // Drag state (manual mode)
        private bool _pressed;
        private bool _dragging;
        private bool _movingPivot;
        private int _downX;
        private int _downY;
        private Vector3 _dragPivot;
        private float _lastAngle;
        private float _rawSweep;

        /// <summary>
        /// Key of the selection the current pivot belongs to. A selection change
        /// made outside a spin clears the pivot; the spin's own result keeps it.
        /// </summary>
        private string _pivotKey;

        // The 2D view the tool was last used in; manual spins turn around its axis
        private OrthographicCamera.OrthographicType _viewType = OrthographicCamera.OrthographicType.Top;

        public SpinTool()
        {
            Usage = ToolUsage.Both;
        }

        public override Image GetIcon()
        {
            return IconLoader.Load(typeof(Resources), nameof(Resources.Tool_Spin));
        }

        public override string GetName()
        {
            return "Spin Tool";
        }

        public override Capability ToolCapability => SpinToolCapability;

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<IDocument>("MapDocument:SelectionChanged", x =>
            {
                var document = GetDocument();
                if (document == null || x != document) return;

                var key = SelectionKey(document.Selection.Select(s => s.ID));
                if (key == _pivotKey) return;

                // The selection changed from outside a spin, so the old pivot no longer applies
                _pivotKey = key;
                if (!HasPieces) document.Selection.RotationPivot = null;
            });
        }

        #region Start and end piece

        public bool HasPieces => _startIds.Count > 0 || _endIds.Count > 0;

        /// <summary>The current selection becomes the start piece.</summary>
        public void SetStartFromSelection()
        {
            var ids = SelectionAsPiece();
            if (ids.Count > 0) _startIds = ids;
        }

        /// <summary>The current selection becomes the end piece.</summary>
        public void SetEndFromSelection()
        {
            var ids = SelectionAsPiece();
            if (ids.Count > 0) _endIds = ids;
        }

        /// <summary>Forget the start and end pieces (back to a manual spin).</summary>
        public void ClearPieces()
        {
            _startIds = new List<long>();
            _endIds = new List<long>();
        }

        private List<long> SelectionAsPiece()
        {
            var document = GetDocument();
            if (document == null) return new List<long>();

            // The top-level selected objects, like Delete uses: a selected entity or group counts as one object
            return document.Selection.GetSelectedParents().Where(x => x.FindAll().OfType<Solid>().Any()).Select(x => x.ID).ToList();
        }

        private static List<IMapObject> Resolve(MapDocument document, List<long> ids)
        {
            return ids.Select(x => document.Map.Root.FindByID(x)).Where(x => x != null && x.Hierarchy.Parent != null).ToList();
        }

        private static List<Solid> SolidsOf(IEnumerable<IMapObject> objects)
        {
            return objects.SelectMany(x => x.FindAll()).OfType<Solid>().Where(x => x.Hierarchy.Parent != null).Distinct().ToList();
        }

        private static List<SpinGeometry.PieceFace> FacesOf(IEnumerable<Solid> solids)
        {
            return solids.SelectMany(s => s.Faces).Select(f => new SpinGeometry.PieceFace(f.Plane.Normal, f.Vertices.ToList())).ToList();
        }

        #endregion

        #region Viewport interaction (manual mode only)

        private bool ManualDragActive => DragRotate && !HasPieces;

        protected override void MouseDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            _viewType = camera.ViewType;

            // Unless the manual drag is on, a click selects, so the start and end piece can be picked here
            if (!ManualDragActive)
            {
                if (e.Button == MouseButtons.Left) ClickSelect(document, PickIn2D(document, camera, e));
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            if (!document.Selection.OfType<Solid>().Any()) return;

            var pivot = ManualPivot(document);

            // Dragging the pivot marker moves the gizmo itself
            var screenPivot = camera.WorldToScreen(pivot);
            if (Math.Abs(screenPivot.X - e.X) <= PivotDragSize && Math.Abs(screenPivot.Y - e.Y) <= PivotDragSize)
            {
                _movingPivot = true;
                _pressed = false;
                _dragging = false;
                _dragPivot = pivot;
                return;
            }

            // Clicks too close to the pivot can't define an angle
            var point = camera.ScreenToWorld(e.X, e.Y);
            if ((point - pivot).Length() < 1f) return;

            var frame = FrameFromView(camera.ViewType);
            _pressed = true;
            _dragging = false;
            _downX = e.X;
            _downY = e.Y;
            _dragPivot = pivot;
            _lastAngle = AngleAt(point, pivot, frame.U, frame.V);
            _rawSweep = 0f;
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            _viewType = camera.ViewType;
            if (!ManualDragActive) return;

            if (_movingPivot)
            {
                // Move the gizmo pivot, keeping its depth (the axis the current
                // view looks down) and snapping to the grid
                var point = camera.GetUnusedCoordinate(ManualPivot(document)) + SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
                document.Selection.RotationPivot = point;
                _dragPivot = point;
                return;
            }

            if (!_pressed) return;

            if (!_dragging)
            {
                var dx = e.X - _downX;
                var dy = e.Y - _downY;
                if (dx * dx + dy * dy < DragThresholdPixels * DragThresholdPixels) return;
                _dragging = true;
            }

            // Accumulate the angle step by step, so a drag can go past 180 degrees
            var frame = FrameFromView(camera.ViewType);
            var angle = AngleAt(camera.ScreenToWorld(e.X, e.Y), _dragPivot, frame.U, frame.V);
            _rawSweep += NormalizeDegrees(angle - _lastAngle);
            _lastAngle = angle;
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (_movingPivot)
            {
                // The pivot is already stored on the selection while dragging
                _movingPivot = false;
                return;
            }

            if (!_pressed) return;

            var dragged = _dragging;
            var sweep = CurrentDragSweep();
            _pressed = false;
            _dragging = false;

            // A plain click does nothing; only a real drag spins
            if (e.Button != MouseButtons.Left || !dragged || Math.Abs(sweep) < 1f) return;

            // Keep the panel in sync with the sweep that is actually applied
            AngleDegrees = sweep;
            Oy.Publish("SpinTool:SetAngle", sweep.ToString("0.#", CultureInfo.InvariantCulture));

            PerformSpin(document);
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left || ManualDragActive) return;
            ClickSelect(document, PickIn3D(document, camera, e));
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            HandleKey(document, e);
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            HandleKey(document, e);
        }

        private void HandleKey(MapDocument document, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                _pressed = false;
                _dragging = false;
                _movingPivot = false;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                PerformSpin(document);
            }
        }

        #endregion

        #region Selecting (click to select, like the Select tool)

        /// <summary>The object under the cursor in a 2D view: its edges or its centre handle are within a few pixels.</summary>
        private static IMapObject PickIn2D(MapDocument document, OrthographicCamera camera, ViewportEvent e)
        {
            // A box around the click, a few pixels wide on screen and unlimited in depth
            var unused = camera.GetUnusedCoordinate(new Vector3(100000, 100000, 100000));
            var tolerance = 4 / camera.Zoom;
            var used = camera.Expand(new Vector3(tolerance, tolerance, 0));
            var add = used + unused;
            var click = camera.ScreenToWorld(e.X, e.Y);
            var box = new Box(click - add, click + add);

            return document.Map.Root.Collect(
                x => x is Root || (x.BoundingBox != null && x.BoundingBox.IntersectsWith(box)),
                x => x.Hierarchy.Parent != null
                     && !x.Hierarchy.HasChildren
                     && ((x.BoundingBox != null && box.Vector3IsInside(x.BoundingBox.Center)) || x.GetPolygons().Any(p => p.GetLines().Any(box.IntersectsWith)))
            ).FirstOrDefault();
        }

        /// <summary>The nearest object under the cursor in the 3D view.</summary>
        private static IMapObject PickIn3D(MapDocument document, PerspectiveCamera camera, ViewportEvent e)
        {
            var flags = document.Map.Data.GetOne<DisplayFlags>() ?? new DisplayFlags();
            var ignore = (flags.HideClipTextures ? MapObjectExtensions.IgnoreOptions.IgnoreClip : MapObjectExtensions.IgnoreOptions.None)
                         | (flags.HideNullTextures ? MapObjectExtensions.IgnoreOptions.IgnoreNull : MapObjectExtensions.IgnoreOptions.None);

            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            return document.Map.Root.GetIntersectionsForVisibleObjects(new Line(start, end), ignore).Select(x => x.Object).FirstOrDefault();
        }

        /// <summary>
        /// Select what was clicked (nothing: select nothing). With Ctrl held the object is added, or removed if
        /// it is already selected, and the rest of the selection stays.
        /// </summary>
        private static void ClickSelect(MapDocument document, IMapObject hit)
        {
            var ctrl = KeyboardState.Ctrl;
            var ignoreGrouping = document.Map.Data.GetOne<SelectionOptions>()?.IgnoreGrouping == true;

            var deselect = new List<IMapObject>();
            var select = new List<IMapObject>();
            if (hit != null)
            {
                if (!ctrl || !hit.IsSelected) select.Add(hit);
                else deselect.Add(hit);
            }

            SetSelected(document, deselect, select, !ctrl, ignoreGrouping);
        }

        /// <summary>A selected entity or group stands for everything in it, unless grouping is ignored.</summary>
        private static IEnumerable<IMapObject> NormaliseSelection(IEnumerable<IMapObject> objects, bool ignoreGrouping)
        {
            return ignoreGrouping
                ? objects
                : objects.Select(x => x.FindTopmostParent(y => y is Group || y is Primitives.MapObjects.Entity) ?? x).Distinct().SelectMany(x => x.FindAll());
        }

        private static void SetSelected(MapDocument document, IEnumerable<IMapObject> toDeselect, IEnumerable<IMapObject> toSelect, bool deselectAll, bool ignoreGrouping)
        {
            if (deselectAll) toDeselect = document.Selection.ToList();

            var deselected = NormaliseSelection(toDeselect.Where(x => x != null), ignoreGrouping).ToList();
            var selected = NormaliseSelection(toSelect.Where(x => x != null), ignoreGrouping).ToList();

            // No need to deselect what is about to be selected
            deselected = deselected.Where(x => !selected.Contains(x)).ToList();

            // Nothing changes: leave the undo history alone
            if (deselected.Count == 0 && !selected.Except(document.Selection).Any()) return;

            var transaction = new Transaction(new Select(selected), new Deselect(deselected));
            ((Action) (async () => await MapDocumentOperation.Perform(document, transaction)))();
        }

        #endregion

        #region Spin operation

        /// <summary>
        /// Called by the sidebar panel's Spin button.
        /// </summary>
        public void ApplySpin()
        {
            var document = GetDocument();
            if (document == null) return;
            PerformSpin(document);
        }

        /// <summary>
        /// Text for the sidebar: what the spin will produce, or why it can't.
        /// </summary>
        public string GetInfoText()
        {
            var document = GetDocument();
            if (document == null) return "";

            var plan = MakePlan(document);
            if (!plan.Valid) return plan.Message;

            if (plan.Pieces)
            {
                var text = $"Start: {plan.SourceSolids.Count} solids, end: {plan.EndObjects.Count} object(s). "
                           + $"{plan.Steps} pieces of {Math.Abs(plan.Delta):0.##} deg, {Math.Abs(plan.Sweep):0.##} deg in total.";
                if (AutoSteps) text += $" (A piece of the start piece's thickness covers {plan.PieceAngle:0.##} deg.)";
                return text;
            }

            return $"Manual spin around the {AxisName(_viewType)} axis. {plan.Steps} pieces of {Math.Abs(plan.Delta):0.##} deg, {Math.Abs(plan.Sweep):0.##} deg from the pivot line.";
        }

        private void PerformSpin(MapDocument document)
        {
            var plan = MakePlan(document);
            if (!plan.Valid)
            {
                if (!string.IsNullOrEmpty(plan.Message))
                {
                    MessageBox.Show(plan.Message, "Spin Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            if (Math.Abs(plan.Delta) >= MaxPieceDegrees)
            {
                MessageBox.Show(
                    $"Each piece would span {Math.Abs(plan.Delta):0.#} degrees. Use more steps so a piece is under {MaxPieceDegrees:0} degrees.",
                    "Spin Tool",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var generator = document.Map.NumberGenerator;
            var transaction = new Transaction();
            var removed = new List<long>();
            var added = new List<long>();
            var failed = 0;
            var spun = new List<SolidSpin>();

            foreach (var solid in plan.SourceSolids)
            {
                // Each solid is all or nothing: if any of its pieces can't be made, it stays as it was
                var result = SpinSolid(solid, plan, MirrorUvs, generator);
                if (result == null)
                {
                    failed++;
                    continue;
                }

                spun.Add(result);
                transaction.Add(result.Operations);
                removed.AddRange(result.Removed);
                added.AddRange(result.Added);
            }

            // Nothing may change if a start solid couldn't be spun: the arch would have holes
            if (plan.Pieces && failed > 0)
            {
                MessageBox.Show(
                    $"{failed} solid(s) of the start piece could not be spun, so nothing was changed. "
                    + "Check them for faces that are broken or flat.",
                    "Spin Tool",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // The unfold needs every solid of a step, so it runs once all of them are spun
            if (UnfoldUvs) UnfoldSteps(spun, plan.Steps);

            if (plan.Pieces)
            {
                // The end piece is replaced by the arch too: remove it together with its container (as Delete does)
                foreach (var group in plan.EndObjects.GroupBy(x => x.Hierarchy.Parent.ID))
                {
                    transaction.Add(new Detatch(group.Key, group));
                }
            }

            if (!transaction.IsEmpty)
            {
                // The selection after the spin is everything still selected, minus the
                // replaced originals, plus the new pieces. Record it so the pivot survives this spin.
                var after = document.Selection.Select(x => x.ID).Except(removed).Concat(added);
                _pivotKey = SelectionKey(after);
                MapDocumentOperation.Perform(document, transaction);
            }

            if (plan.Pieces) ClearPieces();

            if (!plan.Pieces && failed > 0)
            {
                MessageBox.Show(
                    $"{failed} solid(s) could not be spun and were left unchanged. Check them for faces that are broken or flat.",
                    "Spin Tool",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// Unfolds the texture of each step of the spin: the faces of that step's pieces (one per spun solid) that share
        /// an edge and a texture are laid out as one continuous, undistorted surface, as the UV tool's Unfold does.
        /// Each connected group keeps the mapping (and the mirroring) of its least distorted face, so the flipped
        /// steps stay flipped. Faces with the NULL texture are left alone.
        /// </summary>
        private static void UnfoldSteps(List<SolidSpin> spun, int steps)
        {
            for (var step = 0; step < steps; step++)
            {
                var remaining = spun
                    .Where(x => step < x.Pieces.Count)
                    .SelectMany(x => x.Pieces[step].Faces)
                    .Where(x => !string.IsNullOrEmpty(x.Texture.Name) && !string.Equals(x.Texture.Name, "NULL", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Unfold only reaches the faces connected to its reference face, so repeat for what is left
                while (remaining.Count > 0)
                {
                    var unfolded = UvMath.Unfold(remaining);
                    var left = remaining.Where(x => !unfolded.ContainsKey(x.ID)).ToList();
                    if (left.Count == remaining.Count) break;

                    foreach (var face in remaining)
                    {
                        if (unfolded.TryGetValue(face.ID, out var texture)) face.Texture.Unclone(texture);
                    }
                    remaining = left;
                }
            }
        }

        private sealed class SolidSpin
        {
            public readonly Transaction Operations = new Transaction();
            public readonly List<long> Removed = new List<long>();
            public readonly List<long> Added = new List<long>();

            /// <summary>The swept pieces of this solid, one per step, in order.</summary>
            public readonly List<Solid> Pieces = new List<Solid>();
        }

        /// <summary>
        /// Builds the operations that replace one solid by its swept pieces. Returns null if the solid
        /// can't be swept without changing its shape; nothing is changed then.
        /// </summary>
        private static SolidSpin SpinSolid(Solid solid, Plan plan, bool mirror, UniqueNumberGenerator generator)
        {
            // Extruded solids are lofted from their cross-section; anything else (sloped, triangulated
            // pieces) is swept as the hull of the solid at each step
            return SpinExtruded(solid, plan, mirror, generator) ?? SpinHull(solid, plan, generator);
        }

        /// <summary>
        /// Sweeps a solid that isn't an extruded profile: piece j is the convex hull of the solid turned to step j
        /// and to step j + 1. The first piece starts from the solid itself, and in the start/end mode the last copy
        /// stops where the arch ends. Neighbouring pieces overlap by one copy of the solid, so there are no gaps.
        /// </summary>
        private static SolidSpin SpinHull(Solid solid, Plan plan, UniqueNumberGenerator generator)
        {
            try
            {
                var parent = solid.Hierarchy.Parent.ID;
                var faces = solid.Faces.ToList();
                if (faces.Count < 4) return null;

                var frame = plan.Frame;
                var vertices = faces.SelectMany(x => x.Vertices).ToList();

                // How far (degrees, in the sweep direction) the solid reaches past the start line
                var direction = Math.Sign(plan.Delta);
                if (direction == 0) return null;
                var total = plan.Sweep;
                if (plan.Pieces)
                {
                    var reach = 0f;
                    foreach (var vertex in vertices)
                    {
                        var offset = SpinGeometry.AngleOffset(vertex, plan.Pivot, frame.Axis, frame.U, frame.V, plan.A0);
                        if (offset.HasValue) reach = Math.Max(reach, offset.Value * direction);
                    }
                    total = plan.Sweep - reach * direction;
                    if (Math.Abs(total) < 0.01f || Math.Sign(total) != direction) return null;
                }

                var steps = plan.Steps;
                var rotations = Enumerable.Range(0, steps + 1)
                    .Select(k => SpinGeometry.Rotation(plan.Pivot, frame.Axis, total * k / steps))
                    .ToList();

                var pieces = new List<Solid>();
                for (var j = 0; j < steps; j++)
                {
                    var points = vertices.Select(x => Vector3.Transform(x, rotations[j]))
                        .Concat(vertices.Select(x => Vector3.Transform(x, rotations[j + 1])))
                        .ToList();
                    var outlines = SpinGeometry.ConvexHullFaces(points);
                    if (outlines == null) return null;

                    var piece = new Solid(generator.Next("MapObject")) { IsSelected = true };
                    foreach (var outline in outlines)
                    {
                        var normal = SpinGeometry.FaceNormal(outline);
                        var face = new Face(generator.Next("Face"));
                        face.Vertices.AddRange(outline);

                        // The texture of the original face that points the same way, turned to this step
                        var best = -1f;
                        Face source = null;
                        Matrix4x4 sourceRotation = Matrix4x4.Identity;
                        foreach (var step in new[] { j, j + 1 })
                        {
                            foreach (var original in faces)
                            {
                                var dot = Vector3.Dot(Vector3.TransformNormal(original.Plane.Normal, rotations[step]), normal);
                                if (dot > best + 1e-4f)
                                {
                                    best = dot;
                                    source = original;
                                    sourceRotation = rotations[step];
                                }
                            }
                        }

                        face.Texture = source.Texture.Clone();
                        face.Texture.TransformUniform(sourceRotation);
                        if (best < 0.9999f) face.Texture.AlignToNormal(normal);
                        piece.Data.Add(face);
                    }

                    foreach (var data in solid.Data.Where(x => !(x is Face)))
                    {
                        piece.Data.Add((IMapObjectData) data.Clone());
                    }
                    pieces.Add(piece);
                }

                var result = new SolidSpin();
                result.Operations.Add(new Detatch(parent, solid));
                result.Removed.Add(solid.ID);
                result.Pieces.AddRange(pieces);
                foreach (var piece in pieces)
                {
                    result.Operations.Add(new Attach(parent, piece));
                    result.Added.Add(piece.ID);
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static SolidSpin SpinExtruded(Solid solid, Plan plan, bool mirror, UniqueNumberGenerator generator)
        {
            try
            {
                var parent = solid.Hierarchy.Parent.ID;
                var faces = solid.Faces.ToList();
                if (faces.Count == 0) return null;

                var pieceFaces = faces.Select(x => new SpinGeometry.PieceFace(x.Plane.Normal, x.Vertices.ToList())).ToList();

                // The extrusion direction of this solid: its end caps are square to it
                var tangent = SpinGeometry.Tangent(plan.Frame.Axis, plan.Frame.U, plan.Frame.V, plan.A0);
                var caps = faces.Where((x, i) => SpinGeometry.Classify(pieceFaces[i].Normal, tangent) == SpinGeometry.FaceKind.Cap).ToList();
                if (caps.Count == 0) return null;
                if (faces.Where((x, i) => SpinGeometry.Classify(pieceFaces[i].Normal, tangent) == SpinGeometry.FaceKind.Other).Any()) return null;

                // The cross-section, from an end cap, and which original face each of its edges belongs to
                var capVertices = caps[0].Vertices.ToList();
                if (!SpinGeometry.TryGetProfile(capVertices, plan.Pivot, plan.Frame.Axis, plan.Frame.U, plan.Frame.V, plan.A0, out var profile)) return null;
                var edgeFaces = SpinGeometry.FindEdgeFaces(capVertices, pieceFaces, tangent);
                if (edgeFaces.Any(x => x < 0)) return null;

                // Build every piece first, so a failure leaves the solid untouched
                var pieces = new List<Solid>();
                for (var j = 0; j < plan.Steps; j++)
                {
                    var outlines = SpinGeometry.BuildLoft(profile, plan.Pivot, plan.Frame.Axis, plan.Frame.U, plan.Frame.V, plan.A0, plan.Delta, j);
                    if (outlines == null) return null;

                    // The texture mapping of this piece: the first piece keeps the original mapping, and every
                    // next piece is the previous piece's mapping reflected across the seam between them. That makes
                    // the texture continuous across every seam, and the even pieces plain rotations of the first.
                    var flip = mirror && j % 2 == 1;
                    var turns = flip ? j - 1 : j;
                    var rotation = SpinGeometry.Rotation(plan.Pivot, plan.Frame.Axis, plan.Delta * turns);
                    var reflection = SpinGeometry.Reflection(SpinGeometry.Tangent(plan.Frame.Axis, plan.Frame.U, plan.Frame.V, plan.A0 + plan.Delta * j), plan.Pivot);

                    var piece = new Solid(generator.Next("MapObject")) { IsSelected = true };
                    for (var k = 0; k < outlines.Count; k++)
                    {
                        var outline = outlines[k];
                        var face = new Face(generator.Next("Face"));
                        face.Vertices.AddRange(outline);

                        if (k >= 2)
                        {
                            // A side face: the texture of the original face that holds this profile edge
                            face.Texture = faces[edgeFaces[k - 2]].Texture.Clone();
                            face.Texture.TransformUniform(rotation);
                        }
                        else
                        {
                            // A seam: the texture of the end cap that points the same way, aligned to the seam
                            var normal = SpinGeometry.FaceNormal(outline);
                            var source = caps
                                .OrderByDescending(x => Vector3.Dot(Vector3.TransformNormal(x.Plane.Normal, rotation), normal))
                                .First();
                            face.Texture = source.Texture.Clone();
                            face.Texture.TransformUniform(rotation);
                            face.Texture.AlignToNormal(normal);
                        }

                        if (flip) face.Texture.TransformUniform(reflection);

                        piece.Data.Add(face);
                    }

                    // Extra data (visgroups, colour, etc)
                    foreach (var data in solid.Data.Where(x => !(x is Face)))
                    {
                        piece.Data.Add((IMapObjectData) data.Clone());
                    }

                    pieces.Add(piece);
                }

                var result = new SolidSpin();
                result.Operations.Add(new Detatch(parent, solid));
                result.Removed.Add(solid.ID);
                result.Pieces.AddRange(pieces);
                foreach (var piece in pieces)
                {
                    result.Operations.Add(new Attach(parent, piece));
                    result.Added.Add(piece.ID);
                }
                return result;
            }
            catch (Exception)
            {
                // A degenerate solid must not corrupt the spin; the caller reports it
                return null;
            }
        }

        #endregion

        #region Plan

        /// <summary>
        /// How a spin is laid out. The preview and the result both use this, so they always match.
        /// </summary>
        private sealed class Plan
        {
            public bool Valid;
            public string Message = "";

            /// <summary>True when worked out from a start and an end piece, false for a manual spin.</summary>
            public bool Pieces;

            public Vector3 Pivot;
            public SpinFrame Frame;
            public float A0;       // degrees, the radial line the spin starts on (where the profile lies)
            public int Steps;
            public float Delta;    // degrees per step, signed
            public float PieceAngle;
            public float DrawRadius;

            public List<Solid> SourceSolids = new List<Solid>();
            public List<IMapObject> EndObjects = new List<IMapObject>();

            public float Sweep => Delta * Steps;
            public float Start => A0;
        }

        private Plan MakePlan(MapDocument document)
        {
            return HasPieces ? MakePiecePlan(document) : MakeManualPlan(document);
        }

        private Plan MakePiecePlan(MapDocument document)
        {
            var plan = new Plan { Pieces = true };

            if (_startIds.Count == 0) { plan.Message = "Select the start piece and press Set start."; return plan; }
            if (_endIds.Count == 0) { plan.Message = "Select the end piece and press Set end."; return plan; }

            var startObjects = Resolve(document, _startIds);
            var endObjects = Resolve(document, _endIds);
            if (startObjects.Count == 0 || endObjects.Count == 0)
            {
                plan.Message = "The start or the end piece no longer exists. Set it again.";
                return plan;
            }

            var startSolids = SolidsOf(startObjects);
            var endSolids = SolidsOf(endObjects);
            if (startSolids.Count == 0 || endSolids.Count == 0) { plan.Message = "The start and end pieces need solids."; return plan; }
            if (startSolids.Intersect(endSolids).Any()) { plan.Message = "The start and end pieces share solids. Pick two different pieces."; return plan; }

            if (!SpinGeometry.TryDefineArch(FacesOf(startSolids), FacesOf(endSolids), MaxSteps, out var arch, out var error))
            {
                plan.Message = error;
                return plan;
            }

            plan.Valid = true;
            plan.Pivot = arch.Pivot;
            plan.Frame = new SpinFrame { U = arch.U, V = arch.V, Axis = arch.Axis };
            plan.A0 = 0f;
            plan.Steps = AutoSteps ? arch.Steps : Math.Max(1, Math.Min(MaxSteps, Steps));
            plan.Delta = arch.Sweep / plan.Steps;
            plan.PieceAngle = arch.PieceAngle;
            plan.SourceSolids = startSolids;
            plan.EndObjects = endObjects;
            plan.DrawRadius = DrawRadiusOf(startSolids.Concat(endSolids), arch.Pivot, arch.Axis);
            return plan;
        }

        private Plan MakeManualPlan(MapDocument document)
        {
            var plan = new Plan { Pieces = false };
            var frame = FrameFromView(_viewType);
            var solids = SelectedSolids(document);
            if (solids.Count == 0) { plan.Message = $"Axis: {AxisName(_viewType)}. Select solids to spin, or set a start and an end piece."; return plan; }

            var pivot = ManualPivot(document);
            var normals = new List<Vector3>();
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var solid in solids)
            {
                foreach (var face in solid.Faces)
                {
                    normals.Add(face.Plane.Normal);
                    foreach (var vertex in face.Vertices)
                    {
                        min = Vector3.Min(min, vertex);
                        max = Vector3.Max(max, vertex);
                    }
                }
            }

            var angle = 0f;
            if (normals.Count == 0
                || !SpinGeometry.TryFindProfileAngle(normals, frame.Axis, frame.U, frame.V, (min + max) / 2f - pivot, out angle))
            {
                plan.Message = $"Axis: {AxisName(_viewType)}. Move the rotation pivot away from the selection; the solids must be extruded along one direction.";
                return plan;
            }

            plan.Valid = true;
            plan.Pivot = pivot;
            plan.Frame = frame;
            plan.A0 = angle;
            plan.Steps = Math.Max(1, Math.Min(MaxSteps, Steps));
            plan.Delta = (_dragging ? CurrentDragSweep() : AngleDegrees) / plan.Steps;
            plan.SourceSolids = solids;
            plan.DrawRadius = DrawRadiusOf(solids, pivot, frame.Axis);
            return plan;
        }

        private static float DrawRadiusOf(IEnumerable<Solid> solids, Vector3 pivot, Vector3 axis)
        {
            var radius = 64f;
            foreach (var solid in solids)
            {
                foreach (var face in solid.Faces)
                {
                    foreach (var vertex in face.Vertices)
                    {
                        var relative = vertex - pivot;
                        var planar = relative - axis * Vector3.Dot(relative, axis);
                        radius = Math.Max(radius, planar.Length());
                    }
                }
            }
            return radius;
        }

        #endregion

        #region Spin frame helpers

        private struct SpinFrame
        {
            public Vector3 U;
            public Vector3 V;
            public Vector3 Axis;
        }

        /// <summary>
        /// The spin plane for a 2D view. U, V and Axis form a right-handed frame,
        /// so positive angles turn counter-clockwise when seen from the Axis direction.
        /// </summary>
        private static SpinFrame FrameFromView(OrthographicCamera.OrthographicType view)
        {
            switch (view)
            {
                case OrthographicCamera.OrthographicType.Front:
                    return new SpinFrame { Axis = Vector3.UnitX, U = Vector3.UnitY, V = Vector3.UnitZ };
                case OrthographicCamera.OrthographicType.Side:
                    // Negated so (U, V, Axis) stays right-handed in this view
                    return new SpinFrame { Axis = -Vector3.UnitY, U = Vector3.UnitX, V = Vector3.UnitZ };
                default:
                    return new SpinFrame { Axis = Vector3.UnitZ, U = Vector3.UnitX, V = Vector3.UnitY };
            }
        }

        private static string AxisName(OrthographicCamera.OrthographicType view)
        {
            switch (view)
            {
                case OrthographicCamera.OrthographicType.Front: return "X";
                case OrthographicCamera.OrthographicType.Side: return "-Y";
                default: return "Z";
            }
        }

        private static List<Solid> SelectedSolids(MapDocument document)
        {
            return document.Selection.OfType<Solid>().Where(x => x.Hierarchy.Parent != null).ToList();
        }

        private static string SelectionKey(IEnumerable<long> ids)
        {
            return string.Join(",", ids.OrderBy(x => x));
        }

        private float CurrentDragSweep()
        {
            // Shift snaps the sweep to 45 degree increments
            return KeyboardState.Shift ? (float) Math.Round(_rawSweep / 45f) * 45f : _rawSweep;
        }

        private static Vector3 ManualPivot(MapDocument document)
        {
            // The rotation widget's pivot is the authoritative pivot: when a pivot
            // has been placed on the rotation gizmo it overrides the selection centre.
            if (document.Selection.RotationPivot.HasValue) return document.Selection.RotationPivot.Value;
            return document.Selection.GetSelectionBoundingBox().Center;
        }

        private static float AngleAt(Vector3 point, Vector3 pivot, Vector3 u, Vector3 v)
        {
            var diff = point - pivot;
            return (float) (Math.Atan2(diff.Dot(v), diff.Dot(u)) * 180d / Math.PI);
        }

        private static float NormalizeDegrees(float degrees)
        {
            return ((degrees % 360f) + 540f) % 360f - 180f;
        }

        #endregion

        #region Rendering

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            base.Render(document, viewport, camera, worldMin, worldMax, im);

            var plan = MakePlan(document);
            if (!plan.Valid) return;

            var frame = plan.Frame;
            var pivot = plan.Pivot;
            var radius = plan.DrawRadius;

            // The spin circle
            const int sides = 48;
            var previous = default(Vector3);
            for (var i = 0; i <= sides; i++)
            {
                var radians = i * 2f * MathF.PI / sides;
                var point = pivot + (frame.U * MathF.Cos(radians) + frame.V * MathF.Sin(radians)) * radius;
                if (i > 0)
                {
                    im.AddLine(camera.WorldToScreen(previous).ToVector2(), camera.WorldToScreen(point).ToVector2(), Color.DarkGray);
                }
                previous = point;
            }

            // The pivot marker
            var cross = radius * 0.04f;
            im.AddLine(camera.WorldToScreen(pivot - frame.U * cross).ToVector2(), camera.WorldToScreen(pivot + frame.U * cross).ToVector2(), Color.Red);
            im.AddLine(camera.WorldToScreen(pivot - frame.V * cross).ToVector2(), camera.WorldToScreen(pivot + frame.V * cross).ToVector2(), Color.Red);

            if (Math.Abs(plan.Sweep) < 0.01f) return;

            DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Start, radius, Color.White);
            DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Start + plan.Sweep, radius, Color.Turquoise);

            // The swept arc
            var arcSides = Math.Max(2, (int) (Math.Abs(plan.Sweep) / 360f * 96));
            previous = default(Vector3);
            for (var i = 0; i <= arcSides; i++)
            {
                var angle = plan.Start + plan.Sweep * i / arcSides;
                var radians = angle * MathF.PI / 180f;
                var point = pivot + (frame.U * MathF.Cos(radians) + frame.V * MathF.Sin(radians)) * radius;
                if (i > 0)
                {
                    im.AddLine(camera.WorldToScreen(previous).ToVector2(), camera.WorldToScreen(point).ToVector2(), Color.Turquoise);
                }
                previous = point;
            }

            // The seams between pieces
            for (var k = 1; k < plan.Steps; k++)
            {
                DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Start + plan.Delta * k, radius, Color.LightGray);
            }
        }

        private static void DrawRadial(I2DRenderer im, OrthographicCamera camera, Vector3 pivot, Vector3 u, Vector3 v, float degrees, float radius, Color colour)
        {
            var radians = degrees * MathF.PI / 180f;
            var point = pivot + (u * MathF.Cos(radians) + v * MathF.Sin(radians)) * radius;
            im.AddLine(camera.WorldToScreen(pivot).ToVector2(), camera.WorldToScreen(point).ToVector2(), colour);
        }

        #endregion
    }
}
