using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Terrain;
using Sledge.BspEditor.Tools.Properties;
using Sledge.Common;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Sledge.Shell.Input;
using Plane = Sledge.DataStructures.Geometric.Plane;
using BufferBuilder = Sledge.Rendering.Resources.BufferBuilder;
using BufferGroup = Sledge.Rendering.Resources.BufferGroup;
using ResourceCollector = Sledge.BspEditor.Rendering.Resources.ResourceCollector;
using PipelineType = Sledge.Rendering.Pipelines.PipelineType;
using VertexStandard = Sledge.Rendering.Primitives.VertexStandard;

namespace Sledge.BspEditor.Tools.Terrain
{
    /// <summary>
    /// Terrain tool: sculpt a height grid (like a Source displacement) and turn it into GoldSrc brushes.
    ///
    /// A terrain has two states. As a height grid ("Heightfield") it is only data, drawn as a coloured preview, and it is
    /// sculpted with a brush: Raise, Lower, Flatten, Smooth or Set height, always in whole levels so it stays on the grid.
    /// "Bake to brushes" makes the convex brushes (and, if wanted, coarse CLIP collision blocks); "Edit heightfield" removes
    /// them again and brings the grid back, because the grid is kept while the terrain is brushes.
    ///
    /// Make a terrain: select a brush with the Select tool, then press "Terrain from selected brush". The brush is replaced
    /// by a height grid that follows its top surface. Sculpt by dragging over a terrain in the 3D view (or the Top view).
    /// Ctrl swaps Raise and Lower.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("PC")]
    public class TerrainTool : BaseTool
    {
        public static readonly Capability TerrainToolCapability = Capability.Create("TerrainTool");

        // Brush settings (the sidebar sets these)
        public SculptMode Mode { get; set; } = SculptMode.Raise;
        public float Radius { get; set; } = 96f;
        public float Strength { get; set; } = 1f;
        public BrushFalloff Falloff { get; set; } = BrushFalloff.Smooth;
        public int SetLevel { get; set; } = 0;

        // New terrain settings
        public float NewCellSize { get; set; } = 32f;
        public float NewLevelHeight { get; set; } = 16f;

        public long ActiveId { get; private set; }

        private const int StampIntervalMs = 100;

        private sealed class Stroke
        {
            public TerrainData Terrain;
            public TerrainData.Snapshot Before;
            public int TargetLevel;
            public Vector3 LastStamp;
            public MapViewport Viewport;
            public long LastStampTime;
        }

        private Stroke _stroke;
        private Vector3? _hover;
        private TerrainData _hoverTerrain;
        private (Vector3 Start, Vector3 End)? _lastRay;
        private OrthographicCamera.OrthographicType _lastView = OrthographicCamera.OrthographicType.Top;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        // Brush count shown in the sidebar, cached per terrain version
        private long _countId;
        private int _countVersion = -1;
        private bool _countMerge;
        private int _countBrushes;

        public TerrainTool()
        {
            Usage = ToolUsage.Both;
        }

        public override Image GetIcon()
        {
            return IconLoader.Load(typeof(Resources), "Tool_Terrain");
        }

        public override string GetName()
        {
            return "Terrain Tool";
        }

        public override Capability ToolCapability => TerrainToolCapability;

        #region Terrains

        public IEnumerable<TerrainData> Terrains()
        {
            var doc = GetDocument();
            return doc == null ? Enumerable.Empty<TerrainData>() : doc.Map.Data.Get<TerrainData>().ToList();
        }

        public TerrainData Active
        {
            get
            {
                var doc = GetDocument();
                if (doc == null) return null;
                var all = doc.Map.Data.Get<TerrainData>().ToList();
                return all.FirstOrDefault(x => x.ID == ActiveId) ?? all.FirstOrDefault();
            }
        }

        public void SetActive(long id)
        {
            ActiveId = id;
        }

        private static void Run(MapDocument document, IOperation operation)
        {
            ((Action) (async () => await MapDocumentOperation.Perform(document, operation)))();
        }

        /// <summary>
        /// Turns the selected brushes into a terrain: the grid covers their combined box and follows their upper surface.
        /// The brushes are replaced by the terrain (one undo step brings them back). Returns a message when it can't.
        /// </summary>
        public string FromSelection()
        {
            var document = GetDocument();
            if (document == null) return "No map is open.";

            var solids = document.Selection.OfType<Solid>().Where(x => x.Hierarchy.Parent != null).ToList();
            if (solids.Count == 0) return "Select a brush with the Select tool first, then press this button.";

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in solids.SelectMany(x => x.Faces).SelectMany(f => f.Vertices))
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }

            var cell = Math.Max(1f, NewCellSize);
            if ((max.X - min.X) / cell > 256 || (max.Y - min.Y) / cell > 256)
            {
                return "That would be more than 256 x 256 cells. Use a larger cell size.";
            }

            var faces = solids.SelectMany(x => x.Faces)
                .Where(f => f.Plane.Normal.Z > 0.001f && f.Vertices.Count >= 3)
                .Select(f => (Normal: f.Plane.Normal, Vertices: f.Vertices.ToList()))
                .ToList();

            float? Top(float x, float y)
            {
                float? best = null;
                foreach (var f in faces)
                {
                    if (!Inside(f.Vertices, x, y)) continue;
                    var v0 = f.Vertices[0];
                    var z = v0.Z - (f.Normal.X * (x - v0.X) + f.Normal.Y * (y - v0.Y)) / f.Normal.Z;
                    if (!best.HasValue || z > best.Value) best = z;
                }
                return best;
            }

            var number = document.Map.Data.Get<TerrainData>().Count() + 1;
            var terrain = new TerrainData
            {
                ID = document.Map.NumberGenerator.Next("MapObject"),
                Name = "Terrain " + number,
                SurfaceTexture = TopTexture(solids) ?? document.Map.Data.GetOne<ActiveTexture>()?.Name ?? "GROUND",
                Field = Heightfield.FromSurface(min, max, cell, NewLevelHeight, Top)
            };

            var transaction = new Transaction();
            foreach (var group in solids.GroupBy(x => x.Hierarchy.Parent.ID))
            {
                transaction.Add(new Detatch(group.Key, group.Cast<IMapObject>()));
            }
            transaction.Add(new AddMapData(terrain));

            ActiveId = terrain.ID;
            Run(document, transaction);
            return $"{terrain.Name}: {terrain.Field.CellsX} x {terrain.Field.CellsY} cells.";
        }

        /// <summary>The texture of the highest upward-facing face, for the new terrain's surface.</summary>
        private static string TopTexture(List<Solid> solids)
        {
            return solids.SelectMany(x => x.Faces)
                .Where(f => f.Plane.Normal.Z > 0.5f && f.Texture != null && !string.IsNullOrWhiteSpace(f.Texture.Name))
                .OrderByDescending(f => f.Vertices.Max(v => v.Z))
                .Select(f => f.Texture.Name)
                .FirstOrDefault();
        }

        /// <summary>Whether the point is inside the polygon seen from above (a convex polygon, edges count as inside).</summary>
        private static bool Inside(List<Vector3> polygon, float x, float y)
        {
            var sign = 0;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var c = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
                if (Math.Abs(c) < 1e-3f) continue;
                var side = c > 0 ? 1 : -1;
                if (sign == 0) sign = side;
                else if (side != sign) return false;
            }
            return true;
        }

        public void DeleteActive()
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null) return;

            if (terrain.State == TerrainState.Brushes && FindGroup(document, terrain.GroupId) != null)
            {
                MessageBox.Show("This terrain is brushes right now. Press \"Edit heightfield\" first (that removes the brushes), then delete it.", "Terrain Tool", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Run(document, new RemoveMapData(terrain));
        }

        #endregion

        #region Sculpting

        private TerrainData PickIn3D(MapDocument document, (Vector3 Start, Vector3 End) ray, out Vector3 hit)
        {
            TerrainData best = null;
            hit = default;
            var bestDistance = float.MaxValue;
            foreach (var t in document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield))
            {
                if (!t.Field.RayCast(ray.Start, ray.End, out var h)) continue;
                var d = (h - ray.Start).LengthSquared();
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = t;
                hit = h;
            }
            return best;
        }

        private TerrainData PickIn2D(MapDocument document, Vector3 world, out Vector3 hit)
        {
            hit = default;
            // Prefer the active terrain when they overlap
            var candidates = document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield)
                .OrderByDescending(x => x.ID == ActiveId).ToList();
            foreach (var t in candidates)
            {
                var z = t.Field.HeightAt(world.X, world.Y);
                if (!z.HasValue) continue;
                hit = new Vector3(world.X, world.Y, z.Value);
                return t;
            }
            return null;
        }

        private SculptMode EffectiveMode()
        {
            if (!KeyboardState.Ctrl) return Mode;
            if (Mode == SculptMode.Raise) return SculptMode.Lower;
            if (Mode == SculptMode.Lower) return SculptMode.Raise;
            return Mode;
        }

        private void BeginStroke(MapDocument document, MapViewport viewport, TerrainData terrain, Vector3 hit)
        {
            ActiveId = terrain.ID;

            var f = terrain.Field;
            var vx = (int) Math.Round((hit.X - f.Origin.X) / f.CellSize);
            var vy = (int) Math.Round((hit.Y - f.Origin.Y) / f.CellSize);
            vx = Math.Max(0, Math.Min(f.CellsX, vx));
            vy = Math.Max(0, Math.Min(f.CellsY, vy));

            _stroke = new Stroke
            {
                Terrain = terrain,
                Before = terrain.Capture(),
                // Flatten works towards the height where the stroke began
                TargetLevel = Mode == SculptMode.Set ? SetLevel : f.GetLevel(vx, vy),
                LastStamp = hit,
                Viewport = viewport,
                LastStampTime = _clock.ElapsedMilliseconds
            };
            Stamp(hit);
        }

        private void Stamp(Vector3 at)
        {
            if (_stroke == null) return;
            _stroke.Terrain.Field.ApplyBrush(at.X, at.Y, Math.Max(1f, Radius), EffectiveMode(), Math.Max(0.1f, Strength), Falloff, _stroke.TargetLevel);
            _stroke.LastStamp = at;
            _stroke.LastStampTime = _clock.ElapsedMilliseconds;
        }

        private void EndStroke(MapDocument document)
        {
            var stroke = _stroke;
            _stroke = null;
            if (stroke == null || document == null) return;

            var after = stroke.Terrain.Capture();
            if (stroke.Before.Field.SameShapeAs(after.Field)) return;

            // One undo step for the whole stroke
            Run(document, new TerrainEdit(stroke.Terrain, stroke.Before, after));
        }

        #endregion

        #region Actions (sidebar)

        public void SmoothActive(int iterations)
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null || terrain.State != TerrainState.Heightfield) return;

            var before = terrain.Capture();
            if (!terrain.Field.Smooth(iterations)) return;
            Run(document, new TerrainEdit(terrain, before, terrain.Capture()));
        }

        /// <summary>Makes the vertices the active terrain shares with the other height grids the same height (the active one wins).</summary>
        public string WeldActive()
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null || terrain.State != TerrainState.Heightfield) return "Select a terrain in heightfield state.";

            var transaction = new Transaction();
            var changed = 0;
            foreach (var other in document.Map.Data.Get<TerrainData>().Where(x => x != terrain && x.State == TerrainState.Heightfield))
            {
                var before = other.Capture();
                var n = terrain.Field.WeldTo(other.Field);
                if (n == 0) continue;
                changed += n;
                transaction.Add(new TerrainEdit(other, before, other.Capture()));
            }

            if (transaction.IsEmpty) return "Nothing to weld: no other height grid shares an edge with this one.";
            Run(document, transaction);
            return changed + " vertices welded.";
        }

        public void RenameActive(string name)
        {
            var terrain = Active;
            if (terrain != null && !string.IsNullOrWhiteSpace(name)) terrain.Name = name.Trim();
        }

        private static IMapObject FindGroup(MapDocument document, long id)
        {
            return id == 0 ? null : document.Map.Root.FindByID(id);
        }

        /// <summary>Bake the height grid to brushes, or bring the grid back (removing the brushes).</summary>
        public void ToggleState()
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null) return;

            if (terrain.State == TerrainState.Heightfield) Bake(document, terrain);
            else BackToHeightfield(document, terrain);
        }

        private void Bake(MapDocument document, TerrainData terrain)
        {
            var field = terrain.Field;

            var brushes = field.BuildBrushes(terrain.MergeCoplanar);
            List<TerrainBrush> collision = null;
            if (terrain.CollisionEnabled) collision = field.BuildCollision(terrain.CollisionBlock, terrain.CollisionSampling, terrain.MergeCoplanar);

            var total = brushes.Count + (collision?.Count ?? 0);
            if (total > 1500)
            {
                var ok = MessageBox.Show($"This makes {total} brushes. That is a lot for a GoldSrc map. Continue?", "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

            var before = terrain.Capture();
            var generator = document.Map.NumberGenerator;
            var root = document.Map.Root;
            var transaction = new Transaction();

            var group = BuildGroup(generator, brushes, terrain.SurfaceTexture, terrain.HiddenTexture);
            transaction.Add(new Attach(root.ID, group));

            long collisionId = 0;
            if (collision != null && collision.Count > 0)
            {
                var cg = BuildGroup(generator, collision, terrain.ClipTexture, terrain.ClipTexture);
                transaction.Add(new Attach(root.ID, cg));
                collisionId = cg.ID;
            }

            var after = terrain.Capture();
            after.State = TerrainState.Brushes;
            after.GroupId = group.ID;
            after.CollisionGroupId = collisionId;
            transaction.Add(new TerrainEdit(terrain, before, after));

            Run(document, transaction);
        }

        private void BackToHeightfield(MapDocument document, TerrainData terrain)
        {
            var surface = FindGroup(document, terrain.GroupId);
            var collision = FindGroup(document, terrain.CollisionGroupId);

            if (surface != null || collision != null)
            {
                var ok = MessageBox.Show(
                    "Back to the height grid removes the brushes of this terrain, including any changes you made to them by hand. Continue?",
                    "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

            var before = terrain.Capture();
            var transaction = new Transaction();
            foreach (var g in new[] { surface, collision }.Where(x => x != null && x.Hierarchy.Parent != null))
            {
                transaction.Add(new Detatch(g.Hierarchy.Parent.ID, g));
            }

            var after = terrain.Capture();
            after.State = TerrainState.Heightfield;
            after.GroupId = 0;
            after.CollisionGroupId = 0;
            transaction.Add(new TerrainEdit(terrain, before, after));

            Run(document, transaction);
        }

        private static Group BuildGroup(UniqueNumberGenerator generator, List<TerrainBrush> brushes, string topTexture, string otherTexture)
        {
            var group = new Group(generator.Next("MapObject"));
            var colour = Colour.GetRandomBrushColour();

            foreach (var brush in brushes)
            {
                var solid = new Solid(generator.Next("MapObject"));
                foreach (var f in brush.Faces)
                {
                    var face = new Face(generator.Next("Face"));
                    face.Vertices.AddRange(f.Vertices);

                    var normal = new Plane(f.Vertices[0], f.Vertices[1], f.Vertices[2]).Normal;
                    face.Texture = new Sledge.BspEditor.Primitives.Texture { Name = f.Kind == TerrainFaceKind.Top ? topTexture : otherTexture };
                    face.Texture.AlignToNormal(normal);

                    solid.Data.Add(face);
                }
                solid.Data.Add(new ObjectColor(colour));
                solid.Hierarchy.Parent = group;
            }

            return group;
        }

        #endregion

        #region Info

        public string GetInfoText()
        {
            var document = GetDocument();
            if (document == null) return "";

            var terrain = Active;
            if (terrain == null) return "No terrain yet. Select a brush with the Select tool, then press \"Terrain from selected brush\".";

            var f = terrain.Field;
            if (_countId != terrain.ID || _countVersion != f.Version || _countMerge != terrain.MergeCoplanar)
            {
                _countId = terrain.ID;
                _countVersion = f.Version;
                _countMerge = terrain.MergeCoplanar;
                _countBrushes = f.BuildBrushes(terrain.MergeCoplanar).Count;
            }

            var text = $"{terrain.Name}: {f.CellsX} x {f.CellsY} cells, {f.MinLevel} to {f.MaxLevel} levels. Makes {_countBrushes} brushes";
            if (terrain.CollisionEnabled) text += $" + {f.BuildCollision(terrain.CollisionBlock, terrain.CollisionSampling, terrain.MergeCoplanar).Count} collision";
            text += ".";

            if (terrain.State == TerrainState.Brushes) text += " Now brushes: press Edit heightfield to sculpt again.";
            else if (terrain.CollisionEnabled) text += " Tie the visible surface to func_illusionary so only the CLIP blocks are solid.";
            return text;
        }

        #endregion

        #region Viewport interaction

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            var ray = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            _lastRay = ray;

            var terrain = PickIn3D(document, ray, out var hit);
            if (terrain == null) return;
            BeginStroke(document, viewport, terrain, hit);
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            var ray = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            _lastRay = ray;

            var terrain = PickIn3D(document, ray, out var hit);
            _hoverTerrain = terrain;
            _hover = terrain != null ? hit : (Vector3?) null;
            if (terrain == null || _stroke == null || _stroke.Viewport != viewport) return;

            MoveStroke(hit);
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button == MouseButtons.Left) EndStroke(document);
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            EndStroke(document);
            _hover = null;
            _lastRay = null;
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;
            _lastView = camera.ViewType;
            if (camera.ViewType != OrthographicCamera.OrthographicType.Top) return;

            var world = camera.ScreenToWorld(e.X, e.Y);

            var terrain = PickIn2D(document, world, out var hit);
            if (terrain == null) return;
            BeginStroke(document, viewport, terrain, hit);
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            _lastView = camera.ViewType;
            if (camera.ViewType != OrthographicCamera.OrthographicType.Top)
            {
                _hover = null;
                return;
            }

            var world = camera.ScreenToWorld(e.X, e.Y);

            var terrain = PickIn2D(document, world, out var h);
            _hoverTerrain = terrain;
            _hover = terrain != null ? h : (Vector3?) null;
            if (terrain != null && _stroke != null && _stroke.Viewport == viewport) MoveStroke(h);
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            EndStroke(document);
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            EndStroke(document);
            _hover = null;
        }

        private void MoveStroke(Vector3 hit)
        {
            // A stamp every time the brush has moved a fair bit, so a drag leaves an even trail
            var spacing = Math.Max(Radius / 4f, 1f);
            if ((hit - _stroke.LastStamp).Length() >= spacing) Stamp(hit);
        }

        protected override void UpdateFrame(MapDocument document, MapViewport viewport, PerspectiveCamera camera, long frame)
        {
            // Holding still keeps raising or lowering, like an airbrush
            if (_stroke == null || _stroke.Viewport != viewport || !_lastRay.HasValue) return;
            if (_clock.ElapsedMilliseconds - _stroke.LastStampTime < StampIntervalMs) return;

            var mode = EffectiveMode();
            if (mode != SculptMode.Raise && mode != SculptMode.Lower && mode != SculptMode.Smooth) return;

            if (_stroke.Terrain.Field.RayCast(_lastRay.Value.Start, _lastRay.Value.End, out var hit)) Stamp(hit);
        }

        public override System.Threading.Tasks.Task PreToolDeselect()
        {
            var document = GetDocument();
            EndStroke(document);
            return base.PreToolDeselect();
        }

        #endregion

        #region Rendering

        private static readonly Vector4 RingColour = new Vector4(1f, 0.9f, 0.1f, 1f);
        private static readonly Vector4 OutlineColour = new Vector4(1f, 1f, 1f, 1f);

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            var vertices = new List<VertexStandard>();
            var indices = new List<uint>();

            void Line(Vector3 a, Vector3 b, Vector4 colour)
            {
                var o = (uint) vertices.Count;
                vertices.Add(Vert(a, colour));
                vertices.Add(Vert(b, colour));
                indices.Add(o);
                indices.Add(o + 1);
            }

            // The footprint of the active terrain
            var active = Active;
            if (active != null && active.State == TerrainState.Heightfield)
            {
                var f = active.Field;
                var z = f.Origin.Z + f.MaxLevel * f.LevelHeight;
                var x1 = f.Origin.X + f.CellsX * f.CellSize;
                var y1 = f.Origin.Y + f.CellsY * f.CellSize;
                var p = new[]
                {
                    new Vector3(f.Origin.X, f.Origin.Y, z), new Vector3(x1, f.Origin.Y, z),
                    new Vector3(x1, y1, z), new Vector3(f.Origin.X, y1, z)
                };
                for (var i = 0; i < 4; i++) Line(p[i], p[(i + 1) % 4], OutlineColour);
            }

            // The brush ring, lying on the surface
            if (_hover.HasValue)
            {
                var terrain = _hoverTerrain ?? Active;
                var centre = _hover.Value;
                const int sides = 48;
                var previous = default(Vector3);
                for (var i = 0; i <= sides; i++)
                {
                    var a = i * 2f * MathF.PI / sides;
                    var x = centre.X + MathF.Cos(a) * Radius;
                    var y = centre.Y + MathF.Sin(a) * Radius;
                    var height = terrain?.Field.HeightAt(x, y) ?? centre.Z;
                    var point = new Vector3(x, y, height + 0.5f);
                    if (i > 0) Line(previous, point, RingColour);
                    previous = point;
                }
            }

            if (vertices.Count == 0) return;

            var groups = new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, (uint) indices.Count) };
            builder.Append(vertices, indices, groups);
        }

        private static VertexStandard Vert(Vector3 p, Vector4 colour)
        {
            return new VertexStandard
            {
                Position = p,
                Colour = colour,
                Normal = Vector3.UnitZ,
                Texture = Vector2.Zero,
                Tint = Vector4.One
            };
        }

        #endregion
    }
}
