using Sledge.BspEditor.Tools.DragSelect;
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
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hotkeys;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;
using Plane = Sledge.DataStructures.Geometric.Plane;
using BufferBuilder = Sledge.Rendering.Resources.BufferBuilder;
using BufferGroup = Sledge.Rendering.Resources.BufferGroup;
using ResourceCollector = Sledge.BspEditor.Rendering.Resources.ResourceCollector;
using PipelineType = Sledge.Rendering.Pipelines.PipelineType;
using VertexStandard = Sledge.Rendering.Primitives.VertexStandard;
using VertexFlags = Sledge.Rendering.Primitives.VertexFlags;

namespace Sledge.BspEditor.Tools.Extrude
{
    /// <summary>
    /// Extrude tool, in two steps. The tool's own hotkey (Settings > Hotkeys, default Shift+F) activates the tool, and pressing it
    /// again while the tool is active switches between the steps.
    ///
    /// Select (red): click a face, Ctrl+click to add or remove, or hold the left button and sweep over faces to pick them. Any visible
    /// face can be picked, the solid doesn't have to be selected.
    /// Extrude (blue): drag away from the picked faces and release to make the extrusion. Faces that share vertices meet there
    /// (their edges are mitred, not overlapped). The drag snaps to the amount in the sidebar (1 unit by default); Enter extrudes by exactly that amount.
    /// Escape clears the picked faces. After an extrusion the new end faces stay picked, ready for the next drag.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("PB")]
    [DefaultHotkey("Shift+F")]
    public class ExtrudeTool : BaseTool
    {
        public static readonly Capability ExtrudeToolCapability = Capability.Create("ExtrudeTool");

        private sealed class PickedFace
        {
            public Solid Solid { get; }
            public Face Face { get; }

            public PickedFace(Solid solid, Face face)
            {
                Solid = solid;
                Face = face;
            }
        }

        private readonly object _lock = new object();
        private readonly List<PickedFace> _faces = new List<PickedFace>();
        private PickedFace _hover;
        private string _status;
        private float _amount = 1;

        // false: picking faces (red), true: dragging them out (blue). The tool's hotkey switches between the two
        private bool _extrudeMode;
        private bool _painting;
        private bool _pressOnEdge;

        // Drag state: the face the mouse went down on, where, and how far it has been pulled (already snapped)
        private PickedFace _anchor;
        private int _downX;
        private int _downY;
        private bool _dragging;
        private float _dragDistance;

        /// <summary>True while the tool is in its extrude step (picked faces are blue), false while it is picking faces (red).</summary>
        public bool IsExtrudeMode
        {
            get { lock (_lock) return _extrudeMode; }
        }

        /// <summary>Switches between picking faces and extruding them.</summary>
        public void ToggleMode()
        {
            lock (_lock)
            {
                _extrudeMode = !_extrudeMode;
                _painting = false;
                CancelDragUnlocked();
                _status = null;
            }
        }

        /// <summary>The extrusion amount (units). Drags snap to multiples of it and Enter extrudes by it.</summary>
        public float Amount
        {
            get => _amount;
            set
            {
                lock (_lock)
                {
                    _amount = Math.Max(0.01f, value);
                    _status = null;
                }
            }
        }

        public override Capability ToolCapability => ExtrudeToolCapability;

        public ExtrudeTool()
        {
            Usage = ToolUsage.Both;
        }

        public override Image GetIcon()
        {
            return IconLoader.Load(typeof(Resources), nameof(Resources.Tool_Extrude));
        }

        public override string GetName()
        {
            return "Extrude Tool";
        }

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<IDocument>("MapDocument:SelectionChanged", x =>
            {
                if (x == GetDocument()) Refresh();
            });
            // Undo, redo or another tool may have changed the solids: drop faces that are no longer there
            yield return Oy.Subscribe<Change>("MapDocument:Changed", c =>
            {
                if (c.Document == GetDocument()) Refresh();
            });
        }

        // Pressing the tool's hotkey while the tool is already active re-activates it: the tool is deselected and selected again
        // straight away. That is the cue to switch step instead of starting over (the same way the clip tool cycles its side).
        private static readonly TimeSpan ReactivationWindow = TimeSpan.FromMilliseconds(250);
        private DateTime _deselectedAt = DateTime.MinValue;

        public override Task ToolSelected()
        {
            var reactivated = DateTime.UtcNow - _deselectedAt < ReactivationWindow;
            if (reactivated)
            {
                // Same tool, pressed again: keep the picked faces and switch between selecting and extruding
                ToggleMode();
            }
            else
            {
                lock (_lock) _extrudeMode = false;
                ClearFaces();
            }
            return base.ToolSelected();
        }

        public override Task ToolDeselected()
        {
            // Nothing is cleared here: if this turns out to be a re-activation the picked faces must survive.
            // A genuine switch to another tool is cleaned up by the next ToolSelected.
            CancelDrag();
            lock (_lock) _painting = false;
            _deselectedAt = DateTime.UtcNow;
            return base.ToolDeselected();
        }

        protected override void DocumentChanged()
        {
            CancelDrag();
            ClearFaces();
        }

        #region State

        private void CancelDrag()
        {
            lock (_lock)
            {
                _anchor = null;
                _dragging = false;
                _dragDistance = 0;
            }
        }

        private void ClearFaces()
        {
            lock (_lock)
            {
                _faces.Clear();
                _hover = null;
                _status = null;
            }
        }

        /// <summary>Drops faces whose solid is gone from the map or no longer has that face (undo, redo, another tool).</summary>
        private void Refresh()
        {
            lock (_lock)
            {
                _faces.RemoveAll(f => f.Solid.Hierarchy.Parent == null || !f.Solid.Faces.Any(x => ReferenceEquals(x, f.Face)));
                if (_anchor != null && !_faces.Any(f => ReferenceEquals(f.Face, _anchor.Face))) CancelDragUnlocked();
                _hover = null;
                _status = null;
            }
        }

        private void CancelDragUnlocked()
        {
            _anchor = null;
            _dragging = false;
            _dragDistance = 0;
        }

        private static string Units(float value)
        {
            return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + (Math.Abs(value - 1) < 0.001f ? " unit" : " units");
        }

        /// <summary>One line for the sidebar: what is picked, what to do next, or why nothing happened.</summary>
        public string GetInfoText()
        {
            lock (_lock)
            {
                if (_dragging) return "Extruding " + Units(_dragDistance) + ". Release to make it.";
                if (!string.IsNullOrEmpty(_status)) return _status;

                if (!_extrudeMode)
                {
                    if (_faces.Count == 0) return "Click a face, or hold the mouse and sweep over faces. Ctrl adds or removes. Then press the tool hotkey to extrude.";
                    return _faces.Count + (_faces.Count == 1 ? " face picked." : " faces picked.") + " Press the tool hotkey to extrude them.";
                }

                if (_faces.Count == 0) return "No faces picked. Press the tool hotkey to go back and pick some.";
                return _faces.Count + (_faces.Count == 1 ? " face picked." : " faces picked.") + " Drag to extrude, or press Enter for " + Units(_amount) + ". The tool hotkey goes back to picking.";
            }
        }

        #endregion

        #region Picking

        /// <summary>The nearest face under the cursor that points at the camera, on the nearest solid the cursor is over. Any solid counts.</summary>
        /// <summary>True when the screen point lands within the edge margin of the picked face.</summary>
        private static bool IsOnPickedEdge(PerspectiveCamera camera, PickedFace picked, int x, int y)
        {
            var (start, end) = camera.CastRayFromScreen(new Vector3(x, y, 0));
            var point = new Polygon(picked.Face.Vertices).GetIntersectionPoint(new Line(start, end));
            return point.HasValue && FaceEdgeHitTest.IsOnFaceEdge(camera, picked.Face.Vertices, point.Value);
        }

        private static PickedFace PickFace(MapDocument document, PerspectiveCamera camera, int x, int y)
        {
            var (start, end) = camera.CastRayFromScreen(new Vector3(x, y, 0));
            var line = new Line(start, end);
            var dir = Vector3.Normalize(end - start);

            var hit = document.Map.Root.GetIntersectionsForVisibleObjects(line)
                .FirstOrDefault(h => h.Object is Solid);
            if (hit == null) return null;

            var solid = (Solid) hit.Object;
            Face best = null;
            var bestDistance = float.MaxValue;
            foreach (var face in solid.Faces)
            {
                if (face.Vertices.Count < 3) continue;
                if (Vector3.Dot(face.Plane.Normal, dir) >= 0) continue;

                var point = new Polygon(face.Vertices).GetIntersectionPoint(line);
                if (!point.HasValue) continue;

                var d = (point.Value - start).Length();
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = face;
            }

            return best == null ? null : new PickedFace(solid, best);
        }

        private bool IsPicked(PickedFace picked)
        {
            return _faces.Any(x => ReferenceEquals(x.Face, picked.Face));
        }

        #endregion

        #region Mouse and keys

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            e.Handled = true;

            var picked = PickFace(document, camera, e.X, e.Y);
            _pressOnEdge = picked != null && IsOnPickedEdge(camera, picked, e.X, e.Y);
            lock (_lock)
            {
                CancelDragUnlocked();
                _painting = false;
                _status = null;

                if (_extrudeMode)
                {
                    // Extrude step: nothing is picked or unpicked here. The drag pulls every picked face, measured along
                    // the face under the cursor (or the first picked face when the cursor is somewhere else)
                    var under = picked == null ? null : _faces.FirstOrDefault(x => ReferenceEquals(x.Face, picked.Face));
                    _anchor = under ?? _faces.FirstOrDefault();
                    _downX = e.X;
                    _downY = e.Y;
                    return;
                }

                // Select step
                if (picked == null)
                {
                    if (!KeyboardState.Ctrl) _faces.Clear();
                    return;
                }

                var existing = _faces.FirstOrDefault(x => ReferenceEquals(x.Face, picked.Face));
                if (existing != null && KeyboardState.Ctrl)
                {
                    _faces.Remove(existing);
                    return;
                }

                if (!KeyboardState.Ctrl) _faces.Clear();
                if (!_faces.Any(x => ReferenceEquals(x.Face, picked.Face))) _faces.Add(picked);
            }
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            var hover = PickFace(document, camera, e.X, e.Y);
            lock (_lock)
            {
                if (!_dragging) _hover = _extrudeMode ? null : hover;
            }
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            lock (_lock) _hover = null;
        }

        protected override void DragStart(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            lock (_lock)
            {
                if (_extrudeMode)
                {
                    if (_anchor == null) return;
                    _dragging = true;
                    _dragDistance = 0;
                    _hover = null;
                }
                else
                {
                    // Select step: sweeping over faces picks them (not when the press was on an edge)
                    _painting = !_pressOnEdge;
                }
            }
            e.Handled = true;
        }

        protected override void DragMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            PickedFace anchor;
            float amount;
            int downX, downY;
            lock (_lock)
            {
                if (_painting)
                {
                    e.Handled = true;
                    var swept = PickFace(document, camera, e.X, e.Y);
                    if (swept != null && !IsOnPickedEdge(camera, swept, e.X, e.Y) && !_faces.Any(x => ReferenceEquals(x.Face, swept.Face))) _faces.Add(swept);
                    return;
                }

                if (!_dragging || _anchor == null) return;
                anchor = _anchor;
                amount = _amount;
                downX = _downX;
                downY = _downY;
            }
            e.Handled = true;

            var distance = DragDistance(camera, anchor, e.X - downX, e.Y - downY);
            distance = (float) Math.Round(distance / amount) * amount;
            if (distance < 0) distance = 0;

            lock (_lock) _dragDistance = distance;
        }

        protected override async void DragEnd(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            bool wasDragging;
            float distance;
            lock (_lock)
            {
                if (_painting)
                {
                    _painting = false;
                    e.Handled = true;
                    return;
                }

                wasDragging = _dragging;
                distance = _dragDistance;
                CancelDragUnlocked();
            }
            if (!wasDragging) return;

            e.Handled = true;
            if (distance > 0) await Apply(distance);
        }

        /// <summary>How far the face has been pulled along its normal, from how far the mouse moved on screen (in units, not snapped).</summary>
        private static float DragDistance(PerspectiveCamera camera, PickedFace anchor, float dx, float dy)
        {
            const float probe = 64f;
            var center = anchor.Face.Origin;
            var normal = anchor.Face.Plane.Normal;

            var p0 = camera.WorldToScreen(center);
            var p1 = camera.WorldToScreen(center + normal * probe);
            var along = new Vector2(p1.X - p0.X, p1.Y - p0.Y);
            var pixelsPerUnit = along.Length() / probe;

            // The normal points almost straight at (or away from) the camera, so it has no direction on screen:
            // fall back to the vertical mouse movement, scaled by how far away the face is
            if (pixelsPerUnit < 0.05f)
            {
                var distanceToCamera = (center - camera.Position).Length();
                return -dy * Math.Max(0.1f, distanceToCamera / 400f);
            }

            return Vector2.Dot(new Vector2(dx, dy), along / along.Length()) / pixelsPerUnit;
        }

        private void HandleKey(ViewportEvent e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                float amount;
                lock (_lock) amount = _amount;
                var unused = Apply(amount);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                CancelDrag();
                lock (_lock) _painting = false;
                ClearFaces();
                e.Handled = true;
            }
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e) => HandleKey(e);
        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e) => HandleKey(e);

        #endregion

        #region Geometry

        private static (int, int, int) Key(Vector3 v)
        {
            return ((int) Math.Round(v.X * 10), (int) Math.Round(v.Y * 10), (int) Math.Round(v.Z * 10));
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        /// <summary>
        /// The direction a vertex moves for one unit of extrusion, given the normals of every picked face that uses it.
        /// One normal: straight out. Several: the corner where their offset planes meet (each plane moves exactly one unit),
        /// which is what makes neighbouring faces meet at their shared vertices instead of overlapping or leaving a gap.
        /// </summary>
        private static Vector3 Miter(List<Vector3> normals, Vector3 own)
        {
            if (normals.Count == 1) return own;

            var average = Vector3.Zero;
            foreach (var n in normals) average += n;
            average = average.LengthSquared() > 1e-6f ? Vector3.Normalize(average) : own;

            Vector3? m = null;
            if (normals.Count == 2)
            {
                var g12 = Vector3.Dot(normals[0], normals[1]);
                var det = 1 - g12 * g12;
                if (Math.Abs(det) > 1e-4f)
                {
                    var c = 1 / (1 + g12);
                    m = (normals[0] + normals[1]) * c;
                }
            }
            else if (normals.Count == 3)
            {
                var n0 = normals[0];
                var n1 = normals[1];
                var n2 = normals[2];
                var det = Vector3.Dot(n0, Vector3.Cross(n1, n2));
                if (Math.Abs(det) > 1e-3f)
                {
                    // The point whose distance along each normal is exactly 1
                    m = (Vector3.Cross(n1, n2) + Vector3.Cross(n2, n0) + Vector3.Cross(n0, n1)) / det;
                }
            }

            // Very sharp corners would shoot the vertex far away: use the average direction instead
            if (!m.HasValue || !IsFinite(m.Value) || m.Value.Length() > 4f) return average;
            return m.Value;
        }

        /// <summary>A mitred top is only usable if the top face and every side of the new block stay flat.</summary>
        private static bool IsFlat(List<Vector3> bottom, List<Vector3> top, Vector3 normal)
        {
            if (top.Count != bottom.Count || top.Count < 3 || top.Any(v => !IsFinite(v))) return false;

            var topPlane = new Plane(top[0], top[1], top[2]);
            if (Vector3.Dot(topPlane.Normal, normal) < 0.9f) return false;
            if (top.Any(v => Math.Abs(Vector3.Dot(topPlane.Normal, v - top[0])) > 0.05f)) return false;

            for (var i = 0; i < bottom.Count; i++)
            {
                var j = (i + 1) % bottom.Count;
                var side = Vector3.Cross(bottom[j] - bottom[i], top[i] - bottom[i]);
                var length = side.Length();
                if (length < 1e-4f) return false;
                if (Math.Abs(Vector3.Dot(side / length, top[j] - bottom[i])) > 0.05f) return false;
            }
            return true;
        }

        /// <summary>Where the far end of each picked face goes. Faces that share vertices share the moved vertex too.</summary>
        private static Dictionary<PickedFace, List<Vector3>> BuildTops(List<PickedFace> faces, float distance)
        {
            var normalsAt = new Dictionary<(int, int, int), List<Vector3>>();
            foreach (var f in faces)
            {
                var n = f.Face.Plane.Normal;
                foreach (var v in f.Face.Vertices)
                {
                    var key = Key(v);
                    if (!normalsAt.TryGetValue(key, out var list)) normalsAt[key] = list = new List<Vector3>();
                    if (!list.Any(x => Vector3.Dot(x, n) > 0.9999f)) list.Add(n);
                }
            }

            var result = new Dictionary<PickedFace, List<Vector3>>();
            foreach (var f in faces)
            {
                var n = f.Face.Plane.Normal;
                var bottom = f.Face.Vertices.ToList();
                var mitred = bottom.Select(v => v + Miter(normalsAt[Key(v)], n) * distance).ToList();
                result[f] = IsFlat(bottom, mitred, n) ? mitred : bottom.Select(v => v + n * distance).ToList();
            }
            return result;
        }

        #endregion

        #region Apply

        /// <summary>
        /// Extrudes the picked faces by the distance. Each one gets a new solid (sides, a base on the old face, a top that keeps the
        /// texture), attached to the same parent as the solid it came from. Everything is one transaction, so one undo reverses the lot.
        /// The new top faces become the picked faces.
        /// </summary>
        public async Task Apply(float distance)
        {
            var document = GetDocument();
            if (document == null) return;

            List<PickedFace> faces;
            lock (_lock) faces = _faces.ToList();

            if (faces.Count == 0)
            {
                SetStatus("Pick one or more faces first.");
                return;
            }
            if (distance <= 0)
            {
                SetStatus("Nothing changed.");
                return;
            }

            var tops = BuildTops(faces, distance);
            var transaction = new Transaction();
            var parents = new Dictionary<long, IMapObject>();
            var follower = new LinkFollower(document);
            var created = new List<PickedFace>();
            foreach (var picked in faces)
            {
                var made = ExtrudeFace(document, transaction, parents, follower, picked, tops[picked]);
                if (made != null) created.Add(made);
            }

            if (transaction.IsEmpty)
            {
                SetStatus("Nothing changed.");
                return;
            }

            CancelDrag();
            await MapDocumentOperation.Perform(document, transaction);

            lock (_lock)
            {
                _faces.Clear();
                _faces.AddRange(created);
                _status = "Extruded " + created.Count + (created.Count == 1 ? " face" : " faces") + " by " + Units(distance) + ".";
            }
        }

        private void SetStatus(string text)
        {
            lock (_lock) _status = text;
        }

        /// <summary>
        /// Adds the new solid to the same parent as the solid it was made from, so a brush entity (or group) keeps owning it.
        /// A solid that sits loose in the world is grouped with what was made from it.
        /// </summary>
        private static void AttachNextTo(MapDocument document, Transaction transaction, Dictionary<long, IMapObject> parents, Solid original, Solid created)
        {
            if (!parents.TryGetValue(original.ID, out var parent))
            {
                parent = original.Hierarchy.Parent;
                if (parent is Root)
                {
                    var group = new Group(document.Map.NumberGenerator.Next("MapObject"));
                    transaction.Add(new Attach(parent.ID, group));
                    // Detach before re-attaching, so that undo puts the original solid back where it was
                    transaction.Add(new Detatch(parent.ID, original));
                    transaction.Add(new Attach(group.ID, original));
                    parent = group;
                }
                parents[original.ID] = parent;
            }
            transaction.Add(new Attach(parent.ID, created));
        }

        /// <summary>
        /// A new face. With align, the texture axes are reset to lie in the face's own plane: a texture copied from a face that
        /// points another way would have its axes perpendicular to this one (the "texture axis perpendicular to face" problem).
        /// </summary>
        private static Face MakeFace(UniqueNumberGenerator gen, IEnumerable<Vector3> vertices, Sledge.BspEditor.Primitives.Texture texture, bool align)
        {
            var verts = vertices.ToList();
            var face = new Face(gen.Next("Face")) { Texture = texture };
            face.Vertices.AddRange(verts);
            face.Plane = new Plane(verts[0], verts[1], verts[2]);
            if (align) face.Texture.AlignToNormal(face.Plane.Normal);
            return face;
        }

        /// <summary>Queues the new solid for one face. Returns its top face (to keep picked), or null if the face can't be extruded.</summary>
        private static PickedFace ExtrudeFace(MapDocument document, Transaction transaction, Dictionary<long, IMapObject> parents, LinkFollower follower, PickedFace picked, List<Vector3> top)
        {
            var face = picked.Face;
            var gen = document.Map.NumberGenerator;
            var bottom = face.Vertices.ToList();
            if (bottom.Count < 3 || top.Count != bottom.Count || top.Any(v => !IsFinite(v))) return null;

            // Only the extruded face keeps its texture, everything else that gets created is null
            var nullTexture = face.Texture.Clone();
            nullTexture.Name = "null";

            var extruded = new Solid(gen.Next("MapObject"));

            // A side face for each edge: old edge -> new edge
            for (var i = 0; i < bottom.Count; i++)
            {
                var j = (i + 1) % bottom.Count;
                extruded.Data.Add(MakeFace(gen, new[] { bottom[i], bottom[j], top[j], top[i] }, nullTexture.Clone(), true));
            }

            // The base sits on the old face (turned around) and the top is the moved face
            extruded.Data.Add(MakeFace(gen, Enumerable.Reverse(bottom), nullTexture.Clone(), true));
            var topFace = MakeFace(gen, top, face.Texture.Clone(), false);
            extruded.Data.Add(topFace);

            // The old face is now covered by the new solid, so it loses its texture
            var covered = MakeFace(gen, bottom, nullTexture.Clone(), false);
            transaction.Add(new RemoveMapObjectData(picked.Solid.ID, new[] { face }));
            transaction.Add(new AddMapObjectData(picked.Solid.ID, new[] { covered }));

            extruded.DescendantsChanged(); // its bounds are needed to place the linked copies

            // Extruding a solid of a link's origin instance: the other instances get the same covered face and their own linked copy of the new solid
            if (follower.IsFollowed(picked.Solid))
            {
                follower.FaceReplaced(transaction.Add, picked.Solid, face, covered);
                follower.Added(transaction.Add, picked.Solid, extruded);
            }

            AttachNextTo(document, transaction, parents, picked.Solid, extruded);
            extruded.DescendantsChanged();
            return new PickedFace(extruded, topFace);
        }

        #endregion

        #region Rendering

        // Red while picking faces, blue while extruding them
        private static readonly Color SelectColour = Color.Red;
        private static readonly Color ExtrudeColour = Color.DodgerBlue;
        private static readonly Color HoverColour = Color.Yellow;
        private static readonly Color PreviewColour = Color.Cyan;

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            List<PickedFace> faces;
            bool extrude;
            lock (_lock)
            {
                faces = _faces.ToList();
                extrude = _extrudeMode;
            }
            if (faces.Count == 0) return;

            var verts = new List<VertexStandard>();
            var indices = new List<int>();
            var groups = new List<BufferGroup>();

            var col = Vector4.One;
            var tint = Color.FromArgb(128, extrude ? ExtrudeColour : SelectColour).ToVector4();

            foreach (var p in faces)
            {
                var corners = p.Face.Vertices.ToList();
                if (corners.Count < 3) continue;

                var vo = verts.Count;
                var io = indices.Count;

                verts.AddRange(corners.Select(x => new VertexStandard
                {
                    Position = x,
                    Colour = col,
                    Tint = tint,
                    Flags = VertexFlags.FlatColour
                }));

                for (var i = 2; i < corners.Count; i++)
                {
                    indices.Add(vo);
                    indices.Add(vo + i - 1);
                    indices.Add(vo + i);
                }

                groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, p.Face.Origin, (uint) io, (uint) (indices.Count - io)));
            }

            if (verts.Count > 0) builder.Append(verts, indices.Select(x => (uint) x), groups);
        }

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

        private void Draw(PerspectiveCamera camera, OrthographicCamera ortho, I2DRenderer im)
        {
            List<PickedFace> faces;
            PickedFace hover;
            PickedFace anchor;
            bool dragging;
            bool extrude;
            float distance;
            lock (_lock)
            {
                faces = _faces.ToList();
                hover = _hover;
                anchor = _anchor;
                dragging = _dragging;
                extrude = _extrudeMode;
                distance = _dragDistance;
            }
            var faceColour = extrude ? ExtrudeColour : SelectColour;

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

            void Outline(IList<Vector3> v, Color colour, float width)
            {
                for (var i = 0; i < v.Count; i++) Line(v[i], v[(i + 1) % v.Count], colour, width);
            }

            // While dragging: the block that will be made, with the mitred ends where faces meet
            if (dragging && distance > 0 && faces.Count > 0)
            {
                var tops = BuildTops(faces, distance);
                foreach (var p in faces)
                {
                    var bottom = p.Face.Vertices.ToList();
                    var top = tops[p];
                    Outline(top, PreviewColour, 2f);
                    for (var i = 0; i < bottom.Count; i++) Line(bottom[i], top[i], PreviewColour, 1f);
                }
            }

            foreach (var p in faces) Outline(p.Face.Vertices.ToList(), faceColour, 3f);

            if (hover != null && !faces.Any(f => ReferenceEquals(f.Face, hover.Face))) Outline(hover.Face.Vertices.ToList(), HoverColour, 3f);

            // Which step the tool is in, so it is obvious whether a click picks faces or a drag extrudes them
            if (camera != null)
            {
                var label = extrude ? "EXTRUDE  (tool hotkey: back to select)" : "SELECT FACES  (tool hotkey: extrude)";
                im.AddText(new Vector2(12, 12), faceColour, FontType.Large, label);
            }

            // The amount, next to the face being pulled
            if (camera != null && dragging && anchor != null)
            {
                var end = anchor.Face.Origin + anchor.Face.Plane.Normal * distance;
                var toEnd = Vector3.Dot(end - camera.Position, camera.Direction);
                if (toEnd > 1f)
                {
                    var screen = camera.WorldToScreen(end);
                    var text = Units(distance);
                    var size = im.CalcTextSize(FontType.Large, text);
                    im.AddText(new Vector2(screen.X - size.X / 2, screen.Y - size.Y - 8), PreviewColour, FontType.Large, text);
                }
            }
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
