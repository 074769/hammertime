using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Scene;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
    [Export]
    public class MapObjectConverter
    {
        private readonly IEnumerable<Lazy<IMapObjectSceneConverter>> _converters;
        private readonly IEnumerable<Lazy<IMapObjectGroupSceneConverter>> _groupConverters;

        [ImportingConstructor]
        public MapObjectConverter(
            [ImportMany] IEnumerable<Lazy<IMapObjectSceneConverter>> converters,
            [ImportMany] IEnumerable<Lazy<IMapObjectGroupSceneConverter>> groupConverters
        )
        {
            _converters = converters;
            _groupConverters = groupConverters;
        }
        public async Task ConvertSky(MapDocument document, SceneBuilder builder, ResourceCollector resourceCollector)
        {
			var converter = _converters.FirstOrDefault(x=>x.Value is SkyboxConverter).Value;
            if (converter == null) return;
            // Group 0 may not exist yet while an async rebuild is still in progress (it is cleared on full rebuilds).
            // Create it so the sky can be added; the rebuild swaps group 0 later, which replaces this buffer.
            builder.EnsureGroupExists(0);
            var buffer = builder.GetBufferForGroup(0);
            if (buffer == null) return;
            await converter.Convert(buffer, document, null, resourceCollector);
		}

        public async Task Convert(MapDocument document, SceneBuilder builder, IEnumerable<IMapObject> affected, ResourceCollector resourceCollector)
        {
            var objs = document.Map.Root.FindAll().ToList();

            // A full rebuild covers every group in the map. An incremental one covers the groups that changed,
            // including groups that are now empty, so those get cleared too.
            var groupKeys = (affected ?? objs).Select(x => x.ID / 200).Distinct().OrderBy(x => x).ToList();
            var byGroup = objs.ToLookup(x => x.ID / 200);

            var converters = _converters.Select(x => x.Value).OrderBy(x => (int) x.Priority).ToList();
            var groupConverters = _groupConverters.Select(x => x.Value).OrderBy(x => (int) x.Priority).ToList();

            // Rebuild every group into its own staging buffer. The current buffers stay on screen until all of
            // them are ready, so no group disappears while its textures are loading.
            var staged = new List<(long Key, BufferBuilder Buffer, ResourceCollector Collector)>();
            foreach (var key in groupKeys)
            {
                var groupObjects = byGroup[key].ToList();
                var buffer = builder.CreateStagingBuffer();
                var collector = new ResourceCollector();

                foreach (var gc in groupConverters)
                {
                    try
                    {
                        await gc.Convert(buffer, document, groupObjects, collector);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError($"Group scene conversion failed for group {key}: {ex}");
                    }
                }

                foreach (var obj in groupObjects)
                {
                    foreach (var converter in converters)
                    {
                        if (!converter.Supports(obj)) continue;
                        try
                        {
                            await converter.Convert(buffer, document, obj, collector);
                        }
                        catch (Exception ex)
                        {
                            // One bad object must not stop the rest of its group from drawing
                            System.Diagnostics.Trace.TraceError($"Scene conversion failed for object {obj.ID}: {ex}");
                            break;
                        }
                        if (converter.ShouldStopProcessing(document, obj)) break;
                    }
                }

                staged.Add((key, buffer, collector));
            }

            // Every group is ready: swap them in together
            foreach (var s in staged)
            {
                resourceCollector.RemoveRenderables(builder.GetRenderablesForGroup(s.Key));
                builder.SwapGroup(s.Key, s.Buffer, s.Collector.GetRenderablesToAdd());
                resourceCollector.Merge(s.Collector);
            }

            builder.Complete();
        }
    }
}
