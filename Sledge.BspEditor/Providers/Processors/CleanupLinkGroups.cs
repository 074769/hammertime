using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;

namespace Sledge.BspEditor.Providers.Processors
{
    /// <summary>
    /// Removes link groups that have no members left, so they aren't saved into the map file.
    /// (If an undo brings a member back later, its group is recreated automatically.)
    /// </summary>
    [Export(typeof(IBspSourceProcessor))]
    public class CleanupLinkGroups : IBspSourceProcessor
    {
        public string OrderHint => "E";

        public Task AfterLoad(MapDocument document)
        {
            return Task.FromResult(0);
        }

        public Task BeforeSave(MapDocument document)
        {
            // Groups with members, and the groups above them (a sublink needs its parent)
            var used = LinkedObjects.GetKeptGroupIds(document);
            var unused = document.Map.Data.Get<LinkGroup>().Where(x => !used.Contains(x.ID)).ToList();
            foreach (var g in unused) document.Map.Data.Remove(g);
            return Task.FromResult(0);
        }
    }
}
