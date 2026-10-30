using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.ChangeHandlers;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
    /// <summary>
    /// Draws the pick gizmo of model and sprite entities (env_sprite, cycler, cycler_sprite) in
    /// the 3D viewport: a wireframe box of half size PickGeometry.GizmoHalfSize centred on the
    /// origin. This is the same box the entity is picked with. Iconsprite gizmos (lights, spawns
    /// etc.) are skipped because they already draw their own icon. Does not stop other converters.
    /// </summary>
    [Export(typeof(IMapObjectSceneConverter))]
    public class EntityGizmoConverter : IMapObjectSceneConverter
    {
        public MapObjectSceneConverterPriority Priority => MapObjectSceneConverterPriority.DefaultLow;

        public bool ShouldStopProcessing(MapDocument document, IMapObject obj)
        {
            return false;
        }

        public bool Supports(IMapObject obj)
        {
            return obj is Entity e && HasGizmo(e);
        }

        private static bool HasGizmo(Entity ent)
        {
            var sprite = ent.Data.GetOne<EntitySprite>();
            if (sprite != null) return !sprite.IsGizmo;
            return ent.Data.GetOne<EntityModel>()?.Renderable != null;
        }

        public Task Convert(BufferBuilder builder, MapDocument document, IMapObject obj, ResourceCollector resourceCollector)
        {
            if (!(obj is Entity entity) || !HasGizmo(entity)) return Task.CompletedTask;

            var origin = entity.Data.GetOne<Origin>()?.Location ?? Vector3.Zero;
            var colour = (entity.Color?.Color ?? System.Drawing.Color.White).ToVector4();
            var h = PickGeometry.GizmoHalfSize;

            // Corner i uses bit 0 for X, bit 1 for Y, bit 2 for Z.
            var points = new VertexStandard[8];
            for (var i = 0; i < 8; i++)
            {
                var offset = new Vector3(
                    (i & 1) == 0 ? -h : h,
                    (i & 2) == 0 ? -h : h,
                    (i & 4) == 0 ? -h : h);
                points[i] = new VertexStandard { Position = origin + offset, Colour = colour };
            }

            // Twelve edges, each joining two corners that differ by exactly one bit.
            var indices = new List<uint>(24);
            foreach (var bit in new[] { 1, 2, 4 })
            {
                for (var i = 0; i < 8; i++)
                {
                    if ((i & bit) != 0) continue;
                    indices.Add((uint) i);
                    indices.Add((uint) (i | bit));
                }
            }

            builder.Append(points, indices, new[]
            {
                new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, 0, (uint) indices.Count)
            });

            return Task.CompletedTask;
        }
    }
}
