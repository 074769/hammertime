using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Components
{
    /// <summary>
    /// The saved camera of a single viewport, identified by its slot in the viewport layout.
    /// </summary>
    public class ViewCameraEntry
    {
        public int WindowID { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public string Type { get; set; }
        public string Serialised { get; set; }
    }

    /// <summary>
    /// All of the viewport cameras remembered for one specific map.
    /// </summary>
    public class MapViewCameras
    {
        public string MapPath { get; set; }
        public List<ViewCameraEntry> Cameras { get; set; } = new List<ViewCameraEntry>();
    }

    /// <summary>
    /// Remembers, per map file, where every viewport (2D and 3D) was looking when the map was saved.
    /// Map formats only store a 3D camera list (if anything), so this lives in the settings file
    /// instead, keyed by the map's path.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class MapViewCameraSettingsManager : ISettingsContainer
    {
        private const int MaxMaps = 200;

        private static MapViewCameraSettingsManager _instance;

        [Setting("MapViewCameras")]
        public List<MapViewCameras> MapCameras { get; private set; } = new List<MapViewCameras>();

        public string Name => "Sledge.BspEditor.Components.MapViewCameraSettingsManager";

        public bool ValuesLoaded { get; set; } = false;

        public MapViewCameraSettingsManager()
        {
            _instance = this;
        }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            MapCameras = store.Get<MapViewCameras[]>("MapViewCameras")?.ToList() ?? new List<MapViewCameras>();
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("MapViewCameras", MapCameras);
        }

        public static MapViewCameraSettingsManager GetInstance()
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
        /// Gets the remembered cameras for the map path, or null if there are none.
        /// </summary>
        public List<ViewCameraEntry> Get(string mapPath)
        {
            if (string.IsNullOrWhiteSpace(mapPath)) return null;
            var key = NormalisePath(mapPath);
            return MapCameras.FirstOrDefault(x => NormalisePath(x.MapPath) == key)?.Cameras;
        }

        /// <summary>
        /// Remembers the cameras for the map path.
        /// </summary>
        public void Set(string mapPath, IEnumerable<ViewCameraEntry> cameras)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || cameras == null) return;

            var key = NormalisePath(mapPath);
            MapCameras.RemoveAll(x => NormalisePath(x.MapPath) == key);
            MapCameras.Add(new MapViewCameras { MapPath = mapPath, Cameras = cameras.ToList() });

            // Don't let the list grow forever - forget the least recently saved maps
            if (MapCameras.Count > MaxMaps) MapCameras.RemoveRange(0, MapCameras.Count - MaxMaps);
        }
    }
}
