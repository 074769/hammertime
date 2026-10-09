using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Overlay;
using Sledge.Rendering.Viewports;

namespace Sledge.BspEditor.Rendering.Overlay
{
    /// <summary>
    /// Renders entity and brush colour outlines in the 2D viewport using the colours configured by
    /// <see cref="EntityBrushColorSettings"/>, including per-entity-name overrides.
    /// </summary>
    [Export(typeof(IMapObject2DOverlay))]
    public class EntityBrushColorsOverlay : IMapObject2DOverlay
    {
        public void Render(IViewport viewport, ICollection<IMapObject> objects, OrthographicCamera camera, Vector3 worldMin, Vector3 worldMax, I2DRenderer im, MapDocument document)
        {
            var pointColour = EntityBrushColorSettings.PointEntityColour;
            var worldBrushColour = EntityBrushColorSettings.WorldBrushColour;
            var brushEntityColour = EntityBrushColorSettings.BrushEntityColour;
            var overrides = EntityBrushColorSettings.CustomEntityColours;

            foreach (var obj in objects)
            {
                if (obj is Entity entity)
                {
                    var box = entity.BoundingBox;
                    if (box != null && !box.IsEmpty())
                    {
                        DrawBoxOutline(im, camera, box, GetEntityColour(entity, overrides, pointColour, brushEntityColour));
                    }
                }
                else if (obj is Solid solid && solid.Hierarchy.Parent is Root)
                {
                    var box = solid.BoundingBox;
                    if (box != null && !box.IsEmpty())
                    {
                        DrawBoxOutline(im, camera, box, worldBrushColour);
                    }
                }
            }
        }

        private static Color GetEntityColour(Entity entity, List<EntityColourOverride> overrides, Color pointColour, Color brushEntityColour)
        {
            var entityName = entity.EntityData?.Name;
            if (!string.IsNullOrEmpty(entityName))
            {
                // Match case-insensitively and ignore surrounding whitespace so a manually-typed
                // entity class name still applies even if its casing differs from the map's.
                var match = overrides.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.EntityName)
                    && string.Equals(o.EntityName.Trim(), entityName.Trim(), System.StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Colour;
            }

            var hasFaceChildren = entity.Hierarchy.OfType<Solid>().Any(s => s.Faces.Any());
            return hasFaceChildren ? brushEntityColour : pointColour;
        }

        private void DrawBoxOutline(I2DRenderer im, OrthographicCamera camera, Box box, Color colour)
        {
            foreach (var face in box.GetBoxFaces())
            {
                var verts = new Vector2[face.Length];
                for (var i = 0; i < face.Length; i++)
                {
                    var v = camera.WorldToScreen(face[i]);
                    verts[i] = new Vector2(v.X, v.Y);
                }
                for (var i = 0; i < face.Length; i++)
                {
                    var a = verts[i];
                    var b = verts[(i + 1) % face.Length];
                    // Draw a wider black stroke behind the coloured stroke so the entity's
                    // colour reads clearly against any background fill (the same contrast trick
                    // EntityMovementOverlay uses for its helper arrows).
                    im.AddLine(a, b, Color.Black, 3f);
                    im.AddLine(a, b, colour, 2f);
                }
            }
        }
    }
}
