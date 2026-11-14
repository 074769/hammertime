using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Providers.Processors;
using Sledge.BspEditor.Storage;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Terrain
{
    /// <summary>
    /// Remembers terrains for map formats that can't store them (RMF, MAP, ...) in the map's Hammertime sidecar file.
    /// The height grid and the options are written when the map is saved and read back when it is opened.
    /// Those formats don't keep object IDs, so the groups of generated brushes are found again by their bounding box.
    /// The native format (.smf) stores terrains itself, so no sidecar section is used for it.
    /// </summary>
    public static class TerrainSidecar
    {
        public const string SectionName = "Terrain";
        private const float Tolerance = 0.5f;

        private static readonly string[] NativeExtensions = { ".smf" };

        public class Entry
        {
            public long ID { get; set; }
            public long SetId { get; set; }
            public string Name { get; set; }
            public int State { get; set; }
            public int CellsX { get; set; }
            public int CellsY { get; set; }
            public float CellSize { get; set; }
            public float LevelHeight { get; set; }
            public int ThicknessLevels { get; set; }
            public float[] Origin { get; set; }
            public string Corners { get; set; }
            public string Normal { get; set; }
            public string Dirs { get; set; }
            public int[] Levels { get; set; }
            public string SurfaceTexture { get; set; }
            public string HiddenTexture { get; set; }
            public bool MergeCoplanar { get; set; }
            public bool CollisionEnabled { get; set; }
            public int CollisionBlock { get; set; }
            public int CollisionSampling { get; set; }
            public string ClipTexture { get; set; }
            public string CellTextures { get; set; }
            public string OriginalBrushes { get; set; }
            public long OriginalParentId { get; set; }
            public float[] GroupBox { get; set; }
            public float[] CollisionGroupBox { get; set; }
        }

        public class FileData
        {
            public int Version { get; set; } = 1;
            public List<Entry> Terrains { get; set; } = new List<Entry>();
        }

        private static bool IsNative(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName) ?? "";
            return NativeExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }

        private static float[] BoxOf(MapDocument document, long groupId)
        {
            if (groupId == 0) return null;
            var o = document.Map.Root.FindByID(groupId);
            if (o == null || !o.Hierarchy.HasChildren) return null;
            var b = o.BoundingBox;
            return new[] { b.Start.X, b.Start.Y, b.Start.Z, b.End.X, b.End.Y, b.End.Z };
        }

        /// <summary>Write (or remove) the terrain section for a document that was just saved to its file name.</summary>
        public static void Save(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            try
            {
                var terrains = document.Map.Data.Get<TerrainData>().ToList();
                if (terrains.Count == 0 || IsNative(fileName))
                {
                    HammertimeSidecar.Remove(fileName, SectionName);
                    return;
                }

                var data = new FileData();
                foreach (var t in terrains)
                {
                    data.Terrains.Add(new Entry
                    {
                        ID = t.ID,
                        SetId = t.SetId,
                        Name = t.Name,
                        State = (int) t.State,
                        CellsX = t.Field.CellsX,
                        CellsY = t.Field.CellsY,
                        CellSize = t.Field.CellSize,
                        LevelHeight = t.Field.LevelHeight,
                        ThicknessLevels = t.Field.ThicknessLevels,
                        Corners = string.Join(";", new[] { t.Field.P00, t.Field.P10, t.Field.P01, t.Field.P11 }.Select(TerrainText.Write)),
                        Normal = TerrainText.Write(t.Field.Normal),
                        Dirs = TerrainText.Write(t.Field.Dirs),
                        Levels = (int[]) t.Field.Levels.Clone(),
                        SurfaceTexture = t.SurfaceTexture,
                        HiddenTexture = t.HiddenTexture,
                        MergeCoplanar = t.MergeCoplanar,
                        CollisionEnabled = t.CollisionEnabled,
                        CollisionBlock = t.CollisionBlock,
                        CollisionSampling = (int) t.CollisionSampling,
                        ClipTexture = t.ClipTexture,
                        CellTextures = TerrainCellTextures.Write(t.CellTextures),
                        OriginalBrushes = t.OriginalBrushes,
                        OriginalParentId = t.OriginalParentId,
                        GroupBox = t.State == TerrainState.Brushes ? BoxOf(document, t.GroupId) : null,
                        CollisionGroupBox = t.State == TerrainState.Brushes ? BoxOf(document, t.CollisionGroupId) : null
                    });
                }

                HammertimeSidecar.Set(fileName, SectionName, data);
            }
            catch (Exception)
            {
                // Remembering terrains must never get in the way of saving the map
            }
        }

        /// <summary>Restore the terrains for a document that was just loaded from its file name.</summary>
        public static void Load(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            // The map format already gave us its terrains (native format)
            if (document.Map.Data.Get<TerrainData>().Any()) return;

            var data = HammertimeSidecar.Get<FileData>(fileName, SectionName);
            if (data?.Terrains == null || data.Terrains.Count == 0) return;

            var groups = document.Map.Root.FindAll().OfType<Group>().Where(x => x.Hierarchy.HasChildren).ToList();
            var used = new HashSet<long>();

            foreach (var e in data.Terrains)
            {
                if (e?.Levels == null) continue;
                var cells = (e.CellsX + 1) * (e.CellsY + 1);
                if (e.CellsX < 1 || e.CellsY < 1 || e.Levels.Length != cells) continue;

                var t = new TerrainData
                {
                    ID = e.ID,
                    SetId = e.SetId == 0 ? e.ID : e.SetId,
                    Name = e.Name ?? "Terrain",
                    State = (TerrainState) e.State,
                    SurfaceTexture = e.SurfaceTexture ?? "GROUND",
                    HiddenTexture = e.HiddenTexture ?? "NULL",
                    MergeCoplanar = e.MergeCoplanar,
                    CollisionEnabled = e.CollisionEnabled,
                    CollisionBlock = Math.Max(1, e.CollisionBlock),
                    CollisionSampling = (CollisionSampling) e.CollisionSampling,
                    ClipTexture = e.ClipTexture ?? "CLIP",
                    CellTextures = TerrainCellTextures.Read(e.CellTextures, e.CellsX * e.CellsY),
                    OriginalBrushes = e.OriginalBrushes ?? "",
                    OriginalParentId = e.OriginalParentId
                };

                var field = new Heightfield
                {
                    CellsX = e.CellsX,
                    CellsY = e.CellsY,
                    CellSize = e.CellSize,
                    LevelHeight = e.LevelHeight,
                    ThicknessLevels = Math.Max(1, e.ThicknessLevels),
                    Levels = (int[]) e.Levels.Clone()
                };

                if (!string.IsNullOrEmpty(e.Corners))
                {
                    var c = e.Corners.Split(';').Select(TerrainText.ReadVector).ToArray();
                    if (c.Length != 4) continue;
                    field.P00 = c[0];
                    field.P10 = c[1];
                    field.P01 = c[2];
                    field.P11 = c[3];
                    field.Normal = TerrainText.ReadVector(e.Normal);
                    if (field.Normal.LengthSquared() < 1e-8f) field.Normal = Vector3.UnitZ;
                    field.Dirs = TerrainText.ReadList(e.Dirs, cells);
                }
                else if (e.Origin != null && e.Origin.Length == 3)
                {
                    // A flat terrain saved by an earlier version
                    var origin = new Vector3(e.Origin[0], e.Origin[1], e.Origin[2]);
                    var w = e.CellsX * e.CellSize;
                    var h = e.CellsY * e.CellSize;
                    field.P00 = origin;
                    field.P10 = origin + new Vector3(w, 0, 0);
                    field.P01 = origin + new Vector3(0, h, 0);
                    field.P11 = origin + new Vector3(w, h, 0);
                }
                else continue;
                t.Field = field;

                if (t.State == TerrainState.Brushes)
                {
                    var surface = Find(groups, used, e.GroupBox);
                    if (surface == null)
                    {
                        // The brushes are gone (the map was edited elsewhere): back to the editable height grid
                        t.State = TerrainState.Heightfield;
                    }
                    else
                    {
                        used.Add(surface.ID);
                        t.GroupId = surface.ID;
                        var collision = Find(groups, used, e.CollisionGroupBox);
                        if (collision != null)
                        {
                            used.Add(collision.ID);
                            t.CollisionGroupId = collision.ID;
                        }
                    }
                }

                document.Map.Data.Add(t);
            }
        }

        private static Group Find(List<Group> groups, HashSet<long> used, float[] box)
        {
            if (box == null || box.Length != 6) return null;

            Group best = null;
            var bestDiff = float.MaxValue;
            foreach (var g in groups)
            {
                if (used.Contains(g.ID)) continue;
                var b = g.BoundingBox;
                var diff = new[]
                {
                    Math.Abs(b.Start.X - box[0]), Math.Abs(b.Start.Y - box[1]), Math.Abs(b.Start.Z - box[2]),
                    Math.Abs(b.End.X - box[3]), Math.Abs(b.End.Y - box[4]), Math.Abs(b.End.Z - box[5])
                }.Max();
                if (diff > Tolerance || diff >= bestDiff) continue;
                best = g;
                bestDiff = diff;
            }
            return best;
        }
    }

    /// <summary>Brings terrains back when a map is opened from a format that can't store them. Runs after the link restore ("F").</summary>
    [Export(typeof(IBspSourceProcessor))]
    public class RestoreTerrain : IBspSourceProcessor
    {
        public string OrderHint => "G";

        public Task AfterLoad(MapDocument document)
        {
            TerrainSidecar.Load(document);
            return Task.FromResult(0);
        }

        public Task BeforeSave(MapDocument document)
        {
            return Task.FromResult(0);
        }
    }

    /// <summary>Writes the terrain section whenever a map is saved to its file (not for exports or compiles).</summary>
    [Export(typeof(IInitialiseHook))]
    public class SaveTerrainHook : IInitialiseHook
    {
        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Saved", Saved);
            return Task.CompletedTask;
        }

        private Task Saved(IDocument doc)
        {
            if (doc is MapDocument md) TerrainSidecar.Save(md);
            return Task.CompletedTask;
        }
    }
}
