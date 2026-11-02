using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Scene;
using Sledge.Common.Logging;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    [Export(typeof(IMapDocumentOverlayRenderable))]
    public class MapObject2DOverlayManager : IMapDocumentOverlayRenderable
    {
        private readonly IEnumerable<Lazy<IMapObject2DOverlay>> _overlays;

        private readonly WeakReference<MapDocument> _document = new WeakReference<MapDocument>(null);

        /// <summary>
        /// The objects found in a view the last time, and what they were found for. The same list is handed to
        /// the overlays every frame until the map or the visible area changes.
        /// </summary>
        private sealed class VisibleObjects
        {
            public MapDocument Document;
            public long Revision;
            public Vector3 Min;
            public Vector3 Max;
            public List<IMapObject> Objects;
        }

        private readonly ConditionalWeakTable<IViewport, VisibleObjects> _visible = new ConditionalWeakTable<IViewport, VisibleObjects>();

        [ImportingConstructor]
        public MapObject2DOverlayManager(
            [ImportMany] IEnumerable<Lazy<IMapObject2DOverlay>> overlays
        )
        {
            _overlays = overlays;
        }

        public void SetActiveDocument(MapDocument doc)
        {
            _document.SetTarget(doc);
        }

        public void Render(IViewport viewport, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im)
        {
            if (!_overlays.Any()) return;
            if (!_document.TryGetTarget(out var doc)) return;

            // Determine which objects are visible
            // The result only depends on the map and the visible area, so it is reused until one of them changes
            var revision = DocumentRevision.Get(doc);
            var cache = _visible.GetOrCreateValue(viewport);
            if (cache.Objects == null || !ReferenceEquals(cache.Document, doc) || cache.Revision != revision || cache.Min != worldMin || cache.Max != worldMax)
            {
                var padding = Vector3.One * 100;
                var box = new Box(worldMin - padding, worldMax + padding);
                var findTimer = PerfLog.MeasureForView("2D find visible", viewport.ID);
                cache.Objects = doc.Map.Root.Find(x => x.BoundingBox.IntersectsWith(box)).ToList();
                cache.Document = doc;
                cache.Revision = revision;
                cache.Min = worldMin;
                cache.Max = worldMax;
                findTimer.Dispose();
            }
            var objects = cache.Objects;

            // Render the overlay for each object
            foreach (var overlay in _overlays)
            {
                using (PerfLog.MeasureForView("Overlay", overlay.Value, viewport.ID)) overlay.Value.Render(viewport, objects, camera, worldMin, worldMax, im, doc);
            }
            PerfLog.FlushIfDue("2D overlays (last 2 s)", 2000, "Overlay:", "2D find", "Labels:", "MovementOverlay");
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            // 2D only
        }
    }
}