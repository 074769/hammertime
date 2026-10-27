using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;

namespace Sledge.BspEditor.Tools.DragSelect
{
    /// <summary>
    /// Edge rule shared by the click+drag (paint) selection tools. A point within EdgePixels of a face edge,
    /// measured in screen space, counts as being on the edge. A press there still clicks, but it doesn't start
    /// a sweep, and a sweep ignores hits there so it can't flip onto a neighbouring face.
    /// </summary>
    public static class FaceEdgeHitTest
    {
        /// <summary>Screen distance in pixels within which a hit counts as being on an edge.</summary>
        public const float EdgePixels = 3f;

        public static bool IsOnFaceEdge(PerspectiveCamera camera, IEnumerable<Vector3> faceVertices, Vector3 hit)
        {
            var verts = faceVertices.ToList();
            if (verts.Count < 2) return false;

            var p = camera.WorldToScreen(hit);
            for (var i = 0; i < verts.Count; i++)
            {
                var a = camera.WorldToScreen(verts[i]);
                var b = camera.WorldToScreen(verts[(i + 1) % verts.Count]);
                if (DistanceToSegment2D(p, a, b) < EdgePixels) return true;
            }
            return false;
        }

        /// <summary>True when the hit lands on the edge of any face of the object.</summary>
        public static bool IsOnObjectEdge(PerspectiveCamera camera, IMapObject obj, Vector3 hit)
        {
            return obj.GetPolygons().Any(poly => IsOnFaceEdge(camera, poly.Vertices, hit));
        }

        private static float DistanceToSegment2D(Vector3 p, Vector3 a, Vector3 b)
        {
            var abX = b.X - a.X;
            var abY = b.Y - a.Y;
            var lenSq = abX * abX + abY * abY;
            var t = 0f;
            if (lenSq > 0)
            {
                t = ((p.X - a.X) * abX + (p.Y - a.Y) * abY) / lenSq;
                if (t < 0) t = 0;
                if (t > 1) t = 1;
            }
            var dx = p.X - (a.X + t * abX);
            var dy = p.Y - (a.Y + t * abY);
            return (float) Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
