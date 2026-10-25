using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Primitives
{
    public static class SolidExtensions
    {
        public static bool Split(this Solid solid, UniqueNumberGenerator generator, Plane plane, out Solid back, out Solid front)
        {
            back = front = null;
            var pln = plane.ToPrecisionPlane();
            var poly = solid.ToPolyhedron().ToPrecisionPolyhedron();

            if (!poly.Split(pln, out var backPoly, out var frontPoly))
            {
                if (backPoly != null) back = solid;
                else if (frontPoly != null) front = solid;
                return false;
            }

            front = MakeSolid(generator, solid, frontPoly);
            back = MakeSolid(generator, solid, backPoly);
            return true;
        }

        private static Solid MakeSolid(UniqueNumberGenerator generator, Solid original, DataStructures.Geometric.Precision.Polyhedron poly)
        {
            var originalFaces = original.Faces.ToList();

            var solid = new Solid(generator.Next("MapObject")) { IsSelected = original.IsSelected };
            foreach (var p in poly.Polygons)
            {
                // Try and find the original face this polygon came from, so we can duplicate the texture values
                var originalFace = FindOriginalFace(originalFaces, p);

                var face = new Face(generator.Next("Face"));
                face.Vertices.AddRange(p.Vertices.Select(x => x.ToStandardVector3()));
                face.Plane = p.Plane.ToStandardPlane();

                if (originalFace != null)
                {
                    // The plane exists, so we can just apply the texture axes directly
                    face.Texture = originalFace.Texture.Clone();
                }
                else
                {
                    // No matching plane exists, so it's the clipping plane.
                    // Apply the first texture we find and align it with the face.
                    var firstFace = originalFaces[0];
                    face.Texture = firstFace.Texture.Clone();
                    face.Texture.AlignToNormal(face.Plane.Normal);
                }

                solid.Data.Add(face);
            }

            // Add any extra data (visgroups, colour, etc)
            foreach (var data in original.Data.Where(x => !(x is Face)))
            {
                solid.Data.Add((IMapObjectData)data.Clone());
            }
            return solid;
        }

        /// <summary>
        /// Finds the original face whose plane contains every vertex of the polygon.
        /// Uses the face's real vertices (Newell normal) rather than Face.Plane, which is
        /// built from the first three vertices and can be degenerate after earlier clips.
        /// Returns null if no original face is close enough (i.e. the polygon is a new cap).
        /// </summary>
        private static Face FindOriginalFace(IList<Face> faces, DataStructures.Geometric.Precision.Polygon polygon)
        {
            const float tolerance = 0.05f;
            var points = polygon.Vertices.Select(x => x.ToStandardVector3()).ToList();
            if (points.Count < 3) return null;

            Face best = null;
            var bestError = float.MaxValue;
            foreach (var face in faces)
            {
                var verts = face.Vertices;
                if (verts.Count < 3) continue;

                // Newell's method: robust normal for any non-degenerate polygon
                var normal = System.Numerics.Vector3.Zero;
                for (var i = 0; i < verts.Count; i++)
                {
                    var a = verts[i];
                    var b = verts[(i + 1) % verts.Count];
                    normal += new System.Numerics.Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
                }
                if (normal.LengthSquared() < 1e-12f) continue;
                normal = System.Numerics.Vector3.Normalize(normal);

                var distance = System.Numerics.Vector3.Dot(normal, verts[0]);
                var error = points.Max(x => Math.Abs(System.Numerics.Vector3.Dot(normal, x) - distance));
                if (error < bestError)
                {
                    bestError = error;
                    best = face;
                }
            }

            return bestError <= tolerance ? best : null;
        }

        public static bool IsValid(this Solid solid)
        {
            return solid.ToPolyhedron().IsValid();
        }
    }
}