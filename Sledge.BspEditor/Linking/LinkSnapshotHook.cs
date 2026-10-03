using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// Takes the starting snapshots of linked solids when a document is opened or switched to.
    /// Without them the first edit to a freshly opened map couldn't be told apart from the state before it.
    /// </summary>
    [Export(typeof(IInitialiseHook))]
    public class LinkSnapshotHook : IInitialiseHook
    {
        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Activated", Activated);
            return Task.CompletedTask;
        }

        private Task Activated(IDocument doc)
        {
            if (doc is MapDocument md) LinkGeometry.SeedAll(md);
            return Task.CompletedTask;
        }
    }
}
