using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Tools.Vertex.Selection
{
    /// <summary>
    /// A hash of the geometry and texture mapping of a solid, i.e. everything the vertex tool needs to draw it.
    /// Changes that don't touch the geometry (such as the selection marker) leave the value unchanged.
    /// </summary>
    internal static class GeometrySignature
    {
        public static int Of(Solid solid)
        {
            var hash = 17;
            foreach (var face in solid.Faces)
            {
                hash = Mix(hash, face.Vertices.Count);
                foreach (var v in face.Vertices) hash = Mix(hash, v.GetHashCode());
                hash = AddTexture(hash, face.Texture);
            }
            return hash;
        }

        public static int Of(MutableSolid solid)
        {
            var hash = 17;
            foreach (var face in solid.Faces)
            {
                hash = Mix(hash, face.Vertices.Count);
                foreach (var v in face.Vertices) hash = Mix(hash, v.Position.GetHashCode());
                hash = AddTexture(hash, face.Texture);
            }
            return hash;
        }

        private static int AddTexture(int hash, Primitives.Texture t)
        {
            if (t == null) return Mix(hash, 0);
            hash = Mix(hash, t.Name?.GetHashCode() ?? 0);
            hash = Mix(hash, t.UAxis.GetHashCode());
            hash = Mix(hash, t.VAxis.GetHashCode());
            hash = Mix(hash, t.XShift.GetHashCode());
            hash = Mix(hash, t.XScale.GetHashCode());
            hash = Mix(hash, t.YShift.GetHashCode());
            hash = Mix(hash, t.YScale.GetHashCode());
            return hash;
        }

        private static int Mix(int hash, int value)
        {
            unchecked { return hash * 31 + value; }
        }
    }
}
