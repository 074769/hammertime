using System.Collections.Generic;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    public interface IMapObject2DOverlay
    {
        /// <summary>
        /// </summary>
        /// <param name="document">
        /// The active map document. Overlays that only need the map objects
        /// themselves can ignore this; overlays that need the loaded FGD game
        /// data (e.g. to look up spawnflag definitions) can use
        /// <c>document.Environment.GetGameData()</c>. May be null.
        /// </param>
        void Render(IViewport viewport, ICollection<IMapObject> objects, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im, MapDocument document);
    }
}