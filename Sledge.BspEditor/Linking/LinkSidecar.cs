using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// Remembers link groups for map formats that can't store them (RMF, MAP, ...).
    ///
    /// The links are written to a small JSON file next to the map ("mymap.rmf.links.json") when the map is saved,
    /// and read back when it's opened. Those formats don't keep object IDs, so each linked object is recorded by
    /// its type, entity class and bounding box, and matched back up to the nearest object when the map is loaded.
    /// The native format (.smf) stores links itself, so no file is used for it.
    /// </summary>
    public static class LinkSidecar
    {
        public const string Suffix = ".links.json";

        // How far (in map units) a bounding box coordinate can differ and still be the same object.
        // Formats that rebuild brushes from planes (.map) can shift vertices by a fraction of a unit.
        private const float Tolerance = 0.5f;

        private static readonly string[] NativeExtensions = { ".smf" };

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        private class MemberData
        {
            public string Type { get; set; }
            public string Class { get; set; }
            public float[] Box { get; set; }
        }

        private class GroupData
        {
            public long ID { get; set; }
            public string Name { get; set; }
            public int Colour { get; set; }
            public List<MemberData> Members { get; set; } = new List<MemberData>();
        }

        private class FileData
        {
            public int Version { get; set; } = 1;
            public List<GroupData> Groups { get; set; } = new List<GroupData>();
        }

        public static string GetPath(string mapFileName)
        {
            return mapFileName + Suffix;
        }

        private static bool IsNative(string fileName)
        {
            var ext = System.IO.Path.GetExtension(fileName) ?? "";
            return NativeExtensions.Any(x => string.Equals(x, ext, StringComparison.OrdinalIgnoreCase));
        }

        private static string ClassOf(IMapObject o)
        {
            return (o as Entity)?.EntityData?.Name ?? "";
        }

        /// <summary>
        /// Write (or remove) the links file for a document that was just saved to its file name.
        /// </summary>
        public static void Save(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var path = GetPath(fileName);
            try
            {
                var members = LinkedObjects.GetMembers(document);

                // Nothing to remember (or the format remembers it itself): don't leave an out of date file behind
                if (members.Count == 0 || IsNative(fileName))
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                var groups = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());
                var data = new FileData();
                foreach (var kv in members.OrderBy(x => x.Key))
                {
                    groups.TryGetValue(kv.Key, out var group);
                    var g = new GroupData
                    {
                        ID = kv.Key,
                        Name = group?.Name ?? ("Link " + kv.Key),
                        Colour = (group?.Colour ?? LinkedObjects.ColourFor(kv.Key)).ToArgb()
                    };
                    foreach (var o in kv.Value)
                    {
                        var b = o.BoundingBox;
                        g.Members.Add(new MemberData
                        {
                            Type = o.GetType().Name,
                            Class = ClassOf(o),
                            Box = new[] { b.Start.X, b.Start.Y, b.Start.Z, b.End.X, b.End.Y, b.End.Z }
                        });
                    }
                    data.Groups.Add(g);
                }

                File.WriteAllText(path, JsonSerializer.Serialize(data, Json));
            }
            catch (Exception)
            {
                // Remembering links is a nicety, it must never get in the way of saving the map
            }
        }

        /// <summary>
        /// Restore the links for a document that was just loaded from its file name.
        /// </summary>
        public static void Load(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            // The map format already gave us its links (native format)
            if (document.Map.Data.Get<LinkGroup>().Any()) return;

            var path = GetPath(fileName);
            if (!File.Exists(path)) return;

            FileData data;
            try
            {
                data = JsonSerializer.Deserialize<FileData>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return;
            }
            if (data?.Groups == null || data.Groups.Count == 0) return;

            // Candidates: every object that can be linked and isn't already, sorted so we can search by position
            var root = document.Map.Root;
            var candidates = root.FindAll()
                .Where(x => !ReferenceEquals(x, root) && LinkedObjects.GetLinkId(x) == null)
                .GroupBy(x => x.GetType().Name)
                .ToDictionary(x => x.Key, x => x.OrderBy(o => o.BoundingBox.Start.X).ToList());
            var used = new HashSet<IMapObject>();

            foreach (var g in data.Groups)
            {
                var matched = new List<IMapObject>();

                foreach (var m in g.Members ?? new List<MemberData>())
                {
                    if (m?.Box == null || m.Box.Length != 6 || m.Type == null) continue;
                    if (!candidates.TryGetValue(m.Type, out var list)) continue;

                    var found = Find(list, used, m);
                    if (found == null) continue;

                    used.Add(found);
                    matched.Add(found);
                }

                // A link needs at least two objects to mean anything, and a missing object means the map was edited elsewhere
                if (matched.Count < 2) continue;

                document.Map.Data.Add(new LinkGroup
                {
                    ID = g.ID,
                    Name = g.Name,
                    Colour = Color.FromArgb(g.Colour)
                });
                foreach (var o in matched) o.Data.Replace(new LinkGroupID(g.ID));
            }
        }

        private static IMapObject Find(List<IMapObject> sorted, HashSet<IMapObject> used, MemberData m)
        {
            // Binary search for the first object whose start X could match
            var lo = 0;
            var hi = sorted.Count;
            var minX = m.Box[0] - Tolerance;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (sorted[mid].BoundingBox.Start.X < minX) lo = mid + 1;
                else hi = mid;
            }

            IMapObject best = null;
            var bestDiff = float.MaxValue;

            for (var i = lo; i < sorted.Count; i++)
            {
                var o = sorted[i];
                var b = o.BoundingBox;
                if (b.Start.X > m.Box[0] + Tolerance) break;
                if (used.Contains(o)) continue;
                if (!string.Equals(ClassOf(o), m.Class ?? "", StringComparison.OrdinalIgnoreCase)) continue;

                var diff = Math.Max(
                    Math.Max(Math.Abs(b.Start.X - m.Box[0]), Math.Abs(b.Start.Y - m.Box[1])),
                    Math.Max(
                        Math.Max(Math.Abs(b.Start.Z - m.Box[2]), Math.Abs(b.End.X - m.Box[3])),
                        Math.Max(Math.Abs(b.End.Y - m.Box[4]), Math.Abs(b.End.Z - m.Box[5]))
                    )
                );
                if (diff > Tolerance || diff >= bestDiff) continue;

                best = o;
                bestDiff = diff;
            }

            return best;
        }
    }
}
