using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Maintains a "Entities by Class" auto visgroup tree containing exactly one entry per
    /// entity classname that currently exists somewhere in the map - so mappers can show/hide
    /// entities by type on demand. Runs before <see cref="VisgroupHandler"/> ("M") so that any
    /// newly-created groups are already present in the map's data by the time it matches objects
    /// into them.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class EntityClassAutoVisgroupHandler : IMapDocumentChangeHandler
    {
        /// <summary>
        /// Auto visgroup path used for the per-classname group tree. Not tied to any one
        /// environment - Goldsource and Source maps both funnel through here.
        /// </summary>
        public const string EntitiesByClassPath = "Sledge.BspEditor.AutomaticVisgroups.EntitiesByClass";

        public string OrderHint => "L";

        public Task Changed(Change change)
        {
            // New class entries are only created for changed entities; visibility-only
            // changes can't introduce new class names.
            if (VisibilityOnlyFastPath.IsActive(change)) return Task.CompletedTask;

            var classNames = change.Added.Union(change.Updated)
                .OfType<Entity>()
                .Select(e => e.EntityData?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var existing = change.Document.Map.Data.Get<AutomaticVisgroup>()
                .Where(av => av.Path == EntitiesByClassPath)
                .ToDictionary(av => av.Key, av => av, StringComparer.OrdinalIgnoreCase);

            foreach (var className in classNames)
            {
                if (existing.ContainsKey(className)) continue;

                var name = className; // capture for the closure
                var av = new AutomaticVisgroup(x => x is Entity e && string.Equals(e.EntityData?.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    Path = EntitiesByClassPath,
                    Key = name
                };
                change.Document.Map.Data.Add(av);
                existing[name] = av;
            }

            return Task.CompletedTask;
        }
    }
}
