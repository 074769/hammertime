using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Environment
{
    /// <summary>
    /// Remembers, per map file, whether entity relation lines were switched on.
    /// Relations are off unless the map is listed here.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class MapRelationsSettingsManager : ISettingsContainer
    {
        private static MapRelationsSettingsManager _instance;

        [Setting("MapsWithRelations")]
        public List<string> Maps { get; private set; } = new List<string>();

        public string Name => "Sledge.BspEditor.Environment.MapRelationsSettingsManager";

        public bool ValuesLoaded { get; set; } = false;

        public MapRelationsSettingsManager()
        {
            _instance = this;
        }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            Maps = store.Get<string[]>("MapsWithRelations")?.ToList() ?? new List<string>();
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("MapsWithRelations", Maps);
        }

        public static MapRelationsSettingsManager GetInstance()
        {
            return _instance;
        }

        private static string NormalisePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            try { return Path.GetFullPath(path).ToLowerInvariant(); }
            catch { return path.Trim().ToLowerInvariant(); }
        }

        public bool IsOn(string mapPath)
        {
            if (string.IsNullOrWhiteSpace(mapPath)) return false;
            var key = NormalisePath(mapPath);
            return Maps.Any(x => NormalisePath(x) == key);
        }

        /// <returns>True if the remembered value changed (settings should be saved)</returns>
        public bool Set(string mapPath, bool on)
        {
            if (string.IsNullOrWhiteSpace(mapPath)) return false;
            var key = NormalisePath(mapPath);
            var existing = Maps.Where(x => NormalisePath(x) == key).ToList();
            if (on)
            {
                if (existing.Count > 0) return false;
                Maps.Add(mapPath);
                return true;
            }
            if (existing.Count == 0) return false;
            foreach (var e in existing) Maps.Remove(e);
            return true;
        }
    }
}
