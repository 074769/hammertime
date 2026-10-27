using System.Collections.Generic;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives.MapObjectData
{
    /// <summary>
    /// Supplies the geometry an object is picked with (3D click, 2D click and box selection),
    /// replacing the bounding box faces. Return null to keep the default bounding-box picking,
    /// or an empty list to disable box-based picking for the object.
    /// </summary>
    public interface IPickGeometryProvider : IMapObjectData
    {
        IEnumerable<Polygon> GetPickPolygons(IMapObject obj);
    }

    public static class PickGeometry
    {
        /// <summary>Half size of the selection gizmo drawn around an entity origin.</summary>
        public const float GizmoHalfSize = 8f;

        /// <summary>The six faces of the selection gizmo box centred on the origin.</summary>
        public static IEnumerable<Polygon> GizmoPolygons(Vector3 origin)
        {
            var h = Vector3.One * GizmoHalfSize;
            var box = new Box(origin - h, origin + h);
            foreach (var face in box.GetBoxFaces()) yield return new Polygon(face);
        }
    }
}
