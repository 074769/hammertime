using System;
using System.Collections.Generic;
using System.Numerics;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Pure geometry for the spin tool. A spin sweeps the selection's profile (its
    /// radial and vertical extents) around the axis. Every piece is a wedge: two
    /// radial seam planes through the pivot, an inner and an outer plane square to
    /// the slot's middle direction, and a top and bottom plane. Neighbouring wedges
    /// share a seam plane exactly, so they always connect, whatever the shape of the
    /// selected solid was.
    /// </summary>
    public static class SpinGeometry
    {
        /// <summary>Unit vector in the spin plane at the given angle.</summary>
        public static Vector3 Direction(Vector3 u, Vector3 v, float degrees)
        {
            var radians = degrees * MathF.PI / 180f;
            return u * MathF.Cos(radians) + v * MathF.Sin(radians);
        }

        /// <summary>The direction in which the angle increases at the given angle.</summary>
        public static Vector3 Tangent(Vector3 axis, Vector3 u, Vector3 v, float degrees)
        {
            return Vector3.Cross(axis, Direction(u, v, degrees)).Normalise();
        }

        /// <summary>
        /// The extents of a set of vertices measured along a radial direction (from the
        /// pivot) and along the axis. Returns false when the profile can't be swept:
        /// it reaches the axis or lies behind it, or has no thickness.
        /// </summary>
        public static bool TryGetProfile(IEnumerable<Vector3> vertices, Vector3 pivot, Vector3 axis, Vector3 direction,
            out float rMin, out float rMax, out float zMin, out float zMax)
        {
            rMin = zMin = float.MaxValue;
            rMax = zMax = float.MinValue;

            var any = false;
            foreach (var vertex in vertices)
            {
                var relative = vertex - pivot;
                var r = Vector3.Dot(relative, direction);
                var z = Vector3.Dot(relative, axis);
                rMin = Math.Min(rMin, r);
                rMax = Math.Max(rMax, r);
                zMin = Math.Min(zMin, z);
                zMax = Math.Max(zMax, z);
                any = true;
            }

            if (!any) return false;
            return rMin > 0.5f && rMax - rMin > 0.01f && zMax - zMin > 0.01f;
        }

        /// <summary>
        /// The six outward-facing planes of the wedge centred on <paramref name="midDegrees"/>
        /// and spanning <paramref name="sweepDegrees"/> (the sign doesn't matter here).
        /// </summary>
        public static List<Plane> WedgePlanes(Vector3 pivot, Vector3 axis, Vector3 u, Vector3 v,
            float midDegrees, float sweepDegrees, float rMin, float rMax, float zMin, float zMax)
        {
            var half = Math.Abs(sweepDegrees) / 2f;
            var lo = midDegrees - half;
            var hi = midDegrees + half;
            var d = Direction(u, v, midDegrees);

            return new List<Plane>
            {
                new Plane(-Tangent(axis, u, v, lo), pivot),   // seam at the low angle
                new Plane(Tangent(axis, u, v, hi), pivot),    // seam at the high angle
                new Plane(-d, pivot + d * rMin),              // inner
                new Plane(d, pivot + d * rMax),               // outer
                new Plane(-axis, pivot + axis * zMin),        // bottom
                new Plane(axis, pivot + axis * zMax),         // top
            };
        }

        /// <summary>
        /// Builds the wedge from its planes. Returns null unless the result is a valid
        /// convex solid with exactly six faces.
        /// </summary>
        public static Polyhedron BuildWedge(IEnumerable<Plane> planes)
        {
            var wedge = new Polyhedron(planes);
            if (wedge.Polygons.Count != 6) return null;
            return wedge.IsValid() ? wedge : null;
        }
    }
}
