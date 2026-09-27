using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Environment
{
    /// <summary>
    /// The set of texture packages (WADs) manually unloaded for one specific map.
    /// </summary>
    public class MapTexturePackageChoice
    {
        public string MapPath { get; set; }
        public List<string> DisabledPackages { get; set; } = new List<string>();
    }

    /// <summary>
    /// Remembers, per map file, which texture packages (WADs) the user has manually unloaded
    /// in the Texture Browser. All maps ever opened are stored together in a single settings
    /// file so the choice survives editor restarts and doesn't need to live inside the map
    /// file itself.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class MapTexturePackageSettingsManager : ISettingsContainer
    {
        private static MapTexturePackageSettingsManager _instance;

        [Setting("MapTexturePackageChoices")]
        public List<MapTexturePackageChoice> MapChoices { get; private set; } = new List<MapTexturePackageChoice>();

        public string Name => "Sledge.BspEditor.Environment.MapTexturePackageSettingsManager";

        public bool ValuesLoaded { get; set; } = false;

        public MapTexturePackageSettingsManager()
        {
            _instance = this;
        }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            MapChoices = store.Get<MapTexturePackageChoice[]>("MapTexturePackageChoices")?.ToList() ?? new List<MapTexturePackageChoice>();
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("MapTexturePackageChoices", MapChoices);
        }

        /// <summary>
        /// Returns the loaded instance of this settings container, or null if settings
        /// haven't been loaded yet (e.g. very early in startup).
        /// </summary>
        public static MapTexturePackageSettingsManager GetInstance()
        {
            return _instance;
        }

        /// <summary>
        /// Normalises a map path so the same file is recognised between sessions regardless
        /// of case or relative path segments.
        /// </summary>
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
        /// Gets the set of package names manually disabled for the given map path.
        /// Returns an empty set if the map has no remembered choice, or the path is empty
        /// (e.g. the map hasn't been saved yet).
        /// </summary>
        public HashSet<string> GetDisabledPackages(string mapPath)
        {
            var result = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            if (string.IsNullOrWhiteSpace(mapPath)) return result;

            var key = NormalisePath(mapPath);
            var entry = MapChoices.FirstOrDefault(x => NormalisePath(x.MapPath) == key);
            if (entry != null) result.UnionWith(entry.DisabledPackages);
            return result;
        }

        /// <summary>
        /// Sets the disabled package list for a map and immediately persists all settings to
        /// disk, so the choice survives a crash as well as a normal exit.
        /// </summary>
        public void SetDisabledPackages(string mapPath, IEnumerable<string> disabledPackages)
        {
            if (string.IsNullOrWhiteSpace(mapPath)) return;

            var key = NormalisePath(mapPath);
            var entry = MapChoices.FirstOrDefault(x => NormalisePath(x.MapPath) == key);
            var list = (disabledPackages ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .ToList();

            if (entry == null)
            {
                if (list.Count == 0) return;
                MapChoices.Add(new MapTexturePackageChoice { MapPath = mapPath, DisabledPackages = list });
            }
            else if (list.Count == 0)
            {
                MapChoices.Remove(entry);
            }
            else
            {
                entry.MapPath = mapPath;
                entry.DisabledPackages = list;
            }
        }
    }
}
