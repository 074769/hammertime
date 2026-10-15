using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Sledge.Common.Transport;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives.MapObjects
{
    /// <summary>
    /// The root node of a map object tree
    /// </summary>
    public class Root : BaseMapObject
    {
        public override bool IsSelected
        {
            get => false;
            set {}
        }

        public Root(long id) : base(id)
        {
        }

        public Root(SerialisedObject obj) : base(obj)
        {
        }

        [Export(typeof(IMapElementFormatter))]
        public class RootFormatter : StandardMapElementFormatter<Root> { }

        protected override Box GetBoundingBox()
        {
            // Empty groups report Box.Empty (a point at the origin), which must not stretch the map's box
            var boxes = Hierarchy.Where(x => !(x is Group && !x.Hierarchy.HasChildren)).Select(x => x.BoundingBox).ToList();
            return boxes.Count > 0 ? new Box(boxes) : Box.Empty;
        }
        
        public override IEnumerable<Polygon> GetPolygons()
        {
            // The root never contains geometry
            yield break;
        }

        protected override string SerialisedName => "Root";

        public override IEnumerable<IMapObject> Decompose(IEnumerable<Type> allowedTypes)
        {
            yield return this;
        }
    }
}