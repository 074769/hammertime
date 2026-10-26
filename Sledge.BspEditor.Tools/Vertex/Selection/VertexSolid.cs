using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Tools.Vertex.Selection
{
    public class VertexSolid
    {
        public Solid Real { get; set; }
        public MutableSolid Copy { get; set; }
        public bool IsDirty { get; set; }

        /// <summary>
        /// Geometry signature of Real at the moment Copy was taken from it
        /// </summary>
        public int SourceSignature { get; private set; }

        public VertexSolid(Solid solid)
        {
            Real = solid;
            Copy = new MutableSolid(solid);
            SourceSignature = GeometrySignature.Of(solid);
        }

        public void Reset()
        {
            Copy = new MutableSolid(Real);
            IsDirty = false;
            SourceSignature = GeometrySignature.Of(Real);
        }
    }
}