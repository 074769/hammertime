using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GameDataDef = Sledge.DataStructures.GameData.GameData;
using VarType = Sledge.DataStructures.GameData.VariableType;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Tools.Widgets;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.Selection
{
    /// <summary>
    /// Gives entities that were duplicated (Ctrl+Shift+drag) a unique targetname, and re-points
    /// references to duplicated entities (target, killtarget, LightningStart, ...) at the new names.
    /// Must be called before the copies are attached to the map.
    /// </summary>
    internal static class DuplicatedEntityNames
    {
        /// <summary>
        /// Keys that reference another entity's targetname, used when the FGD does not describe the entity class.
        /// </summary>
        private static readonly string[] FallbackReferenceKeys =
        {
            "target", "killtarget", "pathtarget", "master", "LightningStart", "LightningEnd", "LaserTarget",
        };

        /// <summary>
        /// Keys of an entity that are never treated as references to other entities.
        /// </summary>
        private static readonly HashSet<string> NonTargetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "targetname", "classname", "origin", "angles", "spawnflags", "model", "message", "netname"
        };

        private static readonly Regex LastNumber = new Regex("[0-9]+(?![^0-9]*[0-9])");

        /// <param name="document">The document the originals belong to.</param>
        /// <param name="originals">The selected objects that were duplicated.</param>
        /// <param name="copies">The copies, in the same order as <paramref name="originals"/>.</param>
        public static void Apply(MapDocument document, IList<IMapObject> originals, IList<IMapObject> copies)
        {
            if (originals.Count != copies.Count) return;

            // Game data (FGD) tells us which properties reference other entities. Null is fine, we fall back to a key list.
            MotionWidgetDrawing.TryGetGameData(document, out var gameData);

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

            // Give each named duplicate a new unique name, remembering old -> new for references
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

            // Re-point references to duplicated entities; references to entities that were not duplicated keep their name
            foreach (var pair in pairs)
            {
                var data = pair.Value.EntityData;
                if (data == null) continue;

                foreach (var key in data.Properties.Keys.ToList())
                {
                    if (!IsReferenceKey(gameData, data.Name, key)) continue;
                    var value = data.Properties[key];
                    if (value != null && renames.TryGetValue(value.Trim(), out var newValue))
                    {
                        data.Properties[key] = newValue;
                    }
                }

                // multi_manager uses the targetnames of its targets as its keys (values are delays)
                if (string.Equals(data.Name, "multi_manager", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var key in data.Properties.Keys.ToList())
                    {
                        if (NonTargetKeys.Contains(key) || !renames.TryGetValue(key.Trim(), out var newKey)) continue;
                        if (data.Properties.ContainsKey(newKey)) continue;
                        var delay = data.Properties[key];
                        data.Properties.Remove(key);
                        data.Properties[newKey] = delay;
                    }
                }
            }
        }

        /// <summary>
        /// A key is a reference if it is one of the known reference keys, or if the FGD types it as a
        /// target source or destination. The entity's own targetname is never a reference (renamed separately).
        /// </summary>
        private static bool IsReferenceKey(GameDataDef gameData, string className, string key)
        {
            if (string.Equals(key, "targetname", StringComparison.OrdinalIgnoreCase)) return false;
            if (FallbackReferenceKeys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))) return true;

            var cls = (gameData != null && !string.IsNullOrEmpty(className)) ? gameData.GetClass(className) : null;
            if (cls == null) return false;

            return cls.Properties.Any(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)
                                           && (p.VariableType == VarType.TargetSource || p.VariableType == VarType.TargetDestination));
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
