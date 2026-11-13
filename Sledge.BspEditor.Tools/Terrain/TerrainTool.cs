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
    /// by a height grid that follows its top surface (and is kept, so "Revert to original brush" can put it back).
    /// Sculpt by dragging over a terrain in the 3D view (or the Top view): the left and right mouse buttons each have a mode,
    /// Ctrl swaps Raise and Lower, Ctrl+wheel changes the radius, and Shift+drag in a 2D view moves the height grid.
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

        // Settings for a terrain made from a brush (the sidebar shows the active terrain's own when there is one)
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
            public MouseButtons Button;
            public float[] Residual;
        }

        private sealed class MoveDrag
        {
            public TerrainData Terrain;
            public TerrainData.Snapshot Before;
            public Vector3 StartWorld;
            public Vector3 OriginStart;
        }

        private Stroke _stroke;
        private MoveDrag _move;
        private Vector3? _hover;
        private TerrainData _hoverTerrain;
        private (Vector3 Start, Vector3 End)? _lastRay;
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
        /// The brushes are replaced by the terrain (one undo step brings them back), and are kept in the terrain so
        /// "Revert to original brush" can put them back. Returns a message.
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
                Field = Heightfield.FromSurface(min, max, cell, NewLevelHeight, Top),
                OriginalBrushes = TerrainOriginalBrushes.Write(solids.Select(ToOriginal)),
                OriginalParentId = solids[0].Hierarchy.Parent.ID
            };

            // The textures of the top faces carry over, cell by cell
            terrain.CellTextures = CaptureTextures(terrain.Field, solids);

            var transaction = new Transaction();
            foreach (var group in solids.GroupBy(x => x.Hierarchy.Parent.ID))
            {
                transaction.Add(new Detatch(group.Key, group.Cast<IMapObject>()));
            }
            // A group or brush entity left with nothing in it goes too
            transaction.AddRange(GroupCleanup.DetatchEmptiedGroups(solids));
            transaction.Add(new AddMapData(terrain));

            ActiveId = terrain.ID;
            Run(document, transaction);
            return $"{terrain.Name}: {terrain.Field.CellsX} x {terrain.Field.CellsY} cells.";
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

        #region Textures

        /// <summary>The cells a top face covers: a triangle belongs to the cell its middle is in, larger faces to the cells whose middle they hold.</summary>
        private static IEnumerable<int> CellsOf(Heightfield f, List<Vector3> polygon)
        {
            if (polygon.Count == 3)
            {
                var cx = (polygon.Sum(v => v.X) / 3 - f.Origin.X) / f.CellSize;
                var cy = (polygon.Sum(v => v.Y) / 3 - f.Origin.Y) / f.CellSize;
                var x = (int) Math.Floor(cx);
                var y = (int) Math.Floor(cy);
                if (x >= 0 && y >= 0 && x < f.CellsX && y < f.CellsY) yield return y * f.CellsX + x;
                yield break;
            }

            var minX = Math.Max(0, (int) Math.Floor((polygon.Min(v => v.X) - f.Origin.X) / f.CellSize));
            var maxX = Math.Min(f.CellsX - 1, (int) Math.Ceiling((polygon.Max(v => v.X) - f.Origin.X) / f.CellSize));
            var minY = Math.Max(0, (int) Math.Floor((polygon.Min(v => v.Y) - f.Origin.Y) / f.CellSize));
            var maxY = Math.Min(f.CellsY - 1, (int) Math.Ceiling((polygon.Max(v => v.Y) - f.Origin.Y) / f.CellSize));
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var mx = f.Origin.X + (x + 0.5f) * f.CellSize;
                    var my = f.Origin.Y + (y + 0.5f) * f.CellSize;
                    if (Inside(polygon, mx, my)) yield return y * f.CellsX + x;
                }
            }
        }

        /// <summary>Reads the textures of the upward-facing faces of the given brushes into per-cell textures.</summary>
        private static TerrainTextureInfo[] CaptureTextures(Heightfield field, IEnumerable<Solid> solids)
        {
            var cells = new TerrainTextureInfo[field.CellCount];
            var any = false;
            foreach (var face in solids.SelectMany(x => x.Faces))
            {
                if (face.Plane.Normal.Z <= 0.001f || face.Texture == null || face.Vertices.Count < 3) continue;
                if (string.IsNullOrWhiteSpace(face.Texture.Name)) continue;

                var t = face.Texture;
                var info = new TerrainTextureInfo
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

                foreach (var c in CellsOf(field, face.Vertices.ToList()))
                {
                    cells[c] = info.Clone();
                    any = true;
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

        /// <summary>Turns the terrain back into the brush(es) it was made from, and removes it from the terrains.</summary>
        public void RevertToBrush()
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null) return;

            var ok = MessageBox.Show("Turn this terrain back into the brush it was made from? The terrain is removed.",
                "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;

            var generator = document.Map.NumberGenerator;
            var transaction = new Transaction();

            // The generated brushes, if there are any
            foreach (var g in new[] { FindGroup(document, terrain.GroupId), FindGroup(document, terrain.CollisionGroupId) }
                .Where(x => x != null && x.Hierarchy.Parent != null))
            {
                transaction.Add(new Detatch(g.Hierarchy.Parent.ID, g));
            }

            var originals = TerrainOriginalBrushes.Read(terrain.OriginalBrushes);
            var solids = new List<Solid>();
            if (originals.Count > 0)
            {
                solids.AddRange(originals.Select(x => FromOriginal(generator, x)));
            }
            else
            {
                // A terrain made before the brush was kept: a plain block as high as the terrain's highest point
                var flat = terrain.Field.Clone();
                for (var i = 0; i < flat.Levels.Length; i++) flat.Levels[i] = flat.MaxLevel;
                var group = BuildGroup(generator, flat.BuildBrushes(true), flat, null, terrain.SurfaceTexture, terrain.SurfaceTexture, true);
                solids.AddRange(group.Hierarchy.OfType<Solid>());
                foreach (var sol in solids) sol.Hierarchy.Parent = null;
            }

            var parent = FindGroup(document, terrain.OriginalParentId);
            if (parent == null || parent is Solid) parent = document.Map.Root;
            transaction.Add(new Attach(parent.ID, solids.Cast<IMapObject>()));
            transaction.Add(new RemoveMapData(terrain));

            Run(document, transaction);
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

        private void BeginStroke(MapDocument document, MapViewport viewport, TerrainData terrain, Vector3 hit, MouseButtons button)
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
                Button = button,
                Residual = new float[f.Levels.Length],
                // Flatten works towards the height where the stroke began
                TargetLevel = ModeFor(button) == SculptMode.Set ? SetLevel : f.GetLevel(vx, vy),
                LastStamp = hit,
                Viewport = viewport,
                LastStampTime = _clock.ElapsedMilliseconds
            };
            Stamp(hit);
        }

        private void Stamp(Vector3 at)
        {
            if (_stroke == null) return;
            var f = _stroke.Terrain.Field;
            if (_stroke.Residual.Length != f.Levels.Length) return;

            f.ApplyBrushUnits(at.X, at.Y, Math.Max(1f, Radius), EffectiveMode(), Math.Max(0.01f, Strength), Falloff, _stroke.TargetLevel, _stroke.Residual);
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

        private void EndMove(MapDocument document)
        {
            var move = _move;
            _move = null;
            if (move == null || document == null) return;

            var after = move.Terrain.Capture();
            if (move.Before.Field.Origin == after.Field.Origin) return;
            Run(document, new TerrainEdit(move.Terrain, move.Before, after));
        }

        #endregion

        #region Actions (sidebar)

        /// <summary>Changes the active terrain (heightfield state) and records one undo step.</summary>
        private bool Mutate(Action<TerrainData> change)
        {
            var document = GetDocument();
            var terrain = Active;
            if (document == null || terrain == null || terrain.State != TerrainState.Heightfield) return false;

            var before = terrain.Capture();
            change(terrain);
            terrain.Field.Touch();
            Run(document, new TerrainEdit(terrain, before, terrain.Capture()));
            return true;
        }

        public void SmoothActive(int iterations)
        {
            Mutate(t => t.Field.Smooth(iterations));
        }

        /// <summary>Live change of the cell size: the area stays, the heights are taken from the old surface.</summary>
        public string SetCellSize(float size)
        {
            var terrain = Active;
            if (terrain == null) { NewCellSize = size; return null; }
            if (terrain.State != TerrainState.Heightfield) return "Go back to the height grid to change the cell size.";
            if (Math.Abs(terrain.Field.CellSize - size) < 0.001f) return null;

            var f = terrain.Field;
            if (f.CellsX * f.CellSize / size > 256 || f.CellsY * f.CellSize / size > 256) return "That would be more than 256 x 256 cells.";

            Mutate(t =>
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

            Mutate(t => t.Field = t.Field.WithLevelHeight(height));
            NewLevelHeight = height;
            return null;
        }

        /// <summary>Moves the active height grid to a new corner position.</summary>
        public void MoveActiveTo(Vector3 origin)
        {
            var terrain = Active;
            if (terrain == null || terrain.Field.Origin == origin) return;
            Mutate(t => t.Field.Origin = origin);
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

            var group = BuildGroup(generator, brushes, field, cells, terrain.SurfaceTexture, terrain.HiddenTexture, true);
            transaction.Add(new Attach(root.ID, group));

            long collisionId = 0;
            if (collision != null && collision.Count > 0)
            {
                var cg = BuildGroup(generator, collision, field, null, terrain.ClipTexture, terrain.ClipTexture, false);
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
                    "Back to the height grid removes the brushes of this terrain, including any changes you made to their shape by hand. " +
                    "Their textures are kept. Continue?",
                    "Terrain Tool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ok != DialogResult.Yes) return;
            }

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

            var transaction = new Transaction();
            foreach (var g in new[] { surface, collision }.Where(x => x != null && x.Hierarchy.Parent != null))
            {
                transaction.Add(new Detatch(g.Hierarchy.Parent.ID, g));
            }

            after.State = TerrainState.Heightfield;
            after.GroupId = 0;
            after.CollisionGroupId = 0;
            transaction.Add(new TerrainEdit(terrain, before, after));

            Run(document, transaction);
        }

        /// <summary>
        /// The brushes as a group. Textures are world aligned: every face that faces the same way has the same axes and no
        /// offset, so the texture runs on across all the brushes as if it were one surface.
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
            if (terrain.State == TerrainState.Heightfield) text += " Left/right drag sculpts, Ctrl+wheel sets the radius, Shift+drag in a 2D view moves it.";
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

            var terrain = PickIn3D(document, ray, out var hit);
            if (terrain == null) return;

            BeginStroke(document, viewport, terrain, hit, e.Button);
            e.Handled = true;
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

            // Shift + drag moves the active height grid, in any 2D view
            if (e.Button == MouseButtons.Left && KeyboardState.Shift)
            {
                var active = Active;
                if (active == null || active.State != TerrainState.Heightfield) return;

                _move = new MoveDrag { Terrain = active, Before = active.Capture(), StartWorld = world, OriginStart = active.Field.Origin };
                e.Handled = true;
                return;
            }

            if (!IsSculptButton(e.Button)) return;
            if (camera.ViewType != OrthographicCamera.OrthographicType.Top) return;

            var terrain = PickIn2D(document, world, out var hit);
            if (terrain == null) return;

            BeginStroke(document, viewport, terrain, hit, e.Button);
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
                var origin = new Vector3(
                    Math.Abs(delta.X) > 0.0001f ? snapped.X : _move.OriginStart.X,
                    Math.Abs(delta.Y) > 0.0001f ? snapped.Y : _move.OriginStart.Y,
                    Math.Abs(delta.Z) > 0.0001f ? snapped.Z : _move.OriginStart.Z);

                if (_move.Terrain.Field.Origin != origin)
                {
                    _move.Terrain.Field.Origin = origin;
                    _move.Terrain.Field.Touch();
                }
                return;
            }

            if (camera.ViewType != OrthographicCamera.OrthographicType.Top)
            {
                _hover = null;
                return;
            }

            var terrain = PickIn2D(document, world, out var h);
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

            var mode = EffectiveMode();
            if (mode == SculptMode.Set) return;

            if (_stroke.Terrain.Field.RayCast(_lastRay.Value.Start, _lastRay.Value.End, out var hit)) Stamp(hit);
        }

        public override System.Threading.Tasks.Task PreToolDeselect()
        {
            var document = GetDocument();
            EndStroke(document);
            EndMove(document);
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
