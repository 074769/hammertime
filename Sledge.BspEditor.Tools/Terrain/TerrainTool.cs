using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using Sledge.BspEditor.Commands.Clipboard;
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
using VertexFlags = Sledge.Rendering.Primitives.VertexFlags;

namespace Sledge.BspEditor.Tools.Terrain
{
    /// <summary>
    /// Terrain tool: sculpt a surface like a Source displacement and turn it into GoldSrc brushes.
    ///
    /// Make a terrain from faces: click a face of a brush in the 3D view (Ctrl adds more faces), or select whole brushes with
    /// the Select tool, then press "Terrain from selected faces". Each face becomes a grid that is moved along the face's
    /// normal, so a floor rises and falls, a wall bulges in or out, and the inside faces of an arch can be sculpted into a cave.
    /// Faces made together are one terrain: they are sculpted, baked and reverted together, and meet without cracks.
    ///
    /// As a grid ("Heightfield") the terrain is only data, drawn as a coloured preview, and sculpted with a brush: Raise, Lower,
    /// Flatten, Smooth or Set height, always in whole levels so it stays on the grid. "Bake to brushes" extrudes the surface back
    /// along the normals into convex brushes (and, if wanted, coarse CLIP collision blocks); "Edit heightfield" removes them again
    /// and brings the grid back, and "Revert to original brush" puts back the brushes the terrain was made from.
    ///
    /// Sculpt by dragging over a terrain in the 3D view (or the Top view): the left and right mouse buttons each have a mode,
    /// Ctrl swaps Raise and Lower, Ctrl+wheel changes the radius, and Shift+drag in a 2D view moves the terrain.
    /// Strength is in world units per stamp; amounts smaller than one level add up over the stroke.
    /// </summary>
    [Export(typeof(ITool))]
    [Export]
    [OrderHint("PC")]
    public class TerrainTool : BaseTool
    {
        public static readonly Capability TerrainToolCapability = Capability.Create("TerrainTool");

        // Brush settings (the sidebar sets these)

        /// <summary>What the left mouse button does.</summary>
        public SculptMode Mode { get; set; } = SculptMode.Raise;

        /// <summary>What the right mouse button does.</summary>
        public SculptMode RightMode { get; set; } = SculptMode.Lower;

        public float Radius { get; set; } = 96f;

        /// <summary>World units per brush stamp. Less than a level adds up over several stamps.</summary>
        public float Strength { get; set; } = 4f;

        public BrushFalloff Falloff { get; set; } = BrushFalloff.Smooth;
        public int SetLevel { get; set; } = 0;

        // Settings for a new terrain (the sidebar shows the active terrain's own when there is one)
        public float NewCellSize { get; set; } = 32f;
        public float NewLevelHeight { get; set; } = 16f;

        /// <summary>The set (see <see cref="TerrainData.SetId"/>) the sidebar is showing.</summary>
        public long ActiveSetId { get; private set; }

        private const int StampIntervalMs = 100;

        private sealed class Touched
        {
            public TerrainData Terrain;
            public TerrainData.Snapshot Before;
            public float[] Residual;
        }

        private sealed class Stroke
        {
            public List<TerrainData> Candidates;
            public Dictionary<TerrainData, Touched> Entries = new Dictionary<TerrainData, Touched>();
            public int Target;
            public Vector3 LastStamp;
            public MapViewport Viewport;
            public long LastStampTime;
            public MouseButtons Button;
        }

        private sealed class MoveDrag
        {
            public List<TerrainData> Terrains;
            public List<TerrainData.Snapshot> Before;
            public Vector3 StartWorld;
            public Vector3 OriginStart;
        }

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

        private Stroke _stroke;
        private MoveDrag _move;
        private Vector3? _hover;
        private TerrainData _hoverTerrain;
        private (Vector3 Start, Vector3 End)? _lastRay;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly object _lock = new object();
        private readonly List<PickedFace> _faces = new List<PickedFace>();

        private readonly Dictionary<Heightfield, (int Version, Vector3 Min, Vector3 Max)> _bounds
            = new Dictionary<Heightfield, (int, Vector3, Vector3)>();

        // Brush count shown in the sidebar, cached per terrain version
        private long _countKey = -1;
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

        #region Terrains and sets

        private static void Run(MapDocument document, IOperation operation)
        {
            ((Action) (async () => await MapDocumentOperation.Perform(document, operation)))();
        }

        /// <summary>One entry per set (the first surface of it), for the sidebar's list.</summary>
        public List<TerrainData> Terrains()
        {
            var doc = GetDocument();
            if (doc == null) return new List<TerrainData>();

            var all = doc.Map.Data.Get<TerrainData>().ToList();
            return all.GroupBy(x => x.SetId)
                .Select(g => g.FirstOrDefault(x => x.ID == g.Key) ?? g.First())
                .ToList();
        }

        /// <summary>The surfaces of the active set.</summary>
        public List<TerrainData> ActiveSet()
        {
            var doc = GetDocument();
            if (doc == null) return new List<TerrainData>();

            var all = doc.Map.Data.Get<TerrainData>().ToList();
            var set = all.Where(x => x.SetId == ActiveSetId).ToList();
            if (set.Count > 0) return set;

            // Nothing chosen (or it is gone): the first set
            var first = all.FirstOrDefault();
            return first == null ? set : all.Where(x => x.SetId == first.SetId).ToList();
        }

        /// <summary>The first surface of the active set: its name and options stand for the set.</summary>
        public TerrainData Active
        {
            get
            {
                var set = ActiveSet();
                return set.FirstOrDefault(x => x.ID == x.SetId) ?? set.FirstOrDefault();
            }
        }

        public void SetActive(long terrainId)
        {
            var doc = GetDocument();
            var t = doc?.Map.Data.Get<TerrainData>().FirstOrDefault(x => x.ID == terrainId);
            if (t != null) ActiveSetId = t.SetId;
        }

        /// <summary>Changes every heightfield surface of the active set and records one undo step.</summary>
        private bool MutateSet(Action<TerrainData> change)
        {
            var document = GetDocument();
            var set = ActiveSet().Where(x => x.State == TerrainState.Heightfield).ToList();
            if (document == null || set.Count == 0) return false;

            var transaction = new Transaction();
            foreach (var t in set)
            {
                var before = t.Capture();
                change(t);
                t.Field.Touch();
                transaction.Add(new TerrainEdit(t, before, t.Capture()));
            }
            Run(document, transaction);
            return true;
        }

        private static IMapObject FindGroup(MapDocument document, long id)
        {
            return id == 0 ? null : document.Map.Root.FindByID(id);
        }

        #endregion

        #region Face selection

        /// <summary>The nearest face under the cursor that points at the camera, on the nearest solid the cursor is over.</summary>
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

        public void ClearFaces()
        {
            lock (_lock) _faces.Clear();
        }

        private int FaceCount()
        {
            lock (_lock) return _faces.Count;
        }

        protected override void MouseClick(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.Button != MouseButtons.Left) return;

            var picked = PickFace(document, camera, e.X, e.Y);
            lock (_lock)
            {
                if (picked == null)
                {
                    if (!KeyboardState.Ctrl) _faces.Clear();
                    return;
                }

                var existing = _faces.FindIndex(x => ReferenceEquals(x.Face, picked.Face));
                if (existing >= 0)
                {
                    _faces.RemoveAt(existing);
                    return;
                }

                if (!KeyboardState.Ctrl) _faces.Clear();
                _faces.Add(picked);
            }
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Escape) ClearFaces();
        }

        protected override void KeyDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (e.KeyCode == Keys.Escape) ClearFaces();
        }

        #endregion

        #region Making a terrain

        private static float FaceArea(Face f)
        {
            var area = 0f;
            for (var i = 1; i + 1 < f.Vertices.Count; i++)
            {
                area += Vector3.Cross(f.Vertices[i] - f.Vertices[0], f.Vertices[i + 1] - f.Vertices[0]).Length() / 2;
            }
            return area;
        }

        private static TerrainTextureInfo FaceTexture(Face face)
        {
            var t = face.Texture;
            if (t == null || string.IsNullOrWhiteSpace(t.Name)) return null;
            return new TerrainTextureInfo
            {
                Name = t.Name,
                XShift = t.XShift,
                YShift = t.YShift,
                XScale = t.XScale,
                YScale = t.YScale,
                Rotation = t.Rotation,
                UAxis = t.UAxis,
                VAxis = t.VAxis
            };
        }

        /// <summary>
        /// Turns the picked faces into terrain (or, with none picked, the top face of each selected brush). Every face becomes a
        /// surface along its own normal; faces made together are one set. The brushes the faces belong to are replaced by the
        /// terrain (one undo step brings them back) and are kept in it so "Revert to original brush" can put them back.
        /// </summary>
        public string FromSelection()
        {
            var document = GetDocument();
            if (document == null) return "No map is open.";

            var sources = new List<PickedFace>();
            lock (_lock) sources.AddRange(_faces);

            if (sources.Count == 0)
            {
                // Whole brushes: the face that points up the most
                foreach (var solid in document.Selection.OfType<Solid>().Where(x => x.Hierarchy.Parent != null))
                {
                    var top = solid.Faces.Where(f => f.Vertices.Count >= 3 && f.Plane.Normal.Z > 0.5f)
                        .OrderByDescending(f => f.Plane.Normal.Z).ThenByDescending(FaceArea).FirstOrDefault();
                    if (top != null) sources.Add(new PickedFace(solid, top));
                }
            }

            if (sources.Count == 0) return "Click a face in the 3D view (Ctrl adds more), or select brushes with the Select tool, then press this button.";

            var usable = sources.Where(x => x.Face.Vertices.Count == 4 && x.Solid.Hierarchy.Parent != null).ToList();
            var skipped = sources.Count - usable.Count;
            if (usable.Count == 0) return "Only four sided faces can be used. This one has " + sources[0].Face.Vertices.Count + " corners.";

            var cell = Math.Max(1f, NewCellSize);
            var levelHeight = Math.Max(1f, NewLevelHeight);
            var setId = document.Map.NumberGenerator.Next("MapObject");
            var number = document.Map.Data.Get<TerrainData>().Select(x => x.SetId).Distinct().Count() + 1;
            var terrains = new List<TerrainData>();

            foreach (var p in usable)
            {
                var v = p.Face.Vertices;
                var normal = p.Face.Plane.Normal;

                // How far back the brush goes behind this face: the surface is extruded that deep
                var depth = p.Solid.Faces.SelectMany(f => f.Vertices).Max(x => Vector3.Dot(v[0] - x, normal));
                if (depth < levelHeight) depth = levelHeight * 2;

                var field = Heightfield.FromQuad(v[0], v[1], v[2], v[3], normal, cell, levelHeight, depth);
                var info = FaceTexture(p.Face);
                var id = terrains.Count == 0 ? setId : document.Map.NumberGenerator.Next("MapObject");

                var t = new TerrainData
                {
                    ID = id,
                    SetId = setId,
                    Name = usable.Count == 1 ? "Terrain " + number : $"Terrain {number} ({terrains.Count + 1})",
                    SurfaceTexture = info?.Name ?? document.Map.Data.GetOne<ActiveTexture>()?.Name ?? "GROUND",
                    Field = field
                };
                if (info != null)
                {
                    t.CellTextures = Enumerable.Range(0, field.CellCount).Select(_ => info.Clone()).ToArray();
                }
                terrains.Add(t);
            }

            // The faces that meet get the same direction along their joint, so sculpting leaves no crack
            Heightfield.Weld(terrains.Select(x => x.Field).ToList());

            var solids = usable.Select(x => x.Solid).Distinct().ToList();
            terrains[0].OriginalBrushes = TerrainOriginalBrushes.Write(solids.Select(ToOriginal));
            terrains[0].OriginalParentId = solids[0].Hierarchy.Parent.ID;

            var transaction = new Transaction();
            foreach (var group in solids.GroupBy(x => x.Hierarchy.Parent.ID))
            {
                transaction.Add(new Detatch(group.Key, group.Cast<IMapObject>()));
            }
            // A group or brush entity left with nothing in it goes too
            transaction.AddRange(GroupCleanup.DetatchEmptiedGroups(solids));
            foreach (var t in terrains) transaction.Add(new AddMapData(t));

            ActiveSetId = setId;
            ClearFaces();
            Run(document, transaction);

            var cells = terrains.Sum(x => x.Field.CellCount);
            return $"{terrains.Count} surface(s), {cells} cells." + (skipped > 0 ? $" {skipped} face(s) skipped: only four sided faces work." : "");
        }

        private static OriginalSolid ToOriginal(Solid solid)
        {
            var o = new OriginalSolid { Colour = (solid.Data.GetOne<ObjectColor>()?.Color ?? Color.White).ToArgb() };
            foreach (var f in solid.Faces)
            {
                var t = f.Texture;
                o.Faces.Add(new OriginalFace
                {
                    Texture = t?.Name ?? "",
                    UAxis = t?.UAxis ?? Vector3.UnitX,
                    VAxis = t?.VAxis ?? -Vector3.UnitY,
                    XShift = t?.XShift ?? 0,
                    YShift = t?.YShift ?? 0,
                    XScale = t?.XScale ?? 1,
                    YScale = t?.YScale ?? 1,
                    Rotation = t?.Rotation ?? 0,
                    Vertices = f.Vertices.ToList()
                });
            }
            return o;
        }

        private static Solid FromOriginal(UniqueNumberGenerator generator, OriginalSolid o)
        {
            var solid = new Solid(generator.Next("MapObject"));
            foreach (var of in o.Faces)
            {
                var face = new Face(generator.Next("Face"));
                face.Vertices.AddRange(of.Vertices);
                face.Texture = new Sledge.BspEditor.Primitives.Texture
                {
                    Name = of.Texture,
                    UAxis = of.UAxis,
                    VAxis = of.VAxis,
                    XShift = of.XShift,
                    YShift = of.YShift,
                    XScale = of.XScale,
                    YScale = of.YScale,
                    Rotation = of.Rotation
                };
                solid.Data.Add(face);
            }
            solid.Data.Add(new ObjectColor(Color.FromArgb(o.Colour)));
            return solid;
        }

        #endregion

        #region Textures

        /// <summary>Reads the textures of the front facing faces of the given brushes into per-cell textures of the surface.</summary>
        private static TerrainTextureInfo[] CaptureTextures(Heightfield field, IEnumerable<Solid> solids)
        {
            // Where every grid vertex is, so a brush corner finds its grid position
            (long, long, long) Key(Vector3 p) => ((long) Math.Round(p.X * 16), (long) Math.Round(p.Y * 16), (long) Math.Round(p.Z * 16));
            var lookup = new Dictionary<(long, long, long), (int X, int Y)>();
            for (var y = 0; y <= field.CellsY; y++)
                for (var x = 0; x <= field.CellsX; x++)
                    lookup[Key(field.Vertex(x, y))] = (x, y);

            var cells = new TerrainTextureInfo[field.CellCount];
            var any = false;
            foreach (var face in solids.SelectMany(x => x.Faces))
            {
                if (Vector3.Dot(face.Plane.Normal, field.Normal) <= 0.05f || face.Texture == null || face.Vertices.Count < 3) continue;
                if (string.IsNullOrWhiteSpace(face.Texture.Name)) continue;

                var grid = new List<(int X, int Y)>();
                foreach (var v in face.Vertices)
                {
                    if (lookup.TryGetValue(Key(v), out var g)) grid.Add(g);
                }
                if (grid.Count < 3) continue;

                var info = FaceTexture(face);
                var x0 = grid.Min(g => g.X);
                var x1 = grid.Max(g => g.X);
                var y0 = grid.Min(g => g.Y);
                var y1 = grid.Max(g => g.Y);
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        cells[y * field.CellsX + x] = info.Clone();
                        any = true;
                    }
                }
            }
            return any ? cells : null;
        }

        private static Sledge.BspEditor.Primitives.Texture MakeTexture(string name, TerrainTextureInfo info, Vector3 normal)
        {
            TerrainTextureAxes.Resolve(normal, info, out var u, out var v);
            return new Sledge.BspEditor.Primitives.Texture
            {
                Name = name,
                UAxis = u,
                VAxis = v,
                XShift = info?.XShift ?? 0,
                YShift = info?.YShift ?? 0,
                XScale = info?.XScale ?? 1,
                YScale = info?.YScale ?? 1,
                Rotation = info?.Rotation ?? 0
            };
        }

        #endregion

        #region Sculpting

        private (Vector3 Min, Vector3 Max) BoundsOf(Heightfield f)
        {
            if (_bounds.TryGetValue(f, out var b) && b.Version == f.Version) return (b.Min, b.Max);
            if (_bounds.Count > 500) _bounds.Clear();
            var (min, max) = f.Bounds();
            _bounds[f] = (f.Version, min, max);
            return (min, max);
        }

        private bool Near(Heightfield f, Vector3 at, float radius)
        {
            var (min, max) = BoundsOf(f);
            var nearest = Vector3.Clamp(at, min, max);
            return (nearest - at).LengthSquared() <= radius * radius;
        }

        private TerrainData PickIn3D(MapDocument document, (Vector3 Start, Vector3 End) ray, out Vector3 hit, out float gx, out float gy)
        {
            TerrainData best = null;
            hit = default;
            gx = gy = 0;
            var bestDistance = float.MaxValue;
            foreach (var t in document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield))
            {
                if (!t.Field.RayCast(ray.Start, ray.End, out var h, out var x, out var y)) continue;
                var d = (h - ray.Start).LengthSquared();
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = t;
                hit = h;
                gx = x;
                gy = y;
            }
            return best;
        }

        /// <summary>In the Top view: straight down onto the highest surface at that point.</summary>
        private TerrainData PickIn2D(MapDocument document, Vector3 world, out Vector3 hit, out float gx, out float gy)
        {
            return PickIn3D(document, (new Vector3(world.X, world.Y, 32768), new Vector3(world.X, world.Y, -32768)), out hit, out gx, out gy);
        }

        private SculptMode ModeFor(MouseButtons button)
        {
            return button == MouseButtons.Right ? RightMode : Mode;
        }

        /// <summary>The mode of the running stroke; Ctrl swaps Raise and Lower.</summary>
        private SculptMode EffectiveMode()
        {
            var mode = _stroke == null ? Mode : ModeFor(_stroke.Button);
            if (!KeyboardState.Ctrl) return mode;
            if (mode == SculptMode.Raise) return SculptMode.Lower;
            if (mode == SculptMode.Lower) return SculptMode.Raise;
            return mode;
        }

        private void BeginStroke(MapDocument document, MapViewport viewport, TerrainData terrain, Vector3 hit, float gx, float gy, MouseButtons button)
        {
            ActiveSetId = terrain.SetId;

            _stroke = new Stroke
            {
                Candidates = document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield).ToList(),
                Button = button,
                // Flatten works towards the height where the stroke began
                Target = ModeFor(button) == SculptMode.Set ? SetLevel : (int) Math.Round(terrain.Field.LevelAt(gx, gy)),
                LastStamp = hit,
                Viewport = viewport,
                LastStampTime = _clock.ElapsedMilliseconds
            };
            Stamp(hit);
        }

        private void Stamp(Vector3 at)
        {
            if (_stroke == null) return;

            var mode = EffectiveMode();
            foreach (var t in _stroke.Candidates)
            {
                var f = t.Field;
                if (!Near(f, at, Radius)) continue;

                if (!_stroke.Entries.TryGetValue(t, out var touched))
                {
                    touched = new Touched { Terrain = t, Before = t.Capture(), Residual = new float[f.Levels.Length] };
                    _stroke.Entries[t] = touched;
                }
                if (touched.Residual.Length != f.Levels.Length) continue;

                f.ApplyBrushUnits(at, Math.Max(1f, Radius), mode, Math.Max(0.01f, Strength), Falloff, _stroke.Target, touched.Residual);
            }

            _stroke.LastStamp = at;
            _stroke.LastStampTime = _clock.ElapsedMilliseconds;
        }

        private void EndStroke(MapDocument document)
        {
            var stroke = _stroke;
            _stroke = null;
            if (stroke == null || document == null) return;

            // One undo step for the whole stroke, over every surface it touched
            var transaction = new Transaction();
            foreach (var t in stroke.Entries.Values)
            {
                var after = t.Terrain.Capture();
                if (t.Before.Field.SameShapeAs(after.Field)) continue;
                transaction.Add(new TerrainEdit(t.Terrain, t.Before, after));
            }
            if (!transaction.IsEmpty) Run(document, transaction);
        }

        private void EndMove(MapDocument document)
        {
            var move = _move;
            _move = null;
            if (move == null || document == null) return;

            var transaction = new Transaction();
            for (var i = 0; i < move.Terrains.Count; i++)
            {
                var after = move.Terrains[i].Capture();
                if (move.Before[i].Field.SameShapeAs(after.Field)) continue;
                transaction.Add(new TerrainEdit(move.Terrains[i], move.Before[i], after));
            }
            if (!transaction.IsEmpty) Run(document, transaction);
        }

        #endregion

        #region Actions (sidebar)

        public void SmoothActive(int iterations)
        {
            MutateSet(t => t.Field.Smooth(iterations));
        }

        /// <summary>Live change of the cell size: the corners stay, the heights are taken from the old surface.</summary>
        public string SetCellSize(float size)
        {
            var terrain = Active;
            if (terrain == null) { NewCellSize = size; return null; }
            if (terrain.State != TerrainState.Heightfield) return "Go back to the height grid to change the cell size.";
            if (Math.Abs(terrain.Field.CellSize - size) < 0.001f) return null;

            MutateSet(t =>
            {
                var old = t.Field;
                var next = old.Resample(size);
                t.CellTextures = t.CellTextures == null ? null
                    : TerrainCellTextures.Remap(t.CellTextures, old.CellsX, old.CellsY, next.CellsX, next.CellsY);
                t.Field = next;
            });
            NewCellSize = size;
            return null;
        }

        /// <summary>Live change of the level height: the shape is kept, rounded to the new levels.</summary>
        public string SetLevelHeight(float height)
        {
            var terrain = Active;
            if (terrain == null) { NewLevelHeight = height; return null; }
            if (terrain.State != TerrainState.Heightfield) return "Go back to the height grid to change the level height.";
            if (Math.Abs(terrain.Field.LevelHeight - height) < 0.001f) return null;

            MutateSet(t => t.Field = t.Field.WithLevelHeight(height));
            NewLevelHeight = height;
            return null;
        }

        /// <summary>Moves the active terrain so the corner of its first surface is at the position.</summary>
        public void MoveActiveTo(Vector3 position)
        {
            var terrain = Active;
            if (terrain == null) return;
            var delta = position - terrain.Field.P00;
            if (delta.LengthSquared() < 1e-8f) return;
            MutateSet(t => t.Field.Translate(delta));
        }

        /// <summary>Makes the surfaces of the active set and the other height grids meet exactly where their edges touch (the active set's heights win).</summary>
        public string WeldActive()
        {
            var document = GetDocument();
            var set = ActiveSet().Where(x => x.State == TerrainState.Heightfield).ToList();
            if (document == null || set.Count == 0) return "Select a terrain in heightfield state.";

            var all = set.Concat(document.Map.Data.Get<TerrainData>().Where(x => x.State == TerrainState.Heightfield && !set.Contains(x))).ToList();
            var before = all.Select(x => x.Capture()).ToList();

            var changed = Heightfield.Weld(all.Select(x => x.Field).ToList());
            if (changed == 0) return "Nothing to weld: no surfaces meet along an edge.";

            var transaction = new Transaction();
            for (var i = 0; i < all.Count; i++)
            {
                var after = all[i].Capture();
                if (before[i].Field.SameShapeAs(after.Field)) continue;
                transaction.Add(new TerrainEdit(all[i], before[i], after));
            }
            Run(document, transaction);
            return changed + " vertices welded.";
        }

        public void RenameActive(string name)
        {
            var terrain = Active;
            if (terrain != null && !string.IsNullOrWhiteSpace(name)) terrain.Name = name.Trim();
        }

        /// <summary>Bake the height grid to brushes, or bring the grid back (removing the brushes).</summary>
        public void ToggleState()
        {
            var document = GetDocument();
            var set = ActiveSet();
            if (document == null || set.Count == 0) return;

            if (set.All(x => x.State == TerrainState.Heightfield)) Bake(document, set);
            else BackToHeightfield(document, set.Where(x => x.State == TerrainState.Brushes).ToList());
        }

        private void Bake(MapDocument document, List<TerrainData> set)
        {
            // The brushes of every surface first, so the total can be checked before anything is made
            var plans = new List<(TerrainData Terrain, TerrainTextureInfo[] Cells, List<TerrainBrush> Brushes, List<TerrainBrush> Collision)>();
            foreach (var terrain in set)
            {
                var field = terrain.Field;

                // Cells with different textures are never merged into one brush
                var cells = terrain.CellTextures != null && terrain.CellTextures.Length == field.CellCount ? terrain.CellTextures : null;
                int[] keys = null;
                if (cells != null)
                {
                    var distinct = new List<TerrainTextureInfo>();
                    keys = new int[cells.Length];
                    for (var i = 0; i < cells.Length; i++)
                    {
                        if (cells[i] == null) continue;
                        var k = distinct.FindIndex(x => x.SameAs(cells[i]));
                        if (k < 0)
                        {
                            distinct.Add(cells[i]);
                            k = distinct.Count - 1;
                        }
                        keys[i] = k + 1;
                    }
                }

                var brushes = field.BuildBrushes(terrain.MergeCoplanar, keys);
                var collision = terrain.CollisionEnabled ? field.BuildCollision(terrain.CollisionBlock, terrain.CollisionSampling, terrain.MergeCoplanar) : null;
                plans.Add((terrain, cells, brushes, collision));
            }

            var total = plans.Sum(x => x.Brushes.Count + (x.Collision?.Count ?? 0));
            if (total > 1500)
            {
                var ok = MessageBox.Show($"This makes {total} brushes. That is a lot for a GoldSrc map. Continue?", "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

            var generator = document.Map.NumberGenerator;
            var root = document.Map.Root;
            var transaction = new Transaction();

            foreach (var plan in plans)
            {
                var terrain = plan.Terrain;
                var before = terrain.Capture();

                var group = BuildGroup(generator, plan.Brushes, terrain.Field, plan.Cells, terrain.SurfaceTexture, terrain.HiddenTexture, true);
                transaction.Add(new Attach(root.ID, group));

                long collisionId = 0;
                if (plan.Collision != null && plan.Collision.Count > 0)
                {
                    var cg = BuildGroup(generator, plan.Collision, terrain.Field, null, terrain.ClipTexture, terrain.ClipTexture, false);
                    transaction.Add(new Attach(root.ID, cg));
                    collisionId = cg.ID;
                }

                var after = terrain.Capture();
                after.State = TerrainState.Brushes;
                after.GroupId = group.ID;
                after.CollisionGroupId = collisionId;
                transaction.Add(new TerrainEdit(terrain, before, after));
            }

            Run(document, transaction);
        }

        private void BackToHeightfield(MapDocument document, List<TerrainData> set)
        {
            var any = set.Any(t => FindGroup(document, t.GroupId) != null || FindGroup(document, t.CollisionGroupId) != null);
            if (any)
            {
                var ok = MessageBox.Show(
                    "Back to the height grid removes the brushes of this terrain, including any changes you made to their shape by hand. " +
                    "Their textures are kept. Continue?",
                    "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

            var transaction = new Transaction();
            foreach (var terrain in set)
            {
                var surface = FindGroup(document, terrain.GroupId);
                var collision = FindGroup(document, terrain.CollisionGroupId);

                var before = terrain.Capture();
                var after = terrain.Capture();

                // Remember the textures the brushes have now, so baking again gives the same look
                if (surface != null)
                {
                    var captured = CaptureTextures(terrain.Field, surface.FindAll().OfType<Solid>());
                    if (captured != null)
                    {
                        after.CellTextures = captured;
                        var common = captured.Where(x => x != null).GroupBy(x => x.Name).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                        if (!string.IsNullOrEmpty(common)) after.SurfaceTexture = common;
                    }
                }

                foreach (var g in new[] { surface, collision }.Where(x => x != null && x.Hierarchy.Parent != null))
                {
                    transaction.Add(new Detatch(g.Hierarchy.Parent.ID, g));
                }

                after.State = TerrainState.Heightfield;
                after.GroupId = 0;
                after.CollisionGroupId = 0;
                transaction.Add(new TerrainEdit(terrain, before, after));
            }

            Run(document, transaction);
        }

        /// <summary>Turns the terrain back into the brush(es) it was made from, and removes it from the terrains.</summary>
        public void RevertToBrush()
        {
            var document = GetDocument();
            var set = ActiveSet();
            if (document == null || set.Count == 0) return;

            var ok = MessageBox.Show("Turn this terrain back into the brush it was made from? The terrain is removed.",
                "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;

            var generator = document.Map.NumberGenerator;
            var transaction = new Transaction();

            // The generated brushes, if there are any
            foreach (var t in set)
            {
                foreach (var g in new[] { FindGroup(document, t.GroupId), FindGroup(document, t.CollisionGroupId) }
                    .Where(x => x != null && x.Hierarchy.Parent != null))
                {
                    transaction.Add(new Detatch(g.Hierarchy.Parent.ID, g));
                }
            }

            var primary = set.FirstOrDefault(x => x.ID == x.SetId) ?? set[0];
            var originals = TerrainOriginalBrushes.Read(primary.OriginalBrushes);
            var solids = new List<Solid>();
            if (originals.Count > 0)
            {
                solids.AddRange(originals.Select(x => FromOriginal(generator, x)));
            }
            else
            {
                // A terrain made before the brush was kept: a plain block per surface as high as its highest point
                foreach (var t in set)
                {
                    var flat = t.Field.Clone();
                    for (var i = 0; i < flat.Levels.Length; i++) flat.Levels[i] = flat.MaxLevel;
                    var group = BuildGroup(generator, flat.BuildBrushes(true), flat, null, t.SurfaceTexture, t.SurfaceTexture, true);
                    var made = group.Hierarchy.OfType<Solid>().ToList();
                    foreach (var sol in made) sol.Hierarchy.Parent = null;
                    solids.AddRange(made);
                }
            }

            var parent = FindGroup(document, primary.OriginalParentId);
            if (parent == null || parent is Solid) parent = document.Map.Root;
            transaction.Add(new Attach(parent.ID, solids.Cast<IMapObject>()));
            foreach (var t in set) transaction.Add(new RemoveMapData(t));

            Run(document, transaction);
        }

        /// <summary>
        /// The brushes as a group. Textures are world aligned: every face that faces the same way has the same axes and no
        /// offset (or the ones the original face had), so the texture runs on across all the brushes as if it were one surface.
        /// </summary>
        private static Group BuildGroup(UniqueNumberGenerator generator, List<TerrainBrush> brushes, Heightfield field,
            TerrainTextureInfo[] cellTextures, string topTexture, string otherTexture, bool isSurface)
        {
            var group = new Group(generator.Next("MapObject"));
            var colour = Colour.GetRandomBrushColour();

            foreach (var brush in brushes)
            {
                // The texture settings of the cell the top face starts in
                TerrainTextureInfo cell = null;
                if (isSurface && cellTextures != null && brush.CellX0 >= 0)
                {
                    cell = cellTextures[brush.CellY0 * field.CellsX + brush.CellX0];
                }

                var solid = new Solid(generator.Next("MapObject"));
                foreach (var f in brush.Faces)
                {
                    var face = new Face(generator.Next("Face"));
                    face.Vertices.AddRange(f.Vertices);

                    var normal = new Plane(f.Vertices[0], f.Vertices[1], f.Vertices[2]).Normal;
                    var top = f.Kind == TerrainFaceKind.Top;
                    var name = top ? (cell != null && !string.IsNullOrEmpty(cell.Name) ? cell.Name : topTexture) : otherTexture;
                    face.Texture = MakeTexture(name, top ? cell : null, normal);

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

            var faces = FaceCount();
            var set = ActiveSet();
            if (set.Count == 0)
            {
                return faces > 0
                    ? faces + (faces == 1 ? " face" : " faces") + " picked. Press \"Terrain from selected faces\"."
                    : "No terrain yet. Click a face in the 3D view (Ctrl adds more), or select brushes with the Select tool, then press \"Terrain from selected faces\".";
            }

            var primary = Active;
            var cells = set.Sum(x => x.Field.CellCount);
            var key = set.Sum(x => (long) x.Field.Version * 31 + x.ID) + (primary.MergeCoplanar ? 1 : 0);
            if (_countKey != key)
            {
                _countKey = key;
                _countBrushes = set.Sum(x => x.Field.BuildBrushes(x.MergeCoplanar).Count);
            }

            var text = $"{primary.Name}: {set.Count} surface(s), {cells} cells. Makes {_countBrushes} brushes";
            if (primary.CollisionEnabled) text += $" + {set.Sum(x => x.Field.BuildCollision(x.CollisionBlock, x.CollisionSampling, x.MergeCoplanar).Count)} collision";
            text += ".";

            if (set.Any(x => x.State == TerrainState.Brushes)) text += " Now brushes: press Edit heightfield to sculpt again.";
            else
            {
                if (primary.CollisionEnabled) text += " Tie the visible surface to func_illusionary so only the CLIP blocks are solid.";
                text += " Left/right drag sculpts along the face normal, Ctrl+wheel sets the radius, Shift+drag in a 2D view moves it.";
            }
            return text;
        }

        #endregion

        #region Viewport interaction

        private static bool IsSculptButton(MouseButtons b)
        {
            return b == MouseButtons.Left || b == MouseButtons.Right;
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (!IsSculptButton(e.Button)) return;

            var ray = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            _lastRay = ray;

            var terrain = PickIn3D(document, ray, out var hit, out var gx, out var gy);
            if (terrain == null) return;

            BeginStroke(document, viewport, terrain, hit, gx, gy, e.Button);
            e.Handled = true;
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            var ray = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
            _lastRay = ray;

            var terrain = PickIn3D(document, ray, out var hit, out _, out _);
            _hoverTerrain = terrain;
            _hover = terrain != null ? hit : (Vector3?) null;
            if (terrain == null || _stroke == null || _stroke.Viewport != viewport) return;

            MoveStroke(hit);
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            if (_stroke != null && e.Button == _stroke.Button) EndStroke(document);
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            EndStroke(document);
            _hover = null;
            _lastRay = null;
        }

        /// <summary>Ctrl + mouse wheel changes the brush radius (and doesn't move the camera).</summary>
        private void WheelRadius(ViewportEvent e)
        {
            if (!KeyboardState.Ctrl || e.Delta == 0) return;

            var factor = e.Delta > 0 ? 1.1f : 1f / 1.1f;
            Radius = (float) Math.Round(Math.Max(8f, Math.Min(4096f, Radius * factor)));
            e.Handled = true;
        }

        protected override void MouseWheel(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
        {
            WheelRadius(e);
        }

        protected override void MouseWheel(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            WheelRadius(e);
        }

        protected override void MouseDown(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            var world = camera.ScreenToWorld(e.X, e.Y);

            // Shift + drag moves the active terrain, in any 2D view
            if (e.Button == MouseButtons.Left && KeyboardState.Shift)
            {
                var set = ActiveSet().Where(x => x.State == TerrainState.Heightfield).ToList();
                if (set.Count == 0) return;

                var primary = set.FirstOrDefault(x => x.ID == x.SetId) ?? set[0];
                _move = new MoveDrag
                {
                    Terrains = set,
                    Before = set.Select(x => x.Capture()).ToList(),
                    StartWorld = world,
                    OriginStart = primary.Field.P00
                };
                e.Handled = true;
                return;
            }

            if (!IsSculptButton(e.Button)) return;
            if (camera.ViewType != OrthographicCamera.OrthographicType.Top) return;

            var terrain = PickIn2D(document, world, out var hit, out var gx, out var gy);
            if (terrain == null) return;

            BeginStroke(document, viewport, terrain, hit, gx, gy, e.Button);
            e.Handled = true;
        }

        protected override void MouseMove(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            var world = camera.ScreenToWorld(e.X, e.Y);

            if (_move != null)
            {
                var delta = world - _move.StartWorld;
                var snapped = SnapIfNeeded(_move.OriginStart + delta);

                // Only the axes this view shows move; the others stay where they were
                var target = new Vector3(
                    Math.Abs(delta.X) > 0.0001f ? snapped.X : _move.OriginStart.X,
                    Math.Abs(delta.Y) > 0.0001f ? snapped.Y : _move.OriginStart.Y,
                    Math.Abs(delta.Z) > 0.0001f ? snapped.Z : _move.OriginStart.Z);

                var current = _move.Terrains[0].Field.P00;
                foreach (var t in _move.Terrains)
                {
                    // The first surface is the reference; the others move by the same amount
                    t.Field.Translate(target - current);
                }
                return;
            }

            if (camera.ViewType != OrthographicCamera.OrthographicType.Top)
            {
                _hover = null;
                return;
            }

            var terrain = PickIn2D(document, world, out var h, out _, out _);
            _hoverTerrain = terrain;
            _hover = terrain != null ? h : (Vector3?) null;
            if (terrain != null && _stroke != null && _stroke.Viewport == viewport) MoveStroke(h);
        }

        protected override void MouseUp(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            if (_move != null && e.Button == MouseButtons.Left)
            {
                EndMove(document);
                return;
            }

            if (_stroke != null && e.Button == _stroke.Button) EndStroke(document);
        }

        protected override void MouseLeave(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
        {
            EndStroke(document);
            EndMove(document);
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
            // Holding still keeps working on the surface, like an airbrush
            if (_stroke == null || _stroke.Viewport != viewport || !_lastRay.HasValue) return;
            if (_clock.ElapsedMilliseconds - _stroke.LastStampTime < StampIntervalMs) return;
            if (EffectiveMode() == SculptMode.Set) return;

            var terrain = PickIn3D(document, _lastRay.Value, out var hit, out _, out _);
            if (terrain != null) Stamp(hit);
        }

        public override System.Threading.Tasks.Task PreToolDeselect()
        {
            var document = GetDocument();
            EndStroke(document);
            EndMove(document);
            ClearFaces();
            return base.PreToolDeselect();
        }

        #endregion

        #region Rendering

        private static readonly Vector4 RingColour = new Vector4(1f, 0.9f, 0.1f, 1f);
        private static readonly Vector4 OutlineColour = new Vector4(1f, 1f, 1f, 1f);
        private static readonly Vector4 FaceColour = new Vector4(1f, 0.85f, 0.2f, 1f);

        protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
        {
            base.Render(document, builder, resourceCollector);

            // The picked faces
            List<PickedFace> faces;
            lock (_lock) faces = _faces.ToList();
            if (faces.Count > 0) RenderFaces(builder, faces);

            var vertices = new List<VertexStandard>();
            var indices = new List<uint>();

            void Line(Vector3 a, Vector3 b, Vector4 colour)
            {
                var o = (uint) vertices.Count;
                vertices.Add(Vert(a, colour, Vector3.UnitZ));
                vertices.Add(Vert(b, colour, Vector3.UnitZ));
                indices.Add(o);
                indices.Add(o + 1);
            }

            // The outline of the active set's surfaces
            foreach (var t in ActiveSet().Where(x => x.State == TerrainState.Heightfield))
            {
                var f = t.Field;
                var lift = f.Normal * 0.5f;
                var p = new[] { f.P00 + lift, f.P10 + lift, f.P11 + lift, f.P01 + lift };
                for (var i = 0; i < 4; i++) Line(p[i], p[(i + 1) % 4], OutlineColour);
            }

            // The brush ring, in the plane of the surface under the cursor
            if (_hover.HasValue && _hoverTerrain != null)
            {
                var normal = _hoverTerrain.Field.Normal;
                var a = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
                var b = Vector3.Cross(normal, a);

                const int sides = 48;
                var previous = default(Vector3);
                for (var i = 0; i <= sides; i++)
                {
                    var angle = i * 2f * MathF.PI / sides;
                    var point = _hover.Value + (a * MathF.Cos(angle) + b * MathF.Sin(angle)) * Radius + normal * 0.5f;
                    if (i > 0) Line(previous, point, RingColour);
                    previous = point;
                }
            }

            if (vertices.Count == 0) return;

            var groups = new[] { new BufferGroup(PipelineType.Wireframe, CameraType.Both, 0, (uint) indices.Count) };
            builder.Append(vertices, indices, groups);
        }

        /// <summary>The picked faces: a translucent fill in 3D and an outline in every view.</summary>
        private static void RenderFaces(BufferBuilder builder, List<PickedFace> faces)
        {
            var vertices = new List<VertexStandard>();
            var indices = new List<uint>();
            var groups = new List<BufferGroup>();

            foreach (var picked in faces)
            {
                var face = picked.Face;
                var normal = face.Plane.Normal;
                var lift = normal * 0.3f;
                var start = (uint) indices.Count;
                var o = (uint) vertices.Count;

                foreach (var v in face.Vertices)
                {
                    vertices.Add(new VertexStandard
                    {
                        Position = v + lift,
                        Colour = Vector4.One,
                        Normal = normal,
                        Texture = Vector2.Zero,
                        Tint = new Vector4(FaceColour.X, FaceColour.Y, FaceColour.Z, 0.45f),
                        Flags = VertexFlags.FlatColour
                    });
                }
                for (var i = 1; i + 1 < face.Vertices.Count; i++)
                {
                    indices.Add(o);
                    indices.Add(o + (uint) i);
                    indices.Add(o + (uint) i + 1);
                }

                var centre = face.Vertices.Aggregate(Vector3.Zero, (a, b) => a + b) / face.Vertices.Count;
                groups.Add(new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, centre, start, (uint) indices.Count - start));
            }

            var lineStart = (uint) indices.Count;
            foreach (var picked in faces)
            {
                var face = picked.Face;
                var lift = face.Plane.Normal * 0.3f;
                var o = (uint) vertices.Count;
                foreach (var v in face.Vertices) vertices.Add(Vert(v + lift, FaceColour, face.Plane.Normal));
                for (var i = 0; i < face.Vertices.Count; i++)
                {
                    indices.Add(o + (uint) i);
                    indices.Add(o + (uint) ((i + 1) % face.Vertices.Count));
                }
            }
            groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, lineStart, (uint) indices.Count - lineStart));

            builder.Append(vertices, indices, groups);
        }

        private static VertexStandard Vert(Vector3 p, Vector4 colour, Vector3 normal)
        {
            return new VertexStandard
            {
                Position = p,
                Colour = colour,
                Normal = normal,
                Texture = Vector2.Zero,
                Tint = Vector4.One
            };
        }

        #endregion
    }
}
