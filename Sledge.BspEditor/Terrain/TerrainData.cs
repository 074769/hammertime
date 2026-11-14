using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Terrain
{
    public enum TerrainState
    {
        /// <summary>Only the height grid exists (drawn as a preview); there are no brushes in the map.</summary>
        Heightfield = 0,

        /// <summary>The generated brushes are in the map; the height grid is kept so it can be switched back.</summary>
        Brushes = 1
    }

    /// <summary>
    /// A terrain: a height grid that is sculpted, and (once baked) the brushes made from it.
    /// Stored as map data, so the native format keeps it. The other formats use <see cref="TerrainSidecar"/>.
    /// </summary>
    public class TerrainData : IMapData
    {
        // The preview is drawn every frame by its own renderable, so a change here doesn't rebuild the scene
        public bool AffectsRendering => false;

        public long ID { get; set; }

        /// <summary>
        /// Surfaces made together (from several faces) share a set: they are sculpted, baked and reverted together.
        /// The ID of the first one. A single surface is its own set.
        /// </summary>
        public long SetId { get; set; }

        public string Name { get; set; } = "Terrain";
        public TerrainState State { get; set; } = TerrainState.Heightfield;
        public Heightfield Field { get; set; } = new Heightfield();

        /// <summary>Texture of the top (walkable) faces.</summary>
        public string SurfaceTexture { get; set; } = "GROUND";

        /// <summary>Texture of the sides and bottoms, which are never seen.</summary>
        public string HiddenTexture { get; set; } = "NULL";

        public bool MergeCoplanar { get; set; } = true;

        public bool CollisionEnabled { get; set; }

        /// <summary>Collision block size in cells.</summary>
        public int CollisionBlock { get; set; } = 4;

        public CollisionSampling CollisionSampling { get; set; } = CollisionSampling.Lowest;
        public string ClipTexture { get; set; } = "CLIP";

        /// <summary>
        /// Texture of each top face, row by row (null where the surface texture is used). Filled in when the terrain goes back from
        /// brushes to a height grid, so textures put on the brushes by hand are not lost, and used again when it is baked.
        /// </summary>
        public TerrainTextureInfo[] CellTextures { get; set; }

        /// <summary>The brushes this terrain was made from (see <see cref="TerrainOriginalBrushes"/>), so it can be turned back into them.</summary>
        public string OriginalBrushes { get; set; } = "";

        /// <summary>The object the original brushes were in, so they go back there.</summary>
        public long OriginalParentId { get; set; }

        /// <summary>Object ID of the group holding the generated brushes (0 when there are none).</summary>
        public long GroupId { get; set; }

        /// <summary>Object ID of the group holding the generated collision brushes (0 when there are none).</summary>
        public long CollisionGroupId { get; set; }

        public TerrainData()
        {
        }

        public TerrainData(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
            SetId = obj.Get<long>("SetId");
            if (SetId == 0) SetId = ID;
            Name = obj.Get<string>("Name") ?? "Terrain";
            State = (TerrainState) obj.Get<int>("State");
            SurfaceTexture = obj.Get<string>("SurfaceTexture") ?? "GROUND";
            HiddenTexture = obj.Get<string>("HiddenTexture") ?? "NULL";
            MergeCoplanar = obj.Get<bool>("MergeCoplanar", true);
            CollisionEnabled = obj.Get<bool>("CollisionEnabled");
            CollisionBlock = Math.Max(1, obj.Get<int>("CollisionBlock", 4));
            CollisionSampling = (CollisionSampling) obj.Get<int>("CollisionSampling");
            ClipTexture = obj.Get<string>("ClipTexture") ?? "CLIP";
            GroupId = obj.Get<long>("GroupId");
            CollisionGroupId = obj.Get<long>("CollisionGroupId");

            var field = new Heightfield
            {
                CellsX = obj.Get<int>("CellsX"),
                CellsY = obj.Get<int>("CellsY"),
                CellSize = obj.Get<float>("CellSize"),
                LevelHeight = obj.Get<float>("LevelHeight"),
                ThicknessLevels = Math.Max(1, obj.Get<int>("ThicknessLevels", 2))
            };

            var corner = obj.Get<string>("P00");
            if (!string.IsNullOrEmpty(corner))
            {
                field.P00 = TerrainText.ReadVector(corner);
                field.P10 = TerrainText.ReadVector(obj.Get<string>("P10"));
                field.P01 = TerrainText.ReadVector(obj.Get<string>("P01"));
                field.P11 = TerrainText.ReadVector(obj.Get<string>("P11"));
                field.Normal = TerrainText.ReadVector(obj.Get<string>("Normal"));
                if (field.Normal.LengthSquared() < 1e-8f) field.Normal = Vector3.UnitZ;
                field.Dirs = TerrainText.ReadList(obj.Get<string>("Dirs"), (field.CellsX + 1) * (field.CellsY + 1));
            }
            else
            {
                // A flat terrain saved by an earlier version: a rectangle facing up
                var origin = new Vector3(obj.Get<float>("OriginX"), obj.Get<float>("OriginY"), obj.Get<float>("OriginZ"));
                var w = field.CellsX * field.CellSize;
                var h = field.CellsY * field.CellSize;
                field.P00 = origin;
                field.P10 = origin + new Vector3(w, 0, 0);
                field.P01 = origin + new Vector3(0, h, 0);
                field.P11 = origin + new Vector3(w, h, 0);
                field.Normal = Vector3.UnitZ;
            }
            field.Levels = ParseLevels(obj.Get<string>("Levels"), (field.CellsX + 1) * (field.CellsY + 1));
            Field = field;

            CellTextures = TerrainCellTextures.Read(obj.Get<string>("CellTextures"), field.CellsX * field.CellsY);
            OriginalBrushes = obj.Get<string>("OriginalBrushes") ?? "";
            OriginalParentId = obj.Get<long>("OriginalParentId");
        }

        [Export(typeof(IMapElementFormatter))]
        public class TerrainDataFormatter : StandardMapElementFormatter<TerrainData> { }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Meh
        }

        public IMapElement Clone()
        {
            return new TerrainData
            {
                ID = ID,
                SetId = SetId,
                Name = Name,
                State = State,
                Field = Field.Clone(),
                CellTextures = TerrainCellTextures.Clone(CellTextures),
                OriginalBrushes = OriginalBrushes,
                OriginalParentId = OriginalParentId,
                SurfaceTexture = SurfaceTexture,
                HiddenTexture = HiddenTexture,
                MergeCoplanar = MergeCoplanar,
                CollisionEnabled = CollisionEnabled,
                CollisionBlock = CollisionBlock,
                CollisionSampling = CollisionSampling,
                ClipTexture = ClipTexture,
                GroupId = GroupId,
                CollisionGroupId = CollisionGroupId
            };
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            return Clone();
        }

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject("TerrainData");
            so.Set("ID", ID);
            so.Set("SetId", SetId);
            so.Set("Name", Name);
            so.Set("State", (int) State);
            so.Set("SurfaceTexture", SurfaceTexture);
            so.Set("HiddenTexture", HiddenTexture);
            so.Set("MergeCoplanar", MergeCoplanar);
            so.Set("CollisionEnabled", CollisionEnabled);
            so.Set("CollisionBlock", CollisionBlock);
            so.Set("CollisionSampling", (int) CollisionSampling);
            so.Set("ClipTexture", ClipTexture);
            so.Set("GroupId", GroupId);
            so.Set("CollisionGroupId", CollisionGroupId);
            so.Set("CellsX", Field.CellsX);
            so.Set("CellsY", Field.CellsY);
            so.Set("CellSize", Field.CellSize);
            so.Set("LevelHeight", Field.LevelHeight);
            so.Set("ThicknessLevels", Field.ThicknessLevels);
            so.Set("P00", TerrainText.Write(Field.P00));
            so.Set("P10", TerrainText.Write(Field.P10));
            so.Set("P01", TerrainText.Write(Field.P01));
            so.Set("P11", TerrainText.Write(Field.P11));
            so.Set("Normal", TerrainText.Write(Field.Normal));
            so.Set("Dirs", TerrainText.Write(Field.Dirs));
            so.Set("Levels", FormatLevels(Field.Levels));
            so.Set("CellTextures", TerrainCellTextures.Write(CellTextures));
            so.Set("OriginalBrushes", OriginalBrushes ?? "");
            so.Set("OriginalParentId", OriginalParentId);
            return so;
        }

        public static string FormatLevels(int[] levels)
        {
            return string.Join(",", levels.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        }

        public static int[] ParseLevels(string text, int expected)
        {
            var result = new int[expected];
            if (string.IsNullOrEmpty(text)) return result;

            var parts = text.Split(',');
            for (var i = 0; i < expected && i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) result[i] = v;
            }
            return result;
        }

        /// <summary>Everything an edit changes, so an operation can swap it in and out.</summary>
        public sealed class Snapshot
        {
            public Heightfield Field;
            public TerrainTextureInfo[] CellTextures;
            public string SurfaceTexture;
            public TerrainState State;
            public long GroupId;
            public long CollisionGroupId;
        }

        public Snapshot Capture()
        {
            return new Snapshot
            {
                Field = Field.Clone(),
                CellTextures = TerrainCellTextures.Clone(CellTextures),
                SurfaceTexture = SurfaceTexture,
                State = State,
                GroupId = GroupId,
                CollisionGroupId = CollisionGroupId
            };
        }

        public void Restore(Snapshot snapshot)
        {
            Field = snapshot.Field.Clone();
            Field.Touch();
            CellTextures = TerrainCellTextures.Clone(snapshot.CellTextures);
            SurfaceTexture = snapshot.SurfaceTexture ?? SurfaceTexture;
            State = snapshot.State;
            GroupId = snapshot.GroupId;
            CollisionGroupId = snapshot.CollisionGroupId;
        }
    }

    /// <summary>Undoable change of a terrain: swaps between two snapshots.</summary>
    public class TerrainEdit : IOperation
    {
        public bool Trivial => false;

        private readonly TerrainData _terrain;
        private readonly TerrainData.Snapshot _before;
        private readonly TerrainData.Snapshot _after;

        public TerrainEdit(TerrainData terrain, TerrainData.Snapshot before, TerrainData.Snapshot after)
        {
            _terrain = terrain;
            _before = before;
            _after = after;
        }

        public Task<Change> Perform(MapDocument document)
        {
            _terrain.Restore(_after);
            return Task.FromResult(new Change(document).Update(_terrain));
        }

        public Task<Change> Reverse(MapDocument document)
        {
            _terrain.Restore(_before);
            return Task.FromResult(new Change(document).Update(_terrain));
        }
    }
}
