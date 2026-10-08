using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Maintains the "Linked Objects" automatic visgroup tree: one entry per link, and a folder of entries for each
    /// link's instances. Runs before <see cref="VisgroupHandler"/> ("M"), which fills each entry with its objects.
    /// If an object refers to a link or slot that doesn't exist (eg after an undo), it is recreated.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectsVisgroupHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "L";

        public async Task Changed(Change change)
        {
            // Entries are keyed by link/slot IDs and filled by VisgroupHandler, which is
            // skipped for visibility-only changes because membership can't change.
            if (VisibilityOnlyFastPath.IsActive(change)) return;

            var doc = change.Document;

            var linkedChanges = change.Added.Union(change.Updated).Any(x => LinkedObjects.GetLinkId(x) != null)
                                || change.Removed.Any(x => LinkedObjects.GetLinkId(x) != null);
            var dataChanges = change.AffectedData.OfType<LinkGroup>().Any();
            if (!linkedChanges && !dataChanges) return;

            var index = LinkedObjects.BuildIndex(doc);
            var changed = false;

            // Make sure every link and slot that has objects has a record
            foreach (var kv in index.Slots)
            {
                var top = LinkedObjects.GetTopId(kv.Value[0]);

                if (top != kv.Key && !index.Groups.ContainsKey(top))
                {
                    var link = new LinkGroup { ID = top, Name = "Link " + top, Colour = LinkedObjects.ColourFor(top) };
                    doc.Map.Data.Add(link);
                    index.Groups[top] = link;
                }

                if (!index.Groups.ContainsKey(kv.Key))
                {
                    var slot = new LinkGroup
                    {
                        ID = kv.Key,
                        Name = (top == kv.Key ? "Link " : "Slot ") + kv.Key,
                        Colour = LinkedObjects.ColourFor(top),
                        ParentID = top == kv.Key ? 0 : top
                    };
                    doc.Map.Data.Add(slot);
                    index.Groups[kv.Key] = slot;
                }
            }

            var existing = doc.Map.Data.Get<LinkedObjectsVisgroup>().GroupBy(x => (x.GroupID, x.Instance)).ToDictionary(x => x.Key, x => x.First());
            var wanted = new HashSet<(long, long)>();

            foreach (var kv in index.Links)
            {
                var link = index.Groups.TryGetValue(kv.Key, out var g) ? g : null;
                var name = LinkedObjects.SafeName(link?.Name ?? ("Link " + kv.Key));
                var origin = index.OriginInstance(kv.Key);

                changed |= Ensure(doc, existing, wanted, kv.Key, 0, LinkedObjects.AutoVisgroupPath, name);

                // The instances are only worth listing if there's more than one
                if (kv.Value.Count > 1)
                {
                    foreach (var instance in kv.Value.Keys)
                    {
                        var key = "Instance " + instance + (instance == origin ? " (origin)" : "");
                        changed |= Ensure(doc, existing, wanted, kv.Key, instance, LinkedObjects.GetInstancesPath(link), key);
                    }
                }
            }

            // Instances that are gone
            foreach (var kv in existing.Where(x => !wanted.Contains(x.Key)).ToList())
            {
                doc.Map.Data.Remove(kv.Value);
                changed = true;
            }

            if (changed) await Oy.Publish("MapDocument:VisgroupsChanged", doc);
        }

        private static bool Ensure(Documents.MapDocument doc, Dictionary<(long, long), LinkedObjectsVisgroup> existing, HashSet<(long, long)> wanted, long groupId, long instance, string path, string key)
        {
            wanted.Add((groupId, instance));

            if (!existing.TryGetValue((groupId, instance), out var av))
            {
                av = new LinkedObjectsVisgroup(groupId, instance) { Path = path, Key = key };
                doc.Map.Data.Add(av);
                existing[(groupId, instance)] = av;
                return true;
            }

            if (av.Path == path && av.Key == key) return false;
            av.Path = path;
            av.Key = key;
            return true;
        }
    }

    /// <summary>
    /// Removes the "Linked Objects" entries that no longer have any objects.
    /// Runs after <see cref="VisgroupHandler"/> ("M"), which is what removes deleted objects from each visgroup.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectsVisgroupPruner : IMapDocumentChangeHandler
    {
        public string OrderHint => "N";

        public async Task Changed(Change change)
        {
            // Pruner only removes empty entries; visibility-only changes already skip the
            // fill step in VisgroupHandler/LinkedObjectsVisgroupHandler, and an empty
            // "Linked Objects" entry can only appear when links change, never when only
            // visibility flags change.
            if (VisibilityOnlyFastPath.IsActive(change)) return;

            var empty = change.Document.Map.Data.Get<LinkedObjectsVisgroup>().Where(x => x.Objects.Count == 0).ToList();
            if (empty.Count == 0) return;

            foreach (var av in empty)
            {
                change.Document.Map.Data.Remove(av);
            }

            await Oy.Publish("MapDocument:VisgroupsChanged", change.Document);
        }
    }
}
