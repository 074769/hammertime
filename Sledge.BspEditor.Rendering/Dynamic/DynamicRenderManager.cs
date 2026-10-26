using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Resources;

using System.Linq;
using Sledge.BspEditor.Rendering.Debugging;

namespace Sledge.BspEditor.Rendering.Dynamic
{
    [Export(typeof(IStartupHook))]
    public class DynamicRenderManager : IStartupHook, IUpdateable
    {
        #pragma warning disable CS0649
        [Import] private Lazy<EngineInterface> _engine;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [ImportMany] private IDynamicRenderable[] _dynamicRenderables;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [ImportMany] private IMapObjectDynamicRenderable[] _documentDynamicRenderables;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [Import] private ResourceCollection _resourceCollection;
        #pragma warning restore CS0649

        private BufferBuilder _builder;
        private BufferBuilderRenderable _renderable;

        /// <inheritdoc />
        public Task OnStartup()
        {
            Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated);
            Oy.Subscribe<IDocument>("Document:Closed", DocumentClosed);

            _engine.Value.Add(this);

            return Task.FromResult(0);
        }

        private readonly WeakReference<MapDocument> _activeDocument = new WeakReference<MapDocument>(null);

        public void Update(long frame)
        {
            RenderDebug.SetFrame(frame);
            var builder = _engine.Value.CreateBufferBuilder(BufferSize.Small);
            var renderable = new BufferBuilderRenderable(builder);
            
            if (_activeDocument.TryGetTarget(out var md))
            {
                var resourceCollector = new ResourceCollector();

                foreach (var dr in _dynamicRenderables)
                {
                    dr.Render(builder, resourceCollector);
                }

                foreach (var ddr in _documentDynamicRenderables)
                {
                    ddr.Render(md, builder, resourceCollector);
                }

                var env = md.Environment;
                if (env != null) Task.Run(() => _resourceCollection.Upload(env, resourceCollector)).Wait();
            }

            builder.Complete();
            if (RenderDebug.Enabled) RenderDebug.Write("overlay", $"built {builder.NumBuffers} buffer(s), {builder.IndirectBufferGroups.Sum(l => l.Count)} group(s)");
            _engine.Value.Add(renderable);

            if (_builder != null)
            {
                _engine.Value.Remove(_renderable);
                if (RenderDebug.Enabled) RenderDebug.Write("overlay", "disposing previous overlay buffer");
                _renderable.Dispose();
                _builder.Dispose();
            }

            _builder = builder;
            _renderable = renderable;
        }

        // Document events

        private Task DocumentActivated(IDocument doc)
        {
            _activeDocument.SetTarget(doc as MapDocument);
            return Task.CompletedTask;
        }

        private Task DocumentClosed(IDocument doc)
        {
            if (_activeDocument.TryGetTarget(out var md) && md == doc)
            {
                _activeDocument.SetTarget(null);
            }
            return Task.CompletedTask;
        }
    }
}
