using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Storage;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Providers.Processors
{
    /// <summary>
    /// Restores storage groups when a map is opened, and writes the storage file when the map is saved.
    /// </summary>
    [Export(typeof(IBspSourceProcessor))]
    public class RestoreStorage : IBspSourceProcessor
    {
        private readonly StorageSidecar _sidecar;

        [ImportingConstructor]
        public RestoreStorage([Import] StorageSidecar sidecar)
        {
            _sidecar = sidecar;
        }

        public string OrderHint => "E";

        public Task AfterLoad(MapDocument document)
        {
            _sidecar.Load(document);
            return Task.FromResult(0);
        }

        public Task BeforeSave(MapDocument document)
        {
            _sidecar.Save(document);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Saving under a new file name (Save As) writes the storage file next to the new map.
    /// </summary>
    [Export(typeof(IInitialiseHook))]
    public class SaveStorageHook : IInitialiseHook
    {
        private readonly StorageSidecar _sidecar;

        [ImportingConstructor]
        public SaveStorageHook([Import] StorageSidecar sidecar)
        {
            _sidecar = sidecar;
        }

        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Saved", Saved);
            return Task.CompletedTask;
        }

        private Task Saved(IDocument doc)
        {
            if (doc is MapDocument md) _sidecar.Save(md);
            return Task.CompletedTask;
        }
    }
}
