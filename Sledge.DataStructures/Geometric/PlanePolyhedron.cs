using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sledge.DataStructures.Geometric
{
    /// <summary>
    /// Builds a convex polyhedron from a set of face planes.
    /// Planes are stored in single precision, so the planes meeting at one vertex (an apex, for example)
    /// do not pass through exactly the same point. Splitting by those planes can lose the apex or leave
    /// near-duplicate vertices, and the solid is then reported as invalid. This builder solves each vertex
    /// from its three planes in double precision, merges vertices that lie within a tolerance of each other,
    /// and builds each face from the merged vertices.
    /// </summary>
    public static class PlanePolyhedron
    {
        /// <summary>Vertices closer together than this are treated as one vertex.</summary>
        public const double DefaultTolerance = 0.001d;

        /// <summary>
        /// Creates a polyhedron from face planes. Each plane's normal points out of the solid.
        /// Planes that only touch the solid at an edge or point produce no face.
        /// </summary>
        public static Polyhedron Build(IEnumerable<Plane> planes, double tolerance = DefaultTolerance)
        {
            var halves = planes.Select(p => new Half(p)).ToList();
            var vertices = FindVertices(halves, tolerance);

            var polygons = new List<Polygon>();
            foreach (var half in halves)
            {
                var on = vertices.Where(v => Math.Abs(Dot(half.N, v) - half.D) <= tolerance).ToList();
                if (on.Count < 3) continue;

                var ordered = OrderClockwise(on, half.N);
                var polygon = new Polygon(ordered.Select(v => new Vector3((float) v.X, (float) v.Y, (float) v.Z)));
                if (polygon.Vertices.Count >= 3) polygons.Add(polygon);
            }
            return new Polyhedron(polygons);
        }

        /// <summary>
        /// Solves every triple of planes for its intersection point, keeps the points that lie inside all the
        /// planes, and merges points that are within the tolerance of each other.
        /// </summary>
        private static List<D3> FindVertices(List<Half> planes, double tolerance)
        {
            var candidates = new List<D3>();
            for (var i = 0; i < planes.Count; i++)
            {
                for (var j = i + 1; j < planes.Count; j++)
                {
                    for (var k = j + 1; k < planes.Count; k++)
                    {
                        var a = planes[i];
                        var b = planes[j];
                        var c = planes[k];

                        var bc = Cross(b.N, c.N);
                        var det = Dot(a.N, bc);
                        if (Math.Abs(det) < 1e-12) continue;

                        var x = (a.D * bc + b.D * Cross(c.N, a.N) + c.D * Cross(a.N, b.N)) / det;
                        if (planes.All(p => Dot(p.N, x) - p.D <= tolerance)) candidates.Add(x);
                    }
                }
            }

            var groups = new List<List<D3>>();
            foreach (var x in candidates)
            {
                var group = groups.FirstOrDefault(g => Length(x - Mean(g)) < tolerance);
                if (group != null) group.Add(x);
                else groups.Add(new List<D3> { x });
            }
            return groups.Select(Mean).ToList();
        }

        /// <summary>
        /// Orders coplanar points clockwise when seen from outside the solid (along the normal), which is the
        /// winding the editor's polygons use. The start is chosen so the first three points span the largest
        /// triangle, which keeps Polygon.Plane (computed from the first three vertices) well conditioned.
        /// </summary>
        private static List<D3> OrderClockwise(List<D3> points, D3 normal)
        {
            var centre = Mean(points);
            var u = Normalise(points[0] - centre);
            var w = Cross(normal, u);

            // Counter-clockwise seen from the normal, reversed for clockwise seen from outside
            var ordered = points
                .OrderBy(v => Math.Atan2(Dot(v - centre, w), Dot(v - centre, u)))
                .Reverse()
                .ToList();

            var count = ordered.Count;
            var start = 0;
            var bestArea = -1d;
            for (var s = 0; s < count; s++)
            {
                var area = Length(Cross(ordered[(s + 1) % count] - ordered[s], ordered[(s + 2) % count] - ordered[s]));
                if (area > bestArea)
                {
                    bestArea = area;
                    start = s;
                }
            }
            return ordered.Skip(start).Concat(ordered.Take(start)).ToList();
        }

        private static D3 Mean(List<D3> points)
        {
            var sum = points.Aggregate(new D3(0, 0, 0), (s, v) => s + v);
            return sum / points.Count;
        }

        private static double Dot(D3 a, D3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static D3 Cross(D3 a, D3 b) => new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        private static double Length(D3 a) => Math.Sqrt(Dot(a, a));

        private static D3 Normalise(D3 a)
        {
            var length = Length(a);
            return length > 0 ? a / length : a;
        }

        /// <summary>A plane in double precision: Dot(N, x) - D is positive outside the solid.</summary>
        private struct Half
        {
            public readonly D3 N;
            public readonly double D;

            public Half(Plane plane)
            {
                N = new D3(plane.Normal.X, plane.Normal.Y, plane.Normal.Z);
                D = plane.DistanceFromOrigin;
            }
        }

        /// <summary>A point or direction in double precision.</summary>
        private struct D3
        {
            public readonly double X;
            public readonly double Y;
            public readonly double Z;

            public D3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static D3 operator *(double s, D3 a) => new D3(s * a.X, s * a.Y, s * a.Z);
            public static D3 operator /(D3 a, double s) => new D3(a.X / s, a.Y / s, a.Z / s);
        }
    }
}
