using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "", "Entity", "D")]
    [CommandID("BspEditor:Tools:MoveToWorld")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_TieToWorld))]
    [DefaultHotkey("Ctrl+Shift+W")]
    public class MoveToWorld : BaseCommand
    {
        public override string Name { get; set; } = "Move to world";
        public override string Details { get; set; } = "Delete all selected solid entities and move their brushes back to the world. Selected brushes tied to an entity (e.g. with ignore grouping on) are moved back to the world too.";
        
        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                && document.Selection.Any(x => x is Entity || GetOwningEntity(x) != null);
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var entities = document.Selection.OfType<Entity>().ToList();

            // Selected objects that belong to an entity but aren't being removed with it
            // (e.g. brushes selected with ignore grouping on). Anything whose owning entity is
            // itself selected is already handled by the entity pass below.
            var loose = document.Selection
                .Where(x => !(x is Entity))
                .Where(x => {
                    var owner = GetOwningEntity(x);
                    return owner != null && !entities.Contains(owner);
                })
                .ToList();

            // If a selected object has a selected ancestor, the ancestor carries it along
            var looseSet = new HashSet<IMapObject>(loose);
            loose = loose.Where(x => !HasAncestorIn(x, looseSet)).ToList();

            if (!entities.Any() && !loose.Any()) return;

            // Deselect the entities we're about to delete
            var ops = new List<IOperation>();
            if (entities.Any()) ops.Add(new Deselect(entities));

            // Remove the children
            foreach (var entity in entities)
            {
                var children = entity.Hierarchy.Where(x => !(x is Entity)).ToList();

                if (children.Any())
                {
                    // Make sure we don't try and attach the solids back to an entity
                    var newParentId = entities.Contains(entity.Hierarchy.Parent)
                        ? document.Map.Root.ID
                        : entity.Hierarchy.Parent.ID;

                    // Move the entity's children to the new parent before removing the entity
                    ops.Add(new Detatch(entity.ID, children));
                    ops.Add(new Attach(newParentId, children));
                }
            }

            // Remove the parents
            foreach (var entity in entities)
            {
                // If the parent is a selected entity then we don't need to detach this one as the parent will be detatched
                if (!entities.Contains(entity.Hierarchy.Parent))
                {
                    ops.Add(new Detatch(entity.Hierarchy.Parent.ID, entity));
                }
            }

            // Loose selected objects: move them straight to the world root
            if (loose.Any())
            {
                var root = document.Map.Root;

                foreach (var byParent in loose.GroupBy(x => x.Hierarchy.Parent.ID))
                {
                    ops.Add(new Detatch(byParent.Key, byParent.ToList()));
                }
                ops.Add(new Attach(root.ID, loose));

                // Any container left with nothing in it once these are gone is removed too
                // (an empty entity or group would be invalid).
                var movedSet = new HashSet<IMapObject>(loose);
                var toRemove = new HashSet<IMapObject>();
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    var candidates = movedSet.Concat(toRemove)
                        .Select(x => x.Hierarchy.Parent)
                        .Where(p => p != null && !(p is Root) && !entities.Contains(p))
                        .Distinct()
                        .ToList();

                    foreach (var p in candidates)
                    {
                        if (toRemove.Contains(p)) continue;
                        if (p.Hierarchy.All(c => movedSet.Contains(c) || toRemove.Contains(c)))
                        {
                            toRemove.Add(p);
                            changed = true;
                        }
                    }
                }

                // Only remove the topmost empty containers; anything beneath them goes with them
                var topRemove = toRemove.Where(x => !HasAncestorIn(x, toRemove)).ToList();
                foreach (var obj in topRemove)
                {
                    ops.Add(new Detatch(obj.Hierarchy.Parent.ID, obj));
                }
            }

            if (ops.Any())
            {
                await MapDocumentOperation.Perform(document, new Transaction(ops));
            }
        }

        /// <summary>The nearest entity above this object in the hierarchy, or null if it sits in the world.</summary>
        private static Entity GetOwningEntity(IMapObject obj)
        {
            var p = obj.Hierarchy.Parent;
            while (p != null && !(p is Root))
            {
                if (p is Entity e) return e;
                p = p.Hierarchy.Parent;
            }
            return null;
        }

        private static bool HasAncestorIn(IMapObject obj, HashSet<IMapObject> set)
        {
            var p = obj.Hierarchy.Parent;
            while (p != null)
            {
                if (set.Contains(p)) return true;
                p = p.Hierarchy.Parent;
            }
            return false;
        }
    }
}
