using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Primitives.MapData;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Removes "Entities by Class" auto visgroups (see <see cref="EntityClassAutoVisgroupHandler"/>)
    /// once the last entity of that classname has been deleted or renamed away, so the list only
    /// ever shows classnames that actually exist in the map right now.
    /// Runs after <see cref="VisgroupHandler"/> ("M"), which is what removes deleted/renamed
    /// objects from each auto visgroup's object set.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class EntityClassAutoVisgroupPruner : IMapDocumentChangeHandler
    {
        public string OrderHint => "N";

        public Task Changed(Change change)
        {
            if (!change.Removed.Any() && !change.Updated.Any()) return Task.CompletedTask;

            var empty = change.Document.Map.Data.Get<AutomaticVisgroup>()
                .Where(av => av.Path == EntityClassAutoVisgroupHandler.EntitiesByClassPath && av.Objects.Count == 0)
                .ToList();

            foreach (var av in empty)
            {
                change.Document.Map.Data.Remove(av);
            }

            return Task.CompletedTask;
        }
    }
}
