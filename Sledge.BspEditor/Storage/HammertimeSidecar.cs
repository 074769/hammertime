using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// The single sidecar file kept next to a map ("mymap.rmf.hammertime.json") for everything Hammertime
    /// stores that the map format can't hold. Each feature owns one named section, so features share the file.
    /// </summary>
    public static class HammertimeSidecar
    {
        public const string Suffix = ".hammertime.json";
        private const string VersionKey = "Version";
        private const int Version = 2;

        private static readonly object Lock = new object();
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public static string GetPath(string mapFileName)
        {
            return mapFileName + Suffix;
        }

        /// <summary>Read one section, or null if it isn't there.</summary>
        public static T Get<T>(string mapFileName, string section) where T : class
        {
            if (string.IsNullOrWhiteSpace(mapFileName)) return null;

            lock (Lock)
            {
                var sections = ReadElements(mapFileName);
                if (!sections.TryGetValue(section, out var element)) return null;

                try
                {
                    return JsonSerializer.Deserialize<T>(element.GetRawText());
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>Write one section, keeping the others.</summary>
        public static void Set(string mapFileName, string section, object value)
        {
            if (string.IsNullOrWhiteSpace(mapFileName)) return;

            lock (Lock)
            {
                var sections = ReadAll(mapFileName);
                sections[section] = value;
                Write(mapFileName, sections);
            }
        }

        /// <summary>Remove one section. The file is deleted when nothing is left in it.</summary>
        public static void Remove(string mapFileName, string section)
        {
            if (string.IsNullOrWhiteSpace(mapFileName)) return;

            lock (Lock)
            {
                var path = GetPath(mapFileName);
                if (!File.Exists(path)) return;

                var sections = ReadAll(mapFileName);
                if (!sections.Remove(section)) return;

                if (sections.Keys.All(x => x == VersionKey))
                {
                    File.Delete(path);
                }
                else
                {
                    Write(mapFileName, sections);
                }
            }
        }

        private static Dictionary<string, JsonElement> ReadElements(string mapFileName)
        {
            var path = GetPath(mapFileName);
            if (!File.Exists(path)) return new Dictionary<string, JsonElement>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))
                       ?? new Dictionary<string, JsonElement>();
            }
            catch (Exception)
            {
                return new Dictionary<string, JsonElement>();
            }
        }

        private static Dictionary<string, object> ReadAll(string mapFileName)
        {
            return ReadElements(mapFileName).ToDictionary(x => x.Key, x => (object)x.Value);
        }

        private static void Write(string mapFileName, Dictionary<string, object> sections)
        {
            sections[VersionKey] = Version;
            File.WriteAllText(GetPath(mapFileName), JsonSerializer.Serialize(sections, Json));
        }
    }
}
