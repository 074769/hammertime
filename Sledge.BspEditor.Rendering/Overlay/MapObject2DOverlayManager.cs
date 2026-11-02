using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
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
            var padding = Vector3.One * 100;
            var box = new Box(worldMin - padding, worldMax + padding);
            var findTimer = PerfLog.MeasureForView("2D find visible", viewport.ID);
            var objects = doc.Map.Root.Find(x => x.BoundingBox.IntersectsWith(box)).ToList();
            findTimer.Dispose();

            // Render the overlay for each object
            foreach (var overlay in _overlays)
            {
                using (PerfLog.MeasureForView("Overlay", overlay.Value, viewport.ID)) overlay.Value.Render(viewport, objects, camera, worldMin, worldMax, im, doc);
            }
            PerfLog.FlushIfDue("2D overlays (last 2 s)", 2000, "Overlay:", "2D find", "MovementOverlay");
        }

        public void Render(IViewport viewport, PerspectiveCamera camera, I2DRenderer im)
        {
            // 2D only
        }
    }
}