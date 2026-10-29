using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Selection
{
    /// <summary>
    /// Gives entities that were duplicated (Ctrl+Shift+drag) a unique targetname, and re-points
    /// the "target" of duplicated entities at the duplicate of their target when that target
    /// was duplicated as well. Must be called before the copies are attached to the map.
    /// </summary>
    internal static class DuplicatedEntityNames
    {
        private static readonly Regex LastNumber = new Regex("[0-9]+(?![^0-9]*[0-9])");

        /// <param name="document">The document the originals belong to.</param>
        /// <param name="originals">The selected objects that were duplicated.</param>
        /// <param name="copies">The copies, in the same order as <paramref name="originals"/>.</param>
        public static void Apply(MapDocument document, IList<IMapObject> originals, IList<IMapObject> copies)
        {
            if (originals.Count != copies.Count) return;

            // Every targetname currently in the map (originals included) is taken
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in document.Map.Root.Find(x => x is MapEntity).OfType<MapEntity>())
            {
                var name = GetName(e);
                if (name != null) taken.Add(name);
            }

            // Pair up original and copy entities
            var pairs = new List<KeyValuePair<MapEntity, MapEntity>>();
            for (var i = 0; i < originals.Count; i++)
            {
                var oe = originals[i].FindAll().OfType<MapEntity>().ToList();
                var ce = copies[i].FindAll().OfType<MapEntity>().ToList();
                if (oe.Count != ce.Count) continue;
                for (var j = 0; j < oe.Count; j++) pairs.Add(new KeyValuePair<MapEntity, MapEntity>(oe[j], ce[j]));
            }

            // Give each named duplicate a new unique name, remembering old -> new for targets
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                var name = GetName(pair.Key);
                if (name == null) continue;

                var newName = NextName(name, taken);
                taken.Add(newName);
                if (!renames.ContainsKey(name)) renames[name] = newName;

                pair.Value.EntityData.Properties["targetname"] = newName;
            }

            if (renames.Count == 0) return;

            // Re-point targets whose target was duplicated too; untouched targets keep their name
            foreach (var pair in pairs)
            {
                var data = pair.Value.EntityData;
                if (data == null || !data.Properties.TryGetValue("target", out var target)) continue;
                if (target == null) continue;

                var key = target.Trim();
                if (key.Length > 0 && renames.TryGetValue(key, out var newTarget))
                {
                    data.Properties["target"] = newTarget;
                }
            }
        }

        private static string GetName(MapEntity entity)
        {
            var data = entity.EntityData;
            if (data == null || !data.Properties.TryGetValue("targetname", out var name)) return null;
            if (string.IsNullOrWhiteSpace(name)) return null;
            return name.Trim();
        }

        /// <summary>
        /// Returns the name with its last number incremented until it is free ("S2" -> "S3"),
        /// or "_1", "_2", ... appended when the name has no number ("door" -> "door_1").
        /// </summary>
        private static string NextName(string name, ISet<string> taken)
        {
            var match = LastNumber.Match(name);
            for (var i = 1; ; i++)
            {
                string candidate;
                if (match.Success && long.TryParse(match.Value, out var number))
                {
                    var bumped = (number + i).ToString().PadLeft(match.Length, '0');
                    candidate = name.Substring(0, match.Index) + bumped + name.Substring(match.Index + match.Length);
                }
                else
                {
                    candidate = name + "_" + i;
                }

                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
