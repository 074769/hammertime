using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Overlay;

namespace Sledge.BspEditor.Rendering.Overlay
{
    [Export(typeof(IStartupHook))]
    public class OverlayManager : IStartupHook
    {
        #pragma warning disable CS0649
        [Import] private Lazy<EngineInterface> _engine;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [ImportMany] private IOverlayRenderable[] _overlayRenderables;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [ImportMany] private IMapDocumentOverlayRenderable[] _documentOverlayRenderables;
        #pragma warning restore CS0649

        /// <inheritdoc />
        public Task OnStartup()
        {
            Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated);
            Oy.Subscribe<IDocument>("Document:Closed", DocumentClosed);

            foreach (var or in _overlayRenderables.Union(_documentOverlayRenderables))
            {
                _engine.Value.Add(or);
            }

            return Task.FromResult(0);
        }

        private WeakReference<MapDocument> _activeDocument = new WeakReference<MapDocument>(null);

        // Document events

        private async Task DocumentActivated(IDocument doc)
        {
            var md = doc as MapDocument;
            _activeDocument = new WeakReference<MapDocument>(md);
            await UpdateDocument(md);
        }

        private async Task DocumentClosed(IDocument doc)
        {
            if (_activeDocument.TryGetTarget(out var md) && md == doc)
            {
                await UpdateDocument(null);
            }
        }

        private Task UpdateDocument(MapDocument doc)
        {
            foreach (var dor in _documentOverlayRenderables)
            {
                dor.SetActiveDocument(doc);
            }

            return Task.CompletedTask;
        }
    }
}