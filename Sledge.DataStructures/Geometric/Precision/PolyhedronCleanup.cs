using System;
using System.Collections.Generic;
using System.Linq;

namespace Sledge.DataStructures.Geometric.Precision
{
    /// <summary>
    /// Tidies up a polyhedron that was built by intersecting planes (e.g. after a clip), where vertices that
    /// should be the same point can come out slightly apart and tiny or collapsed faces can appear.
    /// </summary>
    public static class PolyhedronCleanup
    {
        public const double DefaultTolerance = 0.01d;

        /// <summary>
        /// Merges vertices that are closer together than the tolerance (across all polygons, so neighbouring faces
        /// share exactly the same points), removes repeated vertices inside each polygon, and drops polygons that
        /// collapse to fewer than 3 vertices or to no area.
        /// Returns null if fewer than 4 polygons are left, because that is not a solid (a sliver).
        /// </summary>
        public static Polyhedron Weld(Polyhedron poly, double tolerance = DefaultTolerance)
        {
            var canonical = new List<Vector3>();
            Vector3 Canon(Vector3 v)
            {
                foreach (var c in canonical)
                {
                    if ((c - v).LengthSquared() <= tolerance * tolerance) return c;
                }
                canonical.Add(v);
                return v;
            }

            var result = new List<Polygon>();
            foreach (var polygon in poly.Polygons)
            {
                var verts = new List<Vector3>();
                foreach (var v in polygon.Vertices)
                {
                    var c = Canon(v);
                    if (verts.Count == 0 || verts[verts.Count - 1] != c) verts.Add(c);
                }
                while (verts.Count > 1 && verts[0] == verts[verts.Count - 1]) verts.RemoveAt(verts.Count - 1);
                if (verts.Count < 3) continue;

                // Newell normal; its length is twice the polygon area
                var normal = Vector3.Zero;
                for (var i = 0; i < verts.Count; i++)
                {
                    var a = verts[i];
                    var b = verts[(i + 1) % verts.Count];
                    normal += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
                }
                if (normal.Length() < tolerance * tolerance) continue;

                // Polygon.Plane is built from the first three vertices, so start at the corner that gives
                // the most reliable (largest, least collinear) triangle
                var start = 0;
                var best = -1d;
                for (var i = 0; i < verts.Count; i++)
                {
                    var cross = (verts[(i + 1) % verts.Count] - verts[i]).Cross(verts[(i + 2) % verts.Count] - verts[(i + 1) % verts.Count]);
                    if (cross.Dot(normal) > best)
                    {
                        best = cross.Dot(normal);
                        start = i;
                    }
                }
                if (best <= 0) continue;
                result.Add(new Polygon(verts.Skip(start).Concat(verts.Take(start))));
            }

            return result.Count < 4 ? null : new Polyhedron(result);
        }
    }
}
