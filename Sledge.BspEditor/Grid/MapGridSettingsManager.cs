using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Grid
{
    /// <summary>
    /// The grid choice remembered for one specific map.
    /// </summary>
    public class MapGridChoice
    {
        public string MapPath { get; set; }
        public float Step { get; set; } = 64;
        public bool SnapToGrid { get; set; } = true;
        public bool NoGrid { get; set; }
    }

    /// <summary>
    /// Remembers, per map file, the grid size (and snap/no-grid state) last used in the editor.
    /// All maps are stored together in a single settings file, so this works for every
    /// map format and doesn't need to live inside the map file itself.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class MapGridSettingsManager : ISettingsContainer
    {
        private static MapGridSettingsManager _instance;

        [Setting("MapGridChoices")]
        public List<MapGridChoice> MapChoices { get; private set; } = new List<MapGridChoice>();

        public string Name => "Sledge.BspEditor.Grid.MapGridSettingsManager";

        public bool ValuesLoaded { get; set; } = false;

        public MapGridSettingsManager()
        {
            _instance = this;
        }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            MapChoices = store.Get<MapGridChoice[]>("MapGridChoices")?.ToList() ?? new List<MapGridChoice>();
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("MapGridChoices", MapChoices);
        }

        public static MapGridSettingsManager GetInstance()
        {
            return _instance;
        }

        private static string NormalisePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            try
            {
                return Path.GetFullPath(path).ToLowerInvariant();
            }
            catch
            {
                return path.Trim().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Gets the remembered grid for the map path, or null if there isn't one.
        /// </summary>
        public MapGridChoice Get(string mapPath)
        {
            if (string.IsNullOrWhiteSpace(mapPath)) return null;
            var key = NormalisePath(mapPath);
            return MapChoices.FirstOrDefault(x => NormalisePath(x.MapPath) == key);
        }

        /// <summary>
        /// Remembers the document's current grid for the map path.
        /// </summary>
        /// <returns>True if the remembered value changed (settings should be saved)</returns>
        public bool Set(string mapPath, GridData data)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || data?.Grid == null) return false;

            var noGrid = data.Grid is NoGrid;
            var entry = Get(mapPath);
            var step = data.Grid is SquareGrid sg ? sg.Step : (entry?.Step ?? 64);

            if (entry == null)
            {
                MapChoices.Add(new MapGridChoice { MapPath = mapPath, Step = step, SnapToGrid = data.SnapToGrid, NoGrid = noGrid });
                return true;
            }

            if (entry.Step == step && entry.SnapToGrid == data.SnapToGrid && entry.NoGrid == noGrid) return false;

            entry.MapPath = mapPath;
            entry.Step = step;
            entry.SnapToGrid = data.SnapToGrid;
            entry.NoGrid = noGrid;
            return true;
        }

        /// <summary>
        /// Applies the remembered grid (if any) to the document's grid data.
        /// </summary>
        public void Apply(string mapPath, GridData data)
        {
            var entry = Get(mapPath);
            if (entry == null || data == null) return;

            data.SnapToGrid = entry.SnapToGrid;
            if (entry.NoGrid) data.Grid = new NoGrid();
            else if (data.Grid is SquareGrid sg && entry.Step > 0) sg.Step = entry.Step;
        }
    }
}
