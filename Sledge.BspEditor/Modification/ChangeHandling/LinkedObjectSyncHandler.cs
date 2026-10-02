using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Keeps linked objects in sync: when one member of a link group is edited, the other members
    /// are updated to match (shape, textures, and properties - each keeps its own position).
    ///
    /// Because the other members are always derived from the edited one, undo and redo
    /// need no special handling: reversing the edit raises another change that is synced the same way.
    ///
    /// Runs before the visgroup handlers so that the objects it replaces are processed by them.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectSyncHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "J";

        public Task Changed(Change change)
        {
            if (!change.Updated.Any()) return Task.CompletedTask;

            // The linked objects touched by this change (an edit to a linked entity's brush counts as an edit to the entity)
            var owners = change.Updated
                .Select(LinkedObjects.FindLinkedOwner)
                .Where(x => x != null)
                .ToList();
            if (owners.Count == 0) return Task.CompletedTask;

            var touched = owners.Select(x => LinkedObjects.GetLinkId(x).Value).Distinct().ToList();
            var members = LinkedObjects.GetMembers(change.Document);

            foreach (var groupId in touched)
            {
                if (!members.TryGetValue(groupId, out var all) || all.Count < 2) continue;

                var updated = all.Where(x => owners.Any(o => ReferenceEquals(o, x))).ToList();
                var others = all.Where(x => !updated.Any(u => ReferenceEquals(u, x))).ToList();

                // If every member was changed at once (eg moving a selection) there's nothing to copy from
                if (others.Count == 0) continue;

                // Selecting an object also counts as an update, so look for a member that
                // actually differs from the members that weren't touched
                var baseline = others[0];
                var source = updated.FirstOrDefault(x => x.GetType() == baseline.GetType() && !LinkedObjects.AreEquivalent(x, baseline));
                if (source == null) continue;

                foreach (var target in all)
                {
                    if (ReferenceEquals(target, source)) continue;
                    if (target.GetType() != source.GetType()) continue;
                    if (LinkedObjects.AreEquivalent(source, target)) continue;

                    LinkedObjects.Sync(change.Document, source, target, change);
                }
            }

            return Task.CompletedTask;
        }
    }
}
