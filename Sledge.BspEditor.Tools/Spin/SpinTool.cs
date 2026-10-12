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
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Spin tool: duplicates the selected solids around the rotation pivot
    /// (Selection.RotationPivot, falling back to the selection centre) so they
    /// form an arch.
    ///
    /// Auto steps (default): the selection's own angular width is the step, so
    /// piece j is exactly the selection rotated into slot j. Nothing needs cutting.
    ///
    /// Manual steps: the sweep is divided into Steps equal slots. Each piece is
    /// trimmed to its slot (when Bisect is on). A piece that can't be trimmed
    /// cleanly is left unchanged and reported, never kept overlapping.
    ///
    /// Every piece is a copy of the selection, so the rotated texture axes move
    /// with the geometry (like texture lock). Odd pieces then get their texture
    /// mapping mirrored about the mid-radial plane of their slot, which makes the
    /// pattern continue across each seam.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("M")]
    public class SpinTool : BaseTool
    {
        public static readonly Capability SpinToolCapability = Capability.Create("SpinTool");

        /// <summary>Largest number of pieces a spin can make.</summary>
        public const int MaxSteps = 64;

        /// <summary>Mouse movement (in pixels) before a press counts as a drag.</summary>
        private const float DragThresholdPixels = 4f;

        /// <summary>Screen-space grab radius for the pivot (gizmo) handle.</summary>
        private const int PivotDragSize = 8;

        /// <summary>Vertices within this distance of the straight line between their neighbours are removed (map units).</summary>
        private const float CollinearTolerance = 0.05f;

        /// <summary>Manual mode: number of equal slots the sweep is divided into.</summary>
        public int Steps { get; set; } = 8;

        /// <summary>Total sweep in degrees. Used by the panel, Enter and the Spin button; set by dragging.</summary>
        public float AngleDegrees { get; set; } = 90f;

        /// <summary>When true each step is exactly one piece wide, so pieces tile without cutting.</summary>
        public bool AutoSteps { get; set; } = true;

        /// <summary>Manual mode only: trim every piece to its own slot.</summary>
        public bool BisectPieces { get; set; } = true;

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

            var frame = FrameFromView(camera.ViewType);
            var solids = SelectedSolids(document);
            if (solids.Count == 0) return;

            // Keep the panel in sync with the sweep that is actually applied
            var plan = MakePlan(solids, sweep, GetPivot(document), frame);
            AngleDegrees = plan.Sweep;
            Oy.Publish("SpinTool:SetAngle", plan.Sweep.ToString("0.#", CultureInfo.InvariantCulture));

            PerformSpin(document, plan.Sweep, frame);
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
            if (plan.Auto)
            {
                return $"Axis: {axis}. {plan.Steps} pieces of {plan.Width:0.##} deg each, {Math.Abs(plan.Sweep):0.##} deg in total.";
            }

            var text = $"Axis: {axis}. {plan.Steps} steps of {Math.Abs(plan.Delta):0.##} deg.";
            if (plan.Width > Math.Abs(plan.Delta) + 0.05f)
            {
                text += BisectPieces
                    ? $" Pieces are {plan.Width:0.##} deg wide, wider than a step, so the excess is trimmed."
                    : $" Pieces are {plan.Width:0.##} deg wide, wider than a step, so they will overlap.";
            }
            else if (plan.Width > 0 && plan.Width < Math.Abs(plan.Delta) - 0.05f)
            {
                text += $" Pieces are {plan.Width:0.##} deg wide, narrower than a step, so gaps will show.";
            }
            return text;
        }

        private void PerformSpin(MapDocument document, float requestedSweep, SpinFrame frame)
        {
            var solids = SelectedSolids(document);
            if (solids.Count == 0 || Math.Abs(requestedSweep) < 0.01f) return;

            var pivot = GetPivot(document);
            var plan = MakePlan(solids, requestedSweep, pivot, frame);

            // Auto steps tile exactly, so trimming only applies to manual steps
            var bisect = !plan.Auto && BisectPieces && Math.Abs(plan.Delta) < 179f;
            var generator = document.Map.NumberGenerator;

            var transaction = new Transaction();
            var removed = new List<long>();
            var added = new List<long>();
            var failed = 0;

            foreach (var solid in solids)
            {
                // Each solid is all or nothing: if any of its pieces can't be made cleanly, it stays as it was
                var result = SpinSolid(solid, plan, pivot, frame, bisect, MirrorUvs, generator);
                if (result == null)
                {
                    failed++;
                    continue;
                }

                if (result.Operations.IsEmpty) continue;
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
                    $"{failed} solid(s) could not be cut cleanly to their slot and were left unchanged.",
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
        /// Builds the operations that replace one solid by its pieces. Returns null
        /// if the solid can't be made into clean pieces; nothing is changed then.
        /// </summary>
        private static SolidSpin SpinSolid(Solid solid, Plan plan, Vector3 pivot, SpinFrame frame, bool bisect, bool mirror, UniqueNumberGenerator generator)
        {
            try
            {
                var parent = solid.Hierarchy.Parent.ID;
                var result = new SolidSpin();

                // One piece per step. Piece 0 is the original; the rest are copies.
                for (var j = 0; j < plan.Steps; j++)
                {
                    Solid piece;
                    if (j == 0)
                    {
                        piece = solid;
                    }
                    else
                    {
                        var copy = (Solid) solid.Copy(generator);
                        copy.IsSelected = true;

                        var rotation = Matrix4x4.CreateTranslation(-pivot)
                                       * Matrix4x4.CreateFromAxisAngle(frame.Axis, plan.Delta * j * MathF.PI / 180f)
                                       * Matrix4x4.CreateTranslation(pivot);
                        copy.Transform(rotation);
                        foreach (var face in copy.Faces)
                        {
                            face.Texture.TransformUniform(rotation);
                        }
                        piece = copy;
                    }

                    if (bisect)
                    {
                        // Null means nothing of this piece lies in its slot
                        if (!TrimToSlot(piece, pivot, frame, plan.Anchor + plan.Delta * j, plan.Delta, generator, out var trimmed)) return null;
                        piece = trimmed;
                    }

                    if (mirror && j % 2 == 1 && piece != null)
                    {
                        // Mirror the texture mapping about the mid-radial plane of this piece's slot
                        var normal = TangentAt(frame.Axis, frame.U, frame.V, plan.Anchor + plan.Delta * (j + 0.5f));
                        var reflection = CreateReflection(normal, pivot);
                        foreach (var face in piece.Faces)
                        {
                            face.Texture.TransformUniform(reflection);
                        }
                    }

                    if (j == 0)
                    {
                        if (!ReferenceEquals(piece, solid))
                        {
                            result.Operations.Add(new Detatch(parent, solid));
                            result.Removed.Add(solid.ID);
                            if (piece != null)
                            {
                                result.Operations.Add(new Attach(parent, piece));
                                result.Added.Add(piece.ID);
                            }
                        }
                    }
                    else if (piece != null)
                    {
                        result.Operations.Add(new Attach(parent, piece));
                        result.Added.Add(piece.ID);
                    }
                }

                return result;
            }
            catch (Exception)
            {
                // A degenerate solid must not corrupt the spin; the caller reports it
                return null;
            }
        }

        /// <summary>
        /// Trims a solid to the angular slot [start, start + sweep] around the pivot.
        /// Returns true on success: result is the solid itself when nothing needed
        /// trimming, a new trimmed solid, or null when nothing lies in the slot.
        /// Returns false when the trimmed piece can't be made valid.
        /// </summary>
        private static bool TrimToSlot(Solid solid, Vector3 pivot, SpinFrame frame, float startDegrees, float sweepDegrees, UniqueNumberGenerator generator, out Solid result)
        {
            result = solid;
            if (Math.Abs(sweepDegrees) < 0.01f || Math.Abs(sweepDegrees) >= 179f) return true;
            if (solid.Faces.Any(x => x.Vertices.Count < 3)) return false;

            var sign = sweepDegrees > 0 ? 1f : -1f;
            var startPlane = new Plane(TangentAt(frame.Axis, frame.U, frame.V, startDegrees) * sign, pivot);
            var endPlane = new Plane(TangentAt(frame.Axis, frame.U, frame.V, startDegrees + sweepDegrees) * -sign, pivot);

            if (!KeepFront(solid, startPlane, generator, out var afterStart, out var emptyStart)) return false;
            if (emptyStart)
            {
                result = null;
                return true;
            }

            if (!KeepFront(afterStart, endPlane, generator, out var afterEnd, out var emptyEnd)) return false;
            if (emptyEnd)
            {
                result = null;
                return true;
            }

            // Nothing was trimmed, so the solid is already inside its slot
            if (ReferenceEquals(afterEnd, solid)) return true;

            CleanFaces(afterEnd);
            if (!afterEnd.IsValid()) return false;

            result = afterEnd;
            return true;
        }

        /// <summary>
        /// Keeps the part of a solid on the front side of a plane. Returns false if
        /// the split fails, or empty when nothing lies on the front side.
        /// </summary>
        private static bool KeepFront(Solid solid, Plane plane, UniqueNumberGenerator generator, out Solid result, out bool empty)
        {
            result = null;
            empty = false;

            if (solid.Split(generator, plane, out var back, out var front))
            {
                if (front == null || !front.Faces.Any())
                {
                    empty = true;
                    return true;
                }
                result = front;
                return true;
            }

            // The plane doesn't cut the solid: it is wholly in front (keep it) or wholly behind (nothing left)
            if (front != null)
            {
                result = solid;
                return true;
            }
            if (back != null)
            {
                empty = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes vertices that sit on the straight line between their neighbours.
        /// The precision split leaves such vertices on the cut lines, slightly off the
        /// exact line, and the validity check rejects them, which is what made
        /// trimmed pieces fall back to uncut, overlapping copies.
        /// </summary>
        private static void CleanFaces(Solid solid)
        {
            foreach (var face in solid.Faces)
            {
                var vertices = face.Vertices.ToList();
                var cleaned = RemoveNearlyCollinear(vertices, CollinearTolerance);
                if (cleaned.Count == vertices.Count) continue;

                face.Vertices.Clear();
                face.Vertices.AddRange(cleaned);
            }
        }

        private static List<Vector3> RemoveNearlyCollinear(List<Vector3> vertices, float tolerance)
        {
            var list = vertices.ToList();
            var changed = true;
            while (changed && list.Count > 3)
            {
                changed = false;
                for (var i = 0; i < list.Count; i++)
                {
                    var a = list[(i + list.Count - 1) % list.Count];
                    var b = list[i];
                    var c = list[(i + 1) % list.Count];

                    // Only vertices lying between their neighbours; spikes are never collapsed
                    if (Vector3.Dot(a - b, c - b) >= 0) continue;

                    var ac = c - a;
                    var length = ac.Length();
                    if (length < 1e-6f) continue;

                    var distance = Vector3.Cross(b - a, ac).Length() / length;
                    if (distance < tolerance)
                    {
                        list.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
            return list;
        }

        #endregion

        #region Plan

        /// <summary>
        /// How a spin is laid out: where the first piece starts, how far each step
        /// turns, and how many steps. The preview and the result both use this, so
        /// they always match.
        /// </summary>
        private struct Plan
        {
            public bool Auto;
            public int Steps;
            public float Delta;   // degrees per step, signed
            public float Anchor;  // degrees, where the first piece starts
            public float Width;   // selection width in degrees, 0 when unknown
            public float Sweep => Delta * Steps;
        }

        private Plan MakePlan(List<Solid> solids, float requested, Vector3 pivot, SpinFrame frame)
        {
            var sign = requested < 0 ? -1f : 1f;
            var hasSpan = TryGetSpan(solids, pivot, frame, out var min, out var max);
            var width = hasSpan ? max - min : 0f;

            // The leading edge of the selection, for the spin direction
            var anchor = hasSpan ? (sign > 0 ? min : max) : 0f;

            if (AutoSteps && width >= 0.5f && width < 179f)
            {
                var steps = (int) Math.Round(Math.Abs(requested) / width);
                steps = Math.Max(1, Math.Min(MaxSteps, steps));
                return new Plan { Auto = true, Steps = steps, Delta = sign * width, Anchor = anchor, Width = width };
            }

            var count = Math.Max(1, Math.Min(MaxSteps, Steps));
            return new Plan { Auto = false, Steps = count, Delta = requested / count, Anchor = anchor, Width = width };
        }

        /// <summary>
        /// The angular range covered by the selected solids' vertices, in degrees.
        /// Vertices on the spin axis are skipped, since they have no angle.
        /// </summary>
        private static bool TryGetSpan(List<Solid> solids, Vector3 pivot, SpinFrame frame, out float min, out float max)
        {
            min = max = 0f;

            var angles = new List<float>();
            foreach (var solid in solids)
            {
                foreach (var face in solid.Faces)
                {
                    foreach (var p in face.Vertices)
                    {
                        var diff = p - pivot;
                        var radial = diff - frame.Axis * Vector3.Dot(diff, frame.Axis);
                        if (radial.Length() < 0.01f) continue;
                        angles.Add(AngleAt(p, pivot, frame.U, frame.V));
                    }
                }
            }
            if (angles.Count == 0) return false;

            // The circular mean is the reference, so ranges that cross +/-180 degrees still work
            double sin = 0, cos = 0;
            foreach (var angle in angles)
            {
                var radians = angle * Math.PI / 180d;
                sin += Math.Sin(radians);
                cos += Math.Cos(radians);
            }
            var mean = (float) (Math.Atan2(sin, cos) * 180d / Math.PI);

            var unwrapped = angles.Select(x => NormalizeDegrees(x - mean)).ToList();
            min = mean + unwrapped.Min();
            max = mean + unwrapped.Max();
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
        /// The unit tangent direction at the given angle in the spin plane: the
        /// direction in which the angle increases when rotating about the axis.
        /// </summary>
        private static Vector3 TangentAt(Vector3 axis, Vector3 u, Vector3 v, float degrees)
        {
            var radians = degrees * MathF.PI / 180f;
            var radial = u * MathF.Cos(radians) + v * MathF.Sin(radians);
            return Vector3.Cross(axis, radial).Normalise();
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

            // The sector a spin would create, from the same plan the spin uses
            var requested = _dragging ? CurrentDragSweep() : AngleDegrees;
            if (Math.Abs(requested) < 0.01f) return;
            var plan = MakePlan(solids, requested, pivot, frame);

            DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Anchor, radius, Color.White);
            if (Math.Abs(plan.Sweep) < 0.01f) return;
            DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Anchor + plan.Sweep, radius, Color.Turquoise);

            // The swept arc
            var arcSides = Math.Max(2, (int) (Math.Abs(plan.Sweep) / 360f * 96));
            previous = default(Vector3);
            for (var i = 0; i <= arcSides; i++)
            {
                var angle = plan.Anchor + plan.Sweep * i / arcSides;
                var radians = angle * MathF.PI / 180f;
                var point = pivot + (frame.U * MathF.Cos(radians) + frame.V * MathF.Sin(radians)) * radius;
                if (i > 0)
                {
                    im.AddLine(camera.WorldToScreen(previous).ToVector2(), camera.WorldToScreen(point).ToVector2(), Color.Turquoise);
                }
                previous = point;
            }

            // The step boundaries inside the sector
            for (var k = 1; k < plan.Steps; k++)
            {
                DrawRadial(im, camera, pivot, frame.U, frame.V, plan.Anchor + plan.Delta * k, radius, Color.LightGray);
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
