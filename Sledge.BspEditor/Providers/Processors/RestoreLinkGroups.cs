using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Providers.Processors
{
    /// <summary>
    /// Brings link groups back when a map is opened from a format that can't store them.
    /// Runs after <see cref="HandleVisgroups"/> ("C") and the invalid object cleanup ("D") so only real objects are matched.
    /// </summary>
    [Export(typeof(IBspSourceProcessor))]
    public class RestoreLinkGroups : IBspSourceProcessor
    {
        public string OrderHint => "F";

        public Task AfterLoad(MapDocument document)
        {
            LinkSidecar.Load(document);
            return Task.FromResult(0);
        }

        public Task BeforeSave(MapDocument document)
        {
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Writes the links file whenever a map is saved to its file (not for exports or compiles).
    /// </summary>
    [Export(typeof(IInitialiseHook))]
    public class SaveLinkGroupsHook : IInitialiseHook
    {
        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Saved", Saved);
            return Task.CompletedTask;
        }

        private Task Saved(IDocument doc)
        {
            if (doc is MapDocument md) LinkSidecar.Save(md);
            return Task.CompletedTask;
        }
    }
}
