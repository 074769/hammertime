using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Maintains the "Linked Objects" automatic visgroup tree: one entry per link group that has members.
    /// Runs before <see cref="VisgroupHandler"/> ("M"), which fills each group with its member objects.
    /// If an object refers to a link group that doesn't exist (eg after an undo), the group is recreated.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectsVisgroupHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "L";

        public async Task Changed(Change change)
        {
            var doc = change.Document;
            var groups = doc.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());
            var visgroups = doc.Map.Data.Get<LinkedObjectsVisgroup>().GroupBy(x => x.GroupID).ToDictionary(x => x.Key, x => x.First());

            var changed = false;

            foreach (var mo in change.Added.Union(change.Updated))
            {
                var id = LinkedObjects.GetLinkId(mo);
                if (id == null) continue;

                if (!groups.TryGetValue(id.Value, out var group))
                {
                    group = new LinkGroup
                    {
                        ID = id.Value,
                        Name = "Link " + id.Value,
                        Colour = LinkedObjects.ColourFor(id.Value)
                    };
                    doc.Map.Data.Add(group);
                    groups[group.ID] = group;
                }

                if (!visgroups.ContainsKey(group.ID))
                {
                    var av = new LinkedObjectsVisgroup(group.ID)
                    {
                        Path = LinkedObjects.AutoVisgroupPath,
                        Key = group.Name
                    };
                    doc.Map.Data.Add(av);
                    visgroups[group.ID] = av;
                    changed = true;
                }
            }

            // Keep the names up to date (renames)
            foreach (var av in visgroups.Values)
            {
                if (groups.TryGetValue(av.GroupID, out var group) && av.Key != group.Name)
                {
                    av.Key = group.Name;
                    changed = true;
                }
            }

            if (changed) await Oy.Publish("MapDocument:VisgroupsChanged", doc);
        }
    }

    /// <summary>
    /// Removes the "Linked Objects" entries that no longer have any members.
    /// Runs after <see cref="VisgroupHandler"/> ("M"), which is what removes deleted objects from each visgroup.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectsVisgroupPruner : IMapDocumentChangeHandler
    {
        public string OrderHint => "N";

        public async Task Changed(Change change)
        {
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
