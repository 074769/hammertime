using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.ChangeHandling;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// Keeps storage groups consistent with the objects in the map.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class StorageChangeHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "N";

        public Task Changed(Change change)
        {
            var groups = StorageRules.Groups(change.Document).ToList();

            // A stored object that is deleted or cut from a visible group leaves the group.
            // Hidden groups take their objects out on purpose (SetStorageVisible), so those removals don't count.
            foreach (var o in change.Removed.ToList())
            {
                var id = o.Data.GetOne<StorageID>();
                if (id == null) continue;

                var group = groups.FirstOrDefault(x => x.ID == id.GroupID);
                if (group == null || !group.Visible) continue;

                if (group.Objects.Remove(o)) change.Update(group);
            }

            // Copies and pastes of stored objects aren't in a group, so they become ordinary objects
            foreach (var o in change.Added.ToList())
            {
                var id = o.Data.GetOne<StorageID>();
                if (id == null) continue;

                var group = groups.FirstOrDefault(x => x.ID == id.GroupID);
                if (group != null && group.Objects.Contains(o)) continue;

                o.Data.Remove(x => x is StorageID);
                change.Update(o);
            }

            return Task.FromResult(0);
        }
    }
}
