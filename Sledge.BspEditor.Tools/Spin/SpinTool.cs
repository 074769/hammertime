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
using Sledge.BspEditor.Modification.Operations.Tree;
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

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Spin tool: sweeps the selected solids around an axis through the rotation
    /// pivot (Selection.RotationPivot, falling back to the selection centre) to
    /// make an arch or ring.
    ///
    /// The sweep is divided into Steps equal slots. Each solid is replaced by one
    /// wedge per slot, built from the solid's radial and vertical extents (its
    /// profile): two radial seam planes through the pivot, an inner and an outer
    /// plane square to the slot's middle direction, and a top and bottom plane.
    /// Neighbouring wedges share a seam plane exactly, so they always connect.
    /// The first wedge sits on the original solid's own position.
    ///
    /// Textures: each wedge takes the texture of the original face that points the
    /// same way, rotated into the slot. Every second wedge is then mirrored about
    /// its slot's mid-radial plane so the pattern continues across the seams.
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

        /// <summary>Number of equal slots (pieces) the sweep is divided into.</summary>
        public int Steps { get; set; } = 8;

        /// <summary>Total sweep in degrees. Used by the panel, Enter and the Spin button; set by dragging.</summary>
        public float AngleDegrees { get; set; } = 90f;

        /// <summary>When true, every second piece gets its texture mapping mirrored so the pattern tiles seamlessly.</summary>
        public bool MirrorUvs { get; set; } = true;

        // Drag state
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

        // The 2D view the tool was last used in; Enter and the Spin button spin around its axis
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
                document.Selection.RotationPivot = null;
            });
        }

        #region Viewport interaction

        protected override void MouseDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            _viewType = camera.ViewType;

            if (!document.Selection.OfType<Solid>().Any()) return;

            var pivot = GetPivot(document);

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

            if (_movingPivot)
            {
                // Move the gizmo pivot, keeping its depth (the axis the current
                // view looks down) and snapping to the grid
                var point = camera.GetUnusedCoordinate(GetPivot(document)) + SnapIfNeeded(camera.ScreenToWorld(e.X, e.Y));
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

            PerformSpin(document, sweep, FrameFromView(camera.ViewType));
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
                PerformSpin(document, AngleDegrees, FrameFromView(_viewType));
            }
        }

        #endregion

        #region Spin operation

        /// <summary>
        /// Called by the sidebar panel's Spin button: apply the spin with the panel's angle.
        /// </summary>
        public void ApplySpin()
        {
            var document = GetDocument();
            if (document == null) return;
            PerformSpin(document, AngleDegrees, FrameFromView(_viewType));
        }

        /// <summary>
        /// Text for the sidebar: the axis in use and what the spin will produce.
        /// </summary>
        public string GetInfoText()
        {
            var document = GetDocument();
            if (document == null) return "";

            var axis = AxisName(_viewType);
            var solids = SelectedSolids(document);
            if (solids.Count == 0) return $"Axis: {axis}. Select solids to spin.";

            var plan = MakePlan(solids, AngleDegrees, GetPivot(document), FrameFromView(_viewType));
            if (!plan.Valid) return $"Axis: {axis}. The selection has no angle around the axis.";

            var text = $"Axis: {axis}. {plan.Steps} pieces of {Math.Abs(plan.Delta):0.##} deg, {Math.Abs(plan.Sweep):0.##} deg in total.";
            if (Math.Abs(plan.Delta) >= MaxPieceDegrees) text += " Pieces are too wide: use more steps.";
            return text;
        }

        private void PerformSpin(MapDocument document, float requestedSweep, SpinFrame frame)
        {
            var solids = SelectedSolids(document);
            if (solids.Count == 0 || Math.Abs(requestedSweep) < 0.01f) return;

            var pivot = GetPivot(document);
            var plan = MakePlan(solids, requestedSweep, pivot, frame);
            if (!plan.Valid) return;

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

            foreach (var solid in solids)
            {
                // Each solid is all or nothing: if any of its pieces can't be made, it stays as it was
                var result = SpinSolid(solid, plan, pivot, frame, MirrorUvs, generator);
                if (result == null)
                {
                    failed++;
                    continue;
                }

                transaction.Add(result.Operations);
                removed.AddRange(result.Removed);
                added.AddRange(result.Added);
            }

            if (!transaction.IsEmpty)
            {
                // The selection after the spin is everything still selected, minus the
                // replaced originals, plus the new pieces. Record it so the pivot survives this spin.
                var after = document.Selection.Select(x => x.ID).Except(removed).Concat(added);
                _pivotKey = SelectionKey(after);
                MapDocumentOperation.Perform(document, transaction);
            }

            if (failed > 0)
            {
                MessageBox.Show(
                    $"{failed} solid(s) could not be spun and were left unchanged. A solid must lie fully on one side of the axis, in front of the pivot.",
                    "Spin Tool",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private sealed class SolidSpin
        {
            public readonly Transaction Operations = new Transaction();
            public readonly List<long> Removed = new List<long>();
            public readonly List<long> Added = new List<long>();
        }

        /// <summary>
        /// Builds the operations that replace one solid by its wedges. Returns null if
        /// the solid can't be swept; nothing is changed then.
        /// </summary>
        private static SolidSpin SpinSolid(Solid solid, Plan plan, Vector3 pivot, SpinFrame frame, bool mirror, UniqueNumberGenerator generator)
        {
            try
            {
                var parent = solid.Hierarchy.Parent.ID;
                var faces = solid.Faces.ToList();
                if (faces.Count == 0) return null;

                // The profile is measured along the direction of the first slot
                var direction = SpinGeometry.Direction(frame.U, frame.V, plan.Mid0);
                if (!SpinGeometry.TryGetProfile(faces.SelectMany(x => x.Vertices), pivot, frame.Axis, direction,
                        out var rMin, out var rMax, out var zMin, out var zMax)) return null;

                // Build every wedge first, so a failure leaves the solid untouched
                var pieces = new List<Solid>();
                for (var j = 0; j < plan.Steps; j++)
                {
                    var mid = plan.Mid0 + plan.Delta * j;
                    var wedge = SpinGeometry.BuildWedge(SpinGeometry.WedgePlanes(pivot, frame.Axis, frame.U, frame.V, mid, plan.Delta, rMin, rMax, zMin, zMax));
                    if (wedge == null) return null;

                    // The original's textures, rotated into this slot
                    var rotation = Matrix4x4.CreateTranslation(-pivot)
                                   * Matrix4x4.CreateFromAxisAngle(frame.Axis, plan.Delta * j * MathF.PI / 180f)
                                   * Matrix4x4.CreateTranslation(pivot);

                    var piece = new Solid(generator.Next("MapObject")) { IsSelected = true };
                    foreach (var polygon in wedge.Polygons)
                    {
                        var normal = polygon.Plane.Normal;

                        // The original face that points the same way once rotated into this slot
                        var best = faces
                            .Select(f => new { Face = f, Dot = Vector3.Dot(Vector3.TransformNormal(f.Plane.Normal, rotation), normal) })
                            .OrderByDescending(x => x.Dot)
                            .First();

                        var face = new Face(generator.Next("Face"));
                        face.Vertices.AddRange(polygon.Vertices);
                        face.Texture = best.Face.Texture.Clone();
                        face.Texture.TransformUniform(rotation);

                        // Faces that point nowhere near an original face (the seams) get a fresh alignment
                        if (best.Dot < 0.999f) face.Texture.AlignToNormal(normal);

                        piece.Data.Add(face);
                    }

                    // Extra data (visgroups, colour, etc)
                    foreach (var data in solid.Data.Where(x => !(x is Face)))
                    {
                        piece.Data.Add((IMapObjectData) data.Clone());
                    }

                    if (mirror && j % 2 == 1)
                    {
                        // Mirror the texture mapping about the mid-radial plane of this slot
                        var reflection = CreateReflection(SpinGeometry.Tangent(frame.Axis, frame.U, frame.V, mid), pivot);
                        foreach (var face in piece.Faces)
                        {
                            face.Texture.TransformUniform(reflection);
                        }
                    }

                    pieces.Add(piece);
                }

                var result = new SolidSpin();
                result.Operations.Add(new Detatch(parent, solid));
                result.Removed.Add(solid.ID);
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
        /// How a spin is laid out: the angle of the first slot's middle, how far each
        /// step turns, and how many steps. The preview and the result both use this,
        /// so they always match.
        /// </summary>
        private struct Plan
        {
            public bool Valid;
            public int Steps;
            public float Delta;   // degrees per step, signed
            public float Mid0;    // degrees, middle of the first slot (the selection's own direction)
            public float Sweep => Delta * Steps;
            public float Start => Mid0 - Delta / 2f;   // where the first slot begins
        }

        private Plan MakePlan(List<Solid> solids, float requested, Vector3 pivot, SpinFrame frame)
        {
            var steps = Math.Max(1, Math.Min(MaxSteps, Steps));
            var valid = TryGetMidAngle(solids, pivot, frame, out var mid);
            return new Plan { Valid = valid, Steps = steps, Delta = requested / steps, Mid0 = mid };
        }

        /// <summary>
        /// The direction of the selection around the axis: the circular mean of its
        /// vertex angles, in degrees. Vertices on the axis have no angle and are skipped.
        /// </summary>
        private static bool TryGetMidAngle(List<Solid> solids, Vector3 pivot, SpinFrame frame, out float mid)
        {
            mid = 0f;

            double sin = 0, cos = 0;
            var count = 0;
            foreach (var solid in solids)
            {
                foreach (var face in solid.Faces)
                {
                    foreach (var p in face.Vertices)
                    {
                        var diff = p - pivot;
                        var radial = diff - frame.Axis * Vector3.Dot(diff, frame.Axis);
                        if (radial.Length() < 0.01f) continue;

                        var radians = AngleAt(p, pivot, frame.U, frame.V) * Math.PI / 180d;
                        sin += Math.Sin(radians);
                        cos += Math.Cos(radians);
                        count++;
                    }
                }
            }

            if (count == 0 || (Math.Abs(sin) < 1e-9 && Math.Abs(cos) < 1e-9)) return false;
            mid = (float) (Math.Atan2(sin, cos) * 180d / Math.PI);
            return true;
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

        private static Vector3 GetPivot(MapDocument document)
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

        /// <summary>
        /// A matrix reflecting everything about the plane with the given normal
        /// running through the given point. Determinant is -1, which
        /// Texture.TransformUniform supports.
        /// </summary>
        private static Matrix4x4 CreateReflection(Vector3 normal, Vector3 point)
        {
            var reflection = Matrix4x4.Identity;
            reflection.M11 -= 2f * normal.X * normal.X;
            reflection.M12 -= 2f * normal.X * normal.Y;
            reflection.M13 -= 2f * normal.X * normal.Z;
            reflection.M21 -= 2f * normal.Y * normal.X;
            reflection.M22 -= 2f * normal.Y * normal.Y;
            reflection.M23 -= 2f * normal.Y * normal.Z;
            reflection.M31 -= 2f * normal.Z * normal.X;
            reflection.M32 -= 2f * normal.Z * normal.Y;
            reflection.M33 -= 2f * normal.Z * normal.Z;

            return Matrix4x4.CreateTranslation(-point) * reflection * Matrix4x4.CreateTranslation(point);
        }

        #endregion

        #region Rendering

        protected override void Render(MapDocument document, IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            base.Render(document, viewport, camera, worldMin, worldMax, im);

            var solids = SelectedSolids(document);
            if (solids.Count == 0) return;

            var frame = FrameFromView(camera.ViewType);
            var pivot = GetPivot(document);
            var radius = Math.Max(64f, document.Selection.GetSelectionBoundingBox().LargestDimension);

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

            // The pivot (gizmo) marker
            var cross = radius * 0.04f;
            im.AddLine(camera.WorldToScreen(pivot - frame.U * cross).ToVector2(), camera.WorldToScreen(pivot + frame.U * cross).ToVector2(), Color.Red);
            im.AddLine(camera.WorldToScreen(pivot - frame.V * cross).ToVector2(), camera.WorldToScreen(pivot + frame.V * cross).ToVector2(), Color.Red);

            // The slots a spin would create, from the same plan the spin uses
            var requested = _dragging ? CurrentDragSweep() : AngleDegrees;
            if (Math.Abs(requested) < 0.01f) return;
            var plan = MakePlan(solids, requested, pivot, frame);
            if (!plan.Valid) return;

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

            // The seams between slots
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
