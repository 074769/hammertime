using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Interfaces;

namespace Sledge.BspEditor.Rendering.ChangeHandlers
{
    public class EntityModel : IMapObjectData, IContentsReplaced, IBoundingBoxProvider, IPickGeometryProvider
    {
        public string Name { get; }
        public IModelRenderable Renderable { get; }

        public bool ContentsReplaced => Renderable != null;

        public EntityModel(string name, IModelRenderable renderable)
        {
            Name = name;
            Renderable = renderable;
        }

        public EntityModel(SerialisedObject obj)
        {
            Name = obj.Get<string>("Name");
        }

        [Export(typeof(IMapElementFormatter))]
        public class ActiveTextureFormatter : StandardMapElementFormatter<EntityModel> { }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("Name", Name);
        }

        public Box GetBoundingBox(IMapObject obj)
        {
            if (Renderable == null) return null;

            var model = Renderable.Model;
            var origin = obj.Data.GetOne<Origin>()?.Location ?? Vector3.Zero;
            var (min, max) = Renderable.GetBoundingBox();
            return new Box(min, max);
            //return new Box(model.Mins + origin, model.Maxs + origin);
        }

        private List<Polygon> _pickPolygons;
        private IModel _pickModel;
        private int _pickSequence;
        private Vector3 _pickOrigin;
        private Vector3 _pickAngles;

        /// <summary>
        /// Picking geometry for a model entity: a gizmo box at the origin plus the triangles of the
        /// model (GoldSrc .mdl). The bounding box is not used for picking.
        /// </summary>
        public IEnumerable<Polygon> GetPickPolygons(IMapObject obj)
        {
            if (Renderable == null) return new Polygon[0];

            if (_pickPolygons == null || _pickModel != Renderable.Model || _pickSequence != Renderable.Sequence
                || _pickOrigin != Renderable.Origin || _pickAngles != Renderable.Angles)
            {
                _pickPolygons = BuildPickPolygons();
                _pickModel = Renderable.Model;
                _pickSequence = Renderable.Sequence;
                _pickOrigin = Renderable.Origin;
                _pickAngles = Renderable.Angles;
            }
            return _pickPolygons;
        }

        private List<Polygon> BuildPickPolygons()
        {
            var polygons = new List<Polygon>(PickGeometry.GizmoPolygons(Renderable.Origin));

            if (Renderable.Model is Sledge.Providers.Model.Mdl10.MdlModel mdl)
            {
                var transform = Renderable.GetModelTransformation();
                foreach (var tri in mdl.GetPickTriangles(Renderable.Sequence))
                {
                    var a = Vector3.Transform(tri[0], transform);
                    var b = Vector3.Transform(tri[1], transform);
                    var c = Vector3.Transform(tri[2], transform);
                    // Degenerate triangles have no usable plane
                    if (Vector3.Cross(b - a, c - a).LengthSquared() < 1e-8f) continue;
                    polygons.Add(new Polygon(new[] { a, b, c }));
                }
            }
            return polygons;
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            return Clone();
        }

        public IMapElement Clone()
        {
            return new EntityModel(Name, null);
        }

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject(nameof(EntityModel));
            so.Set(nameof(Name), Name);
            return so;
        }
    }
}