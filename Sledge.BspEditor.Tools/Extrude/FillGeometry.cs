using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Extrude
{
    /// <summary>
    /// Builds the solid that fills the space between two faces: the convex hull of both faces' vertices.
    /// A convex hull is always a valid brush, and when the two faces face each other (the usual case) the hull
    /// has both of them as its end faces, so the new brush sits flush against both.
    /// </summary>
    internal static class FillGeometry
    {
        private const float Tolerance = 0.01f;
        private const int MaxPoints = 64;

        /// <summary>
        /// The faces of the hull as vertex loops in the editor's winding (the order that gives an outward-pointing plane).
        /// Null when the points are flat, a line, a single spot, or there are too many of them.
        /// </summary>
        public static List<List<Vector3>> ConvexHull(IEnumerable<Vector3> input)
        {
            // Merge points that sit on top of each other
            var points = new List<Vector3>();
            foreach (var p in input)
            {
                if (!points.Any(x => (x - p).LengthSquared() < Tolerance * Tolerance)) points.Add(p);
            }
            if (points.Count < 4 || points.Count > MaxPoints) return null;

            var planes = new List<(Vector3 normal, float distance)>();
            for (var i = 0; i < points.Count - 2; i++)
            {
                for (var j = i + 1; j < points.Count - 1; j++)
                {
                    for (var k = j + 1; k < points.Count; k++)
                    {
                        var n = Vector3.Cross(points[j] - points[i], points[k] - points[i]);
                        var length = n.Length();
                        if (length < 1e-3f) continue; // the three points are in a line
                        n /= length;
                        var d = Vector3.Dot(n, points[i]);

                        var above = false;
                        var below = false;
                        foreach (var p in points)
                        {
                            var s = Vector3.Dot(n, p) - d;
                            if (s > Tolerance) above = true;
                            else if (s < -Tolerance) below = true;
                            if (above && below) break;
                        }
                        if (above && below) continue; // points on both sides: not a face of the hull
                        if (!above && !below) return null; // every point is on this plane: the shape is flat

                        // The hull's interior is on the side the other points are on, so the outward normal points the other way
                        if (above)
                        {
                            n = -n;
                            d = -d;
                        }
                        if (planes.Any(x => Vector3.Dot(x.normal, n) > 0.9999f && Math.Abs(x.distance - d) < Tolerance)) continue;
                        planes.Add((n, d));
                    }
                }
            }
            if (planes.Count < 4) return null;

            var faces = new List<List<Vector3>>();
            foreach (var (normal, distance) in planes)
            {
                var onPlane = points.Where(p => Math.Abs(Vector3.Dot(normal, p) - distance) <= Tolerance).ToList();
                if (onPlane.Count < 3) return null;

                var loop = Order(onPlane, normal);
                if (loop.Count < 3) return null;
                faces.Add(loop);
            }
            return faces;
        }

        /// <summary>Puts coplanar points in order around their centre, drops points that don't make a corner, and winds the result like the editor does.</summary>
        private static List<Vector3> Order(List<Vector3> onPlane, Vector3 normal)
        {
            var centre = Vector3.Zero;
            foreach (var p in onPlane) centre += p;
            centre /= onPlane.Count;

            var u = onPlane.Select(p => p - centre).FirstOrDefault(x => x.LengthSquared() > 1e-6f);
            if (u == Vector3.Zero) return new List<Vector3>();
            u = Vector3.Normalize(u);
            var v = Vector3.Cross(normal, u);

            var sorted = onPlane.OrderBy(p => Math.Atan2(Vector3.Dot(p - centre, v), Vector3.Dot(p - centre, u))).ToList();

            // Points in the middle of an edge aren't corners, and three in a row would make a useless plane
            var corners = new List<Vector3>();
            for (var i = 0; i < sorted.Count; i++)
            {
                var previous = sorted[(i + sorted.Count - 1) % sorted.Count];
                var next = sorted[(i + 1) % sorted.Count];
                if (Vector3.Cross(sorted[i] - previous, next - sorted[i]).LengthSquared() < 1e-3f) continue;
                corners.Add(sorted[i]);
            }
            if (corners.Count < 3) return new List<Vector3>();

            // The editor's planes point outwards for the opposite winding to the obvious one: check, and turn the loop round if needed
            if (Vector3.Dot(new Plane(corners[0], corners[1], corners[2]).Normal, normal) < 0) corners.Reverse();
            return corners;
        }
    }
}
