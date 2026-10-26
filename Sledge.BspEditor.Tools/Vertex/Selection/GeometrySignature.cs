using System.Numerics;
using Sledge.BspEditor.Primitives;
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
            var hash = new HashCode();
            foreach (var face in solid.Faces)
            {
                hash.Add(face.Vertices.Count);
                foreach (var v in face.Vertices) hash.Add(v);
                AddTexture(ref hash, face.Texture);
            }
            return hash.ToHashCode();
        }

        public static int Of(MutableSolid solid)
        {
            var hash = new HashCode();
            foreach (var face in solid.Faces)
            {
                hash.Add(face.Vertices.Count);
                foreach (var v in face.Vertices) hash.Add(v.Position);
                AddTexture(ref hash, face.Texture);
            }
            return hash.ToHashCode();
        }

        private static void AddTexture(ref HashCode hash, Texture t)
        {
            if (t == null)
            {
                hash.Add(0);
                return;
            }
            hash.Add(t.Name);
            hash.Add(t.UAxis);
            hash.Add(t.VAxis);
            hash.Add(t.XShift);
            hash.Add(t.XScale);
            hash.Add(t.YShift);
            hash.Add(t.YScale);
        }
    }
}
