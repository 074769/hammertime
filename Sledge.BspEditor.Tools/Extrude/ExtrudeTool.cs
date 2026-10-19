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

namespace Sledge.BspEditor.Tools.Extrude
{
    /// <summary>
    /// Extrude tool: pick faces of the selected solids, set a distance, press Extrude.
    ///
    /// Click a solid in the 3D view to select it first, then click its faces (Ctrl+click adds or removes a face,
    /// Shift+click picks every face of the solid, hold the left button and sweep to paint faces).
    /// The new block is previewed while faces are picked. Enter applies, Escape clears the faces.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("PB")]
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
        private bool _painting;
        private float _distance = 16;

        /// <summary>How far to extrude along each face's normal (units).</summary>
        public float Distance
        {
            get => _distance;
            set
            {
                lock (_lock)
                {
                    _distance = value;
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

        public override Task ToolSelected()
        {
            ClearFaces();
            return base.ToolSelected();
        }

        public override Task ToolDeselected()
        {
            _painting = false;
            ClearFaces();
            return base.ToolDeselected();
        }

        protected override void DocumentChanged()
        {
            ClearFaces();
        }

        #region State

        private static IEnumerable<Solid> SelectedSolids(MapDocument document)
        {
            return document.Selection.OfType<Solid>().ToList();
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

        /// <summary>Drops faces whose solid is no longer selected or no longer has that face.</summary>
        private void Refresh()
        {
            var document = GetDocument();
            lock (_lock)
            {
                if (document == null)
                {
                    _faces.Clear();
                }
                else
                {
                    var selected = new HashSet<Solid>(SelectedSolids(document));
                    _faces.RemoveAll(f => !selected.Contains(f.Solid) || !f.Solid.Faces.Any(x => ReferenceEquals(x, f.Face)));
                }
                _hover = null;
                _status = null;
            }
        }

        /// <summary>One line for the sidebar: what is selected, what will happen, or why it can't.</summary>
        public string GetInfoText()
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_status)) return _status;

                var document = GetDocument();
                if (document == null) return "";
                if (!SelectedSolids(document).Any()) return "Select a solid (click it in the 3D view), then click its faces.";
                if (_faces.Count == 0) return "Click a face to pick it. Shift+click picks the whole solid, drag to paint faces. Ctrl adds or removes.";
                return _faces.Count + (_faces.Count == 1 ? " face selected." : " faces selected.");
            }
        }

        #endregion

        #region Picking

        /// <summary>
        /// True if nothing else is in front of the point as seen from the camera. The solids themselves count, so the far side
        /// of a brush is hidden by its own near side.
        /// </summary>
        private static bool IsVisible(MapDocument document, PerspectiveCamera camera, Vector3 point)
        {
            var distance = (point - camera.Position).Length();
            if (distance < 1e-3f) return true;

            var tolerance = 0.5f + distance * 0.003f;
            foreach (var hit in document.Map.Root.GetIntersectionsForVisibleObjects(new Line(camera.Position, point)))
            {
                if (!(hit.Object is Solid)) continue;
                return (hit.Intersection - camera.Position).Length() >= distance - tolerance;
            }
            return true;
        }

        /// <summary>The nearest visible, camera-facing face of a selected solid under the cursor, or null.</summary>
        private PickedFace PickFace(MapDocument document, PerspectiveCamera camera, int x, int y)
        {
            var selected = SelectedSolids(document).ToList();
            if (selected.Count == 0) return null;

            var (start, end) = camera.CastRayFromScreen(new Vector3(x, y, 0));
            var line = new Line(start, end);
            var dir = Vector3.Normalize(end - start);

            PickedFace best = null;
            var bestPoint = Vector3.Zero;
            var bestDistance = float.MaxValue;

            foreach (var solid in selected)
            {
                foreach (var face in solid.Faces)
                {
                    if (face.Vertices.Count < 3) continue;
                    if (Vector3.Dot(face.Plane.Normal, dir) >= 0) continue;

                    var hit = new Polygon(face.Vertices).GetIntersectionPoint(line);
                    if (!hit.HasValue) continue;

                    var d = (hit.Value - start).Length();
                    if (d >= bestDistance) continue;

                    bestDistance = d;
                    bestPoint = hit.Value;
                    best = new PickedFace(solid, face);
                }
            }

            return best != null && IsVisible(document, camera, bestPoint) ? best : null;
        }

        /// <summary>Adds the picked face (every face of its solid with Shift). With toggle, faces already picked are removed instead.</summary>
        private bool AddPicked(PickedFace picked, bool wholeSolid, bool toggle)
        {
            var changed = false;
            var faces = wholeSolid ? picked.Solid.Faces.ToList() : new List<Face> { picked.Face };
            lock (_lock)
            {
                foreach (var f in faces)
                {
                    var existing = _faces.FirstOrDefault(x => ReferenceEquals(x.Face, f));
                    if (existing != null)
                    {
                        if (!toggle) continue;
                        _faces.Remove(existing);
                    }
                    else
                    {
                        _faces.Add(new PickedFace(picked.Solid, f));
                    }
                    changed = true;
                }
                _status = null;
            }
            return changed;
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
            e.Handled = true;

            var picked = PickFace(document, camera, e.X, e.Y);
            if (picked != null)
            {
                if (!KeyboardState.Ctrl) ClearFaces();
                AddPicked(picked, KeyboardState.Shift, KeyboardState.Ctrl);
                _painting = true;
                return;
            }

            // Not on a face of a selected solid: a click on a solid selects that solid, so its faces can be picked next
            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var closest = document.Map.Root.GetIntersectionsForVisibleObjects(new Line(start, end))
                .Where(x => x.Object is Solid)
                .Select(x => (Solid) x.Object)
                .FirstOrDefault();

            if (closest != null)
            {
                if (!closest.IsSelected || KeyboardState.Ctrl) SelectSolid(document, closest);
            }
            else if (!KeyboardState.Ctrl) ClearFaces();
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            // Painting: faces under the cursor are added as it sweeps over them
            if (_painting)
            {
                var painted = PickFace(document, camera, e.X, e.Y);
                if (painted != null && !_faces.Any(x => ReferenceEquals(x.Face, painted.Face))) AddPicked(painted, false, false);
            }

            var hover = PickFace(document, camera, e.X, e.Y);
            lock (_lock) _hover = hover;
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            _painting = false;
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            lock (_lock) _hover = null;
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
                ClearFaces();
                e.Handled = true;
            }
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e) => HandleKey(e);
        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e) => HandleKey(e);

        #endregion

        #region Apply

        public void ClearSelectedFaces()
        {
            ClearFaces();
        }

        /// <summary>Picks every face of the selected solids.</summary>
        public void SelectAllFaces()
        {
            var document = GetDocument();
            if (document == null) return;
            lock (_lock)
            {
                _faces.Clear();
                foreach (var solid in SelectedSolids(document))
                {
                    foreach (var f in solid.Faces) _faces.Add(new PickedFace(solid, f));
                }
                _status = null;
            }
        }

        /// <summary>
        /// Extrudes the picked faces. Each one gets a new solid (sides, a base on the old face, a top that keeps the texture),
        /// attached to the same parent as the solid it came from. Everything is one transaction, so one undo reverses the lot.
        /// </summary>
        public async Task Apply()
        {
            var document = GetDocument();
            if (document == null) return;

            List<PickedFace> faces;
            float distance;
            lock (_lock)
            {
                faces = _faces.ToList();
                distance = _distance;
            }

            if (faces.Count == 0)
            {
                SetStatus("Pick one or more faces first.");
                return;
            }

            var transaction = new Transaction();
            var parents = new Dictionary<long, IMapObject>();
            var done = 0;
            foreach (var picked in faces)
            {
                if (ExtrudeFace(document, transaction, parents, picked, distance)) done++;
            }

            if (transaction.IsEmpty)
            {
                SetStatus("Nothing changed.");
                return;
            }

            ClearFaces();
            await MapDocumentOperation.Perform(document, transaction);
            SetStatus("Extruded " + done + (done == 1 ? " face" : " faces") + " by "
                      + distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " units.");
        }

        private void SetStatus(string text)
        {
            lock (_lock) _status = text;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
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

        private static Face MakeFace(UniqueNumberGenerator gen, IEnumerable<Vector3> vertices, Sledge.BspEditor.Primitives.Texture texture)
        {
            var verts = vertices.ToList();
            var face = new Face(gen.Next("Face")) { Texture = texture };
            face.Vertices.AddRange(verts);
            face.Plane = new Plane(verts[0], verts[1], verts[2]);
            return face;
        }

        private static bool ExtrudeFace(MapDocument document, Transaction transaction, Dictionary<long, IMapObject> parents, PickedFace picked, float distance)
        {
            var face = picked.Face;
            var gen = document.Map.NumberGenerator;
            var bottom = face.Vertices.ToList();
            if (bottom.Count < 3) return false;

            var offset = face.Plane.Normal * distance;
            var top = bottom.Select(v => v + offset).ToList();
            if (top.Any(v => !IsFinite(v))) return false;

            // Only the extruded face keeps its texture, everything else that gets created is null
            var nullTexture = face.Texture.Clone();
            nullTexture.Name = "null";

            var extruded = new Solid(gen.Next("MapObject"));

            // A side face for each edge: old edge -> new edge
            for (var i = 0; i < bottom.Count; i++)
            {
                var j = (i + 1) % bottom.Count;
                var side = MakeFace(gen, new[] { bottom[i], bottom[j], top[j], top[i] }, nullTexture.Clone());
                extruded.Data.Add(side);
            }

            // The base sits on the old face (turned around) and the top is the moved face
            extruded.Data.Add(MakeFace(gen, Enumerable.Reverse(bottom), nullTexture.Clone()));
            extruded.Data.Add(MakeFace(gen, top, face.Texture.Clone()));

            // The old face is now covered by the new solid, so it loses its texture
            var covered = MakeFace(gen, bottom, nullTexture.Clone());
            transaction.Add(new RemoveMapObjectData(picked.Solid.ID, new[] { face }));
            transaction.Add(new AddMapObjectData(picked.Solid.ID, new[] { covered }));

            AttachNextTo(document, transaction, parents, picked.Solid, extruded);
            transaction.Add(new Select(extruded));
            extruded.DescendantsChanged();
            return true;
        }

        #endregion

        #region Rendering

        private static readonly Color FaceColour = Color.OrangeRed;
        private static readonly Color HoverColour = Color.Yellow;
        private static readonly Color PreviewColour = Color.Cyan;

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
            float distance;
            lock (_lock)
            {
                faces = _faces.ToList();
                hover = _hover;
                distance = _distance;
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

            void Outline(Face face, Vector3 shift, Color colour, float width)
            {
                var v = face.Vertices;
                for (var i = 0; i < v.Count; i++) Line(v[i] + shift, v[(i + 1) % v.Count] + shift, colour, width);
            }

            foreach (var p in faces)
            {
                var offset = p.Face.Plane.Normal * distance;

                // The block that will be made: the moved face and the edges that join it to the old one
                Outline(p.Face, offset, PreviewColour, 1f);
                foreach (var v in p.Face.Vertices) Line(v, v + offset, PreviewColour, 1f);

                Outline(p.Face, Vector3.Zero, FaceColour, 3f);
            }

            if (hover != null && !faces.Any(f => ReferenceEquals(f.Face, hover.Face))) Outline(hover.Face, Vector3.Zero, HoverColour, 3f);
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
