using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Resources;

using Sledge.BspEditor.Rendering.Debugging;

namespace Sledge.BspEditor.Rendering.Scene
{
    public class SceneBuilder : IDisposable
    {
        private readonly EngineInterface _engine;
        private readonly object _lock = new object();

        private readonly Dictionary<long, BufferBuilder> _bufferBuilders;

        /// <summary>
        /// A snapshot of the group buffers. The scene draws from this while the converter may be swapping groups.
        /// </summary>
        public IEnumerable<BufferBuilder> BufferBuilders
        {
            get { lock (_lock) return _bufferBuilders.Values.ToList(); }
        }
        public IRenderable SceneBuilderRenderable { get; }

        private readonly Dictionary<long, HashSet<IRenderable>> _renderables;

        public SceneBuilder(EngineInterface engine)
        {
            _engine = engine;
            _bufferBuilders = new Dictionary<long, BufferBuilder>();
            _renderables = new Dictionary<long, HashSet<IRenderable>>();

            SceneBuilderRenderable = new SceneBuilderRenderable(this);
        }

        public void AddRenderablesToGroup(long group, IEnumerable<IRenderable> renderables)
        {
            EnsureGroupExists(group);
            _renderables[group].UnionWith(renderables);
        }

        public void RemoveRenderablesFromGroup(long group, IEnumerable<IRenderable> renderables)
        {
            EnsureGroupExists(group);
            _renderables[group].ExceptWith(renderables);
        }

        public IEnumerable<IRenderable> GetRenderablesForGroup(long group)
        {
            if (_renderables.ContainsKey(group)) return _renderables[group];
            return new IRenderable[0];
        }

        public IEnumerable<IRenderable> GetAllRenderables() => _renderables.Values.SelectMany(x => x);

        public BufferBuilder GetBufferForGroup(long group)
        {
            return _bufferBuilders.ContainsKey(group) ? _bufferBuilders[group] : null;
        }

        public void Clear()
        {
            foreach (var bb in _bufferBuilders) bb.Value.Dispose();
            _bufferBuilders.Clear();
            _renderables.Clear();
        }

        public void DeleteGroup(long group)
        {
            if (_bufferBuilders.ContainsKey(group))
            {
                var bb = _bufferBuilders[group];
                bb.Dispose();
                _bufferBuilders.Remove(group);
            }

            if (_renderables.ContainsKey(group))
            {
                _renderables.Remove(group);
            }
        }

        /// <summary>
        /// Creates a buffer for rebuilding a group. It is not part of the scene until <see cref="SwapGroup"/> is called.
        /// </summary>
        public BufferBuilder CreateStagingBuffer()
        {
            return _engine.CreateBufferBuilder(BufferSize.Medium);
        }

        /// <summary>
        /// Replaces a group's geometry in one step. The old buffer stays in the scene until the new one is complete,
        /// so the group is never missing while it is being rebuilt.
        /// </summary>
        public void SwapGroup(long group, BufferBuilder buffer, IEnumerable<IRenderable> renderables)
        {
            lock (_lock)
            {
                if (RenderDebug.Enabled) RenderDebug.Write("scene", $"swap group {group}: {buffer.NumBuffers} buffer(s), replacing {(_bufferBuilders.ContainsKey(group) ? "existing" : "nothing")}");
                buffer.Complete();

                if (_bufferBuilders.TryGetValue(group, out var old) && !ReferenceEquals(old, buffer))
                {
                    old.Dispose();
                }
                _bufferBuilders[group] = buffer;
                _renderables[group] = new HashSet<IRenderable>(renderables);
            }
        }

        public void EnsureGroupExists(long group)
        {
            if (!_bufferBuilders.ContainsKey(group)) _bufferBuilders[group] = _engine.CreateBufferBuilder(BufferSize.Medium);
            if (!_renderables.ContainsKey(group)) _renderables[group] = new HashSet<IRenderable>();
        }

        public void Complete()
        {
            foreach (var bb in _bufferBuilders)
            {
                bb.Value.Complete();
            }
        }

        public void Dispose()
        {
            SceneBuilderRenderable.Dispose();
            foreach (var bb in _bufferBuilders)
            {
                bb.Value.Dispose();
            }
            _bufferBuilders.Clear();
            _renderables.Clear();
        }
    }
}