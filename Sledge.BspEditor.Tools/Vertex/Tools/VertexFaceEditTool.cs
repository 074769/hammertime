using System;
using System.Collections.Concurrent;
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
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Vertex.Controls;
using Sledge.BspEditor.Tools.Vertex.Selection;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Shell.Input;

namespace Sledge.BspEditor.Tools.Vertex.Tools
{
	[AutoTranslate]
    [Export(typeof(VertexSubtool))]
    public class VertexFaceEditTool : VertexSubtool
    {
        public static readonly Capability VertexFaceEditToolCapability = Capability.Create("VertexFaceEditTool");

		private readonly VertexEditFaceControl _control;

        public override string OrderHint => "F";
        public override string GetName() => "Face editing";
        public override Control Control => _control;
        
        private readonly List<SolidFace> _selectedFaces;
		public override Capability ToolCapability => VertexFaceEditToolCapability;

        [ImportingConstructor]
        public VertexFaceEditTool(
            [Import] Lazy<VertexEditFaceControl> control
        )
        {
            _control = control.Value;
            _selectedFaces = new List<SolidFace>();
        }

        protected override IEnumerable<Subscription> Subscribe()
        {
            yield return Oy.Subscribe<string>("VertexTool:DeselectAll", _ => ClearSelection());
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Poke", v => Poke(v));
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Bevel", v => Bevel(v));
            yield return Oy.Subscribe<int>("VertexEditFaceTool:Extrude", v => Extrude(v));
		}
        private async Task Extrude(int num)
        {
            var document = GetDocument();
            if (document == null || _selectedFaces.Count == 0) return;

            var tran = new Transaction();
            var parents = new Dictionary<long, IMapObject>();
            foreach (var solidFace in _selectedFaces)
            {
                ExtrudeFace(document, tran, parents, solidFace, num);
            }
            await FinishEdit(document, tran, _selectedFaces.Select(x => x.Solid));
        }

        private async Task Poke(int num)
        {
            var document = GetDocument();
            if (document == null || _selectedFaces.Count == 0) return;

            foreach (var solidFace in _selectedFaces)
            {
                PokeFace(solidFace, num);
            }
            await FinishEdit(document, new Transaction(), _selectedFaces.Select(x => x.Solid));
        }

        private async Task Bevel(int num)
        {
            var document = GetDocument();
            if (document == null || _selectedFaces.Count == 0) return;

            var changed = new List<VertexSolid>();
            foreach (var group in _selectedFaces.GroupBy(x => x.Solid).ToList())
            {
                if (BevelSolid(group.Key, group.Select(x => x.Face).ToList(), num)) changed.Add(group.Key);
            }

            // Nothing could be bevelled (distance too big for the face, or no neighbouring faces): leave everything as it was
            if (changed.Count == 0) return;

            await FinishEdit(document, new Transaction(), changed);
        }

        /// <summary>
        /// Writes every edited solid back to the map in the same transaction as the operations that
        /// built on it (new solids, groups). That way one undo reverses the whole edit.
        /// </summary>
        private async Task FinishEdit(MapDocument document, Transaction tran, IEnumerable<VertexSolid> changedSolids)
        {
            var solids = changedSolids
                .GroupBy(x => x.Real.ID)
                .Select(g => g.First())
                .ToList();

            foreach (var solid in solids)
            {
                // Same guard as VertexSelection.Commit: never write NaN / infinity into a map solid
                if (solid.Copy.Faces.Any(f => f.Vertices.Any(v => !IsFinite(v.Position)))) continue;

                tran.Add(new RemoveMapObjectData(solid.Real.ID, solid.Real.Faces));
                tran.Add(new AddMapObjectData(solid.Real.ID, solid.Copy.Faces.Select(x => x.ToFace(document.Map.NumberGenerator))));
            }

            // The selected faces are stale once the solids are rebuilt, so drop the selection
            ClearSelection();

            if (!tran.IsEmpty)
            {
                await MapDocumentOperation.Perform(document, tran);
            }

            // Working copies now match the map again (and are no longer dirty)
            foreach (var solid in solids)
            {
                solid.Reset();
            }
            Invalidate();
        }

        private static bool IsFinite(System.Numerics.Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        private IList<MutableFace> GetSelectedFaces()
        {
            return _selectedFaces.Select(x => x.Face).ToList();
        }

        private void ClearSelection()
        {
            _selectedFaces.Clear();
            Invalidate();
        }

        #region Edit faces

        private void PokeFace(SolidFace solidFace, int num)
        {
            var face = solidFace.Face;
            var solid = solidFace.Solid.Copy;

            // Remove the face
            solid.Faces.Remove(face);

            var center = face.Origin + face.Plane.Normal * num;
            foreach (var edge in face.GetEdges())
            {
                var v1 = face.Vertices.First(x => x.Position.EquivalentTo(edge.Start));
                var v2 = face.Vertices.First(x => x.Position.EquivalentTo(edge.End));
                var verts = new[] { v1.Position, v2.Position, center };
                var f = new MutableFace(verts, face.Texture.Clone());
                solid.Faces.Add(f);
            }
        }

        /// <summary>
        /// Adds the new solid to the same parent as the solid it was made from, so a brush entity (or group) keeps owning it.
        /// A solid that sits loose in the world is grouped with what was made from it.
        /// </summary>
        private void AttachNextTo(MapDocument document, Transaction transaction, Dictionary<long, IMapObject> parents, Solid original, Solid created)
        {
            IMapObject parent;
            if (!parents.TryGetValue(original.ID, out parent))
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

        private void ExtrudeFace(MapDocument document, Transaction transaction, Dictionary<long, IMapObject> parents, SolidFace solidFace, int num)
        {
            var face = solidFace.Face;
            var solid = solidFace.Solid.Copy;
            var gen = document.Map.NumberGenerator;

            // Remember the original positions, then move the face out by the extrude distance
            var vertexPositions = face.Vertices.Select(x => x.Position).ToList();
            face.Transform(Matrix4x4.CreateTranslation(face.Plane.Normal * num));
            var vertList = face.Vertices.ToList();

            // Only the extruded face keeps its texture, everything else that gets created is null
            var nullTexture = face.Texture.Clone();
            nullTexture.Name = "null";

            var extruded = new Solid(gen.Next("MapObject"));

            // Create a face for each new edge -> old edge
            foreach (var edge in face.GetEdges())
            {
                var startIndex = vertList.FindIndex(x => x.Position.EquivalentTo(edge.Start));
                var endIndex = vertList.FindIndex(x => x.Position.EquivalentTo(edge.End));
                var verts = new[] { vertexPositions[startIndex], vertexPositions[endIndex], edge.End, edge.Start };
                var f = new Face(gen.Next("Face"))
                {
                    Plane = new Sledge.DataStructures.Geometric.Plane(verts[0], verts[1], verts[3]),
                    Texture = nullTexture.Clone()
                };
                f.Vertices.AddRange(verts);
                extruded.Data.Add(f);
            }

            // The original face is now covered by the new solid
            solid.Faces.Remove(face);
            solid.Faces.Add(new MutableFace(vertexPositions, nullTexture.Clone()));

            extruded.Data.Add(new MutableFace(vertexPositions.Reverse<Vector3>(), nullTexture.Clone()).ToFace(gen));
            extruded.Data.Add(face.ToFace(gen));

            AttachNextTo(document, transaction, parents, solidFace.Solid.Real, extruded);
            transaction.Add(new Select(extruded));
            extruded.DescendantsChanged();
        }

        private static Vector3? InwardDirection(Vector3 towardsInterior, Vector3 edgeDirection)
        {
            var v = towardsInterior - edgeDirection * Vector3.Dot(towardsInterior, edgeDirection);
            return v.LengthSquared() < 1e-6f ? (Vector3?) null : Vector3.Normalize(v);
        }

        /// <summary>
        /// Chamfers every edge of the given faces: each edge is cut back by <paramref name="distance"/> along both faces that meet at it.
        /// The face itself shrinks, the neighbouring faces get shorter, and a new sloped face fills each cut.
        /// The solid is rebuilt from its planes, so it stays one valid convex brush. The bevelled faces keep their texture and the new faces are null.
        /// </summary>
        private bool BevelSolid(VertexSolid vertexSolid, IList<MutableFace> faces, float distance)
        {
            var solid = vertexSolid.Copy;
            var d = Math.Abs(distance);
            if (d < 0.001f) return false;

            var points = solid.Faces.SelectMany(f => f.Vertices.Select(v => v.Position)).ToList();
            if (points.Count == 0) return false;
            var centre = points.Aggregate(Vector3.Zero, (a, b) => a + b) / points.Count;

            var chamfers = new List<Sledge.DataStructures.Geometric.Plane>();
            foreach (var face in faces)
            {
                foreach (var edge in face.GetEdges())
                {
                    var along = edge.End - edge.Start;
                    if (along.LengthSquared() < 1e-6f) continue;
                    along = Vector3.Normalize(along);
                    var mid = (edge.Start + edge.End) / 2;

                    var neighbour = solid.Faces.FirstOrDefault(f => !ReferenceEquals(f, face)
                        && f.Vertices.Any(v => v.Position.EquivalentTo(edge.Start))
                        && f.Vertices.Any(v => v.Position.EquivalentTo(edge.End)));
                    if (neighbour == null) continue;

                    var inFace = InwardDirection(face.Origin - mid, along);
                    var inNeighbour = InwardDirection(neighbour.Origin - mid, along);
                    if (inFace == null || inNeighbour == null) continue;

                    // The cut runs from d inside the bevelled face to d inside the neighbouring face
                    var p1 = edge.Start + inFace.Value * d;
                    var p2 = edge.End + inFace.Value * d;
                    var p3 = edge.Start + inNeighbour.Value * d;

                    var n = Vector3.Cross(p2 - p1, p3 - p1);
                    if (n.LengthSquared() < 1e-8f) continue;
                    n = Vector3.Normalize(n);
                    if (Vector3.Dot(n, centre - p1) > 0) n = -n; // face away from the inside of the solid

                    chamfers.Add(new Sledge.DataStructures.Geometric.Plane(n, p1));
                }
            }
            if (chamfers.Count == 0) return false;

            var poly = new Polyhedron(solid.Faces.Select(f => f.Plane).Concat(chamfers));
            if (poly.Polygons.Count < 4) return false;
            if (poly.Polygons.Any(p => p.Vertices.Count < 3 || p.Vertices.Any(v => !IsFinite(v)))) return false;

            var nullTexture = faces[0].Texture.Clone();
            nullTexture.Name = "null";

            var rebuilt = new List<MutableFace>();
            foreach (var pg in poly.Polygons)
            {
                var original = solid.Faces.FirstOrDefault(f => f.Plane.EquivalentTo(pg.Plane, 0.0075f));
                rebuilt.Add(new MutableFace(pg.Vertices, original != null ? original.Texture.Clone() : nullTexture.Clone()));
            }

            solid.Faces.Clear();
            foreach (var f in rebuilt) solid.Faces.Add(f);
            return true;
        }

		#endregion

		#region 3D interaction

		private Vector3? GetIntersectionPoint(MutableFace face, Line line)
        {
            return new Polygon(face.Vertices.Select(x => x.Position)).GetIntersectionPoint(line);
        }
        
        // True while the left button is held after a drag has started: faces under the cursor are added as it sweeps over them
        private bool _painting;

        /// <summary>The nearest face (of the vertex-selected solids) under the cursor, or null.</summary>
        private SolidFace PickFace(PerspectiveCamera camera, ViewportEvent e)
        {
            // First, get the ray that is cast from the clicked point along the viewport frustrum
            var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            var ray = new Line(start, end);

            // Grab all the elements that intersect with the ray
            var hits = Selection.Where(x => x.Copy.BoundingBox.IntersectsWith(ray));

            // Sort the list of intersecting elements by distance from ray origin
            var clicked = hits
                .SelectMany(x => x.Copy.Faces.Select(f => new { Solid = x, Face = f }))
                .Select(x => new { Item = x, Intersection = GetIntersectionPoint(x.Face, ray) })
                .Where(x => x.Intersection != null)
                .OrderBy(x => (x.Intersection.Value - ray.Start).Length())
                .Select(x => x.Item)
                .FirstOrDefault();

            return clicked == null ? null : new SolidFace(clicked.Solid, clicked.Face);
        }

        /// <summary>Adds the picked face (every face of its solid while Shift is held), skipping faces that are already selected.</summary>
        private bool AddPicked(SolidFace picked)
        {
            var added = false;
            var faces = KeyboardState.Shift ? picked.Solid.Copy.Faces.ToList() : new List<MutableFace> { picked.Face };
            foreach (var f in faces)
            {
                if (_selectedFaces.Any(x => ReferenceEquals(x.Face, f))) continue;
                _selectedFaces.Add(new SolidFace(picked.Solid, f));
                added = true;
            }
            return added;
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera,
            ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            e.Handled = true;

            var picked = PickFace(camera, e);

            if (!KeyboardState.Ctrl) ClearSelection();
            if (picked != null) AddPicked(picked);

            Invalidate();
        }

        // Click and drag: hold the left button after the first click and sweep over faces to add them.
        // Ctrl on the first click keeps the existing selection, Shift adds the whole solid under the cursor.
        protected override void DragStart(MapDocument document, MapViewport viewport, PerspectiveCamera camera,
            ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            e.Handled = true;
            _painting = true;
        }

        protected override void DragMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera,
            ViewportEvent e)
        {
            if (!_painting || e.Button != MouseButtons.Left) return;
            e.Handled = true;

            var picked = PickFace(camera, e);
            if (picked != null && AddPicked(picked)) Invalidate();
        }

        protected override void DragEnd(MapDocument document, MapViewport viewport, PerspectiveCamera camera,
            ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            e.Handled = true;
            _painting = false;
        }

        #endregion

        public override async Task SelectionChanged()
        {
            ClearSelection();
        }

        public override async Task ToolSelected()
        {
            ClearSelection();
            await base.ToolSelected();
        }

        public override async Task ToolDeselected()
        {
            _painting = false;
            ClearSelection();
            await base.ToolDeselected();
        }

        public override void Update()
        {
            ClearSelection();
        }

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            var verts = new List<VertexStandard>();
            var indices = new List<int>();
            var groups = new List<BufferGroup>();

            var col = Vector4.One;
            var tintCol = Color.FromArgb(128, Color.OrangeRed).ToVector4();

            foreach (var face in _selectedFaces)
            {
                var vo = verts.Count;
                var io = indices.Count;

                verts.AddRange(face.Face.Vertices.Select(x => new VertexStandard
                {
                    Position = x.Position,
                    Colour = col,
                    Tint = tintCol,
                    Flags = VertexFlags.FlatColour
                }));

                for (var i = 2; i < face.Face.Vertices.Count; i++)
                {
                    indices.Add(vo);
                    indices.Add(vo + i - 1);
                    indices.Add(vo + i);
                }

                groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, face.Face.Origin, (uint) io, (uint)(indices.Count - io)));
            }

            builder.Append(verts, indices.Select(x => (uint) x), groups);
        }

        private class SolidFace
        {
            public VertexSolid Solid { get; set; }
            public MutableFace Face { get; set; }

            public SolidFace(VertexSolid solid, MutableFace face)
            {
                Solid = solid;
                Face = face;
            }
        }
    }
}