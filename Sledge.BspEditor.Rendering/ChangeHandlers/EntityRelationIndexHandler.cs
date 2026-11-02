using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.ChangeHandling;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Rendering.ChangeHandlers
{
    /// <summary>
    /// Finds entity relations (target -> targetname) from the live entities instead of lists stored on
    /// each entity, which went stale whenever an entity was cloned (move, undo, copy). The lookup is built
    /// once per change and only when relations are being drawn.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class EntityRelationIndexHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "A";

        private class Index
        {
            public Dictionary<string, List<Entity>> ByTarget = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
            public Dictionary<string, List<Entity>> ByTargetName = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        }

        private static readonly ConditionalWeakTable<MapDocument, Index> Indexes = new ConditionalWeakTable<MapDocument, Index>();

        public static void Invalidate(MapDocument doc)
        {
            if (doc != null) Indexes.Remove(doc);
        }

        private static Index Get(MapDocument doc)
        {
            return Indexes.GetValue(doc, Build);
        }

        private static Index Build(MapDocument doc)
        {
            var idx = new Index();
            foreach (var e in doc.Map.Root.Find(x => x is Entity).OfType<Entity>())
            {
                var data = e.EntityData;
                if (data == null) continue;
                if (data.Properties.TryGetValue("target", out var t) && !string.IsNullOrWhiteSpace(t)) Add(idx.ByTarget, t.Trim(), e);
                if (data.Properties.TryGetValue("targetname", out var n) && !string.IsNullOrWhiteSpace(n)) Add(idx.ByTargetName, n.Trim(), e);
            }
            return idx;
        }

        private static void Add(Dictionary<string, List<Entity>> d, string key, Entity e)
        {
            if (!d.TryGetValue(key, out var list)) d[key] = list = new List<Entity>();
            list.Add(e);
        }

        /// <summary>Entities that target the given entity (their "target" equals its "targetname").</summary>
        public static IReadOnlyList<Entity> GetTargeting(MapDocument doc, Entity entity)
        {
            var name = Name(entity, "targetname");
            if (name == null) return Array.Empty<Entity>();
            return Get(doc).ByTarget.TryGetValue(name, out var l) ? l.Where(x => x != entity).ToList() : (IReadOnlyList<Entity>) Array.Empty<Entity>();
        }

        private static string Name(Entity e, string key)
        {
            var data = e.EntityData;
            if (data == null || !data.Properties.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return null;
            return v.Trim();
        }

        public Task Changed(Change change)
        {
            if (VisibilityOnlyFastPath.IsActive(change) || SelectionOnlyFastPath.IsActive(change)) return Task.CompletedTask;

            Invalidate(change.Document);

            var flags = change.Document.Map.Data.GetOne<DisplayFlags>();
            if (flags == null || !flags.ToggleEntityRelations) return Task.CompletedTask;

            // A line runs between two entities, so when one end moves or is renamed the other end's lines are stale too
            var idx = Get(change.Document);
            var extra = new List<IMapObject>();
            foreach (var e in change.Added.Union(change.Updated).Union(change.Removed).OfType<Entity>().ToList())
            {
                var target = Name(e, "target");
                if (target != null && idx.ByTargetName.TryGetValue(target, out var named)) extra.AddRange(named);
                var name = Name(e, "targetname");
                if (name != null && idx.ByTarget.TryGetValue(name, out var targeting)) extra.AddRange(targeting);
            }
            if (extra.Count > 0) change.UpdateRange(extra.Distinct().Where(x => x.Hierarchy.Parent != null));
            return Task.CompletedTask;
        }
    }
}
