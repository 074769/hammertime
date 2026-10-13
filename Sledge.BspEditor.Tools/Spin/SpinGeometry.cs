using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Pure geometry for the spin tool.
    ///
    /// A spin sweeps a profile around an axis. The profile is a solid that is extruded
    /// along one direction (the tangent): its side faces are parallel to that direction
    /// and its two end caps are square to it. Sweeping keeps every side face exactly as
    /// it is (same tilt, same texture) and replaces the two end caps by radial seam
    /// planes through the pivot. Each slot gets the side faces turned to the slot's
    /// middle direction, so neighbouring pieces meet on their shared seam exactly.
    /// </summary>
    public static class SpinGeometry
    {
        /// <summary>Faces within this of parallel to the extrusion direction are side faces.</summary>
        private const float SideTolerance = 0.02f;

        /// <summary>Faces within this of square to the extrusion direction are end caps.</summary>
        private const float CapTolerance = 0.98f;

        public enum FaceKind
        {
            /// <summary>Parallel to the extrusion direction: kept as part of the profile.</summary>
            Side,

            /// <summary>Square to the extrusion direction: replaced by a seam.</summary>
            Cap,

            /// <summary>Neither: the solid can't be swept without changing its shape.</summary>
            Other
        }

        /// <summary>One plane of a swept piece. <see cref="Source"/> is the index of the original face it came from, or -1 for a seam.</summary>
        public struct SweepPlane
        {
            public Vector3 Normal;
            public Vector3 Point;
            public int Source;

            public Plane ToPlane() => new Plane(Normal, Point);
        }

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

        public static FaceKind Classify(Vector3 normal, Vector3 tangent)
        {
            var dot = Math.Abs(Vector3.Dot(normal, tangent));
            if (dot <= SideTolerance) return FaceKind.Side;
            if (dot >= CapTolerance) return FaceKind.Cap;
            return FaceKind.Other;
        }

        /// <summary>
        /// Finds the profile's own direction: the angle (degrees, in the spin plane) of the
        /// radial line from the pivot along which the profile lies. The extrusion direction
        /// is the one direction most faces are either parallel or square to. When that is
        /// ambiguous (a plain box) the one pointing at the selection wins.
        /// </summary>
        /// <param name="normals">Face normals of every selected solid.</param>
        /// <param name="offset">Vector from the pivot to the middle of the selection.</param>
        public static bool TryFindProfileAngle(IReadOnlyList<Vector3> normals, Vector3 axis, Vector3 u, Vector3 v, Vector3 offset, out float angle)
        {
            angle = 0f;

            // Where the selection lies, in the spin plane
            var planar = offset - axis * Vector3.Dot(offset, axis);
            if (planar.Length() < 1f) return false;
            var towards = planar.Normalise();

            // A spin leaves many faces with the same few normals, so work on the distinct ones.
            // (Scoring every face against every candidate gets slow once hundreds of solids are selected.)
            var distinct = new Dictionary<(int, int, int), (Vector3 Normal, int Count)>();
            foreach (var n in normals)
            {
                var key = ((int) MathF.Round(n.X * 1000f), (int) MathF.Round(n.Y * 1000f), (int) MathF.Round(n.Z * 1000f));
                distinct[key] = distinct.TryGetValue(key, out var found) ? (found.Normal, found.Count + 1) : (n, 1);
            }
            var unique = distinct.Values.ToList();

            // Candidate extrusion directions come from the faces themselves
            var candidates = new Dictionary<(int, int, int), Vector3>();
            foreach (var entry in unique)
            {
                var flat = entry.Normal - axis * Vector3.Dot(entry.Normal, axis);
                if (flat.Length() < 0.2f) continue;
                flat = flat.Normalise();
                foreach (var c in new[] { flat, Vector3.Cross(axis, flat).Normalise() })
                {
                    candidates[((int) MathF.Round(c.X * 1000f), (int) MathF.Round(c.Y * 1000f), (int) MathF.Round(c.Z * 1000f))] = c;
                }
            }
            if (candidates.Count == 0) return false;

            var bestScore = -1;
            var bestAlign = -1f;
            var best = Vector3.Zero;
            foreach (var t in candidates.Values)
            {
                var score = 0;
                foreach (var entry in unique)
                {
                    var dot = Math.Abs(Vector3.Dot(entry.Normal, t));
                    if (dot <= SideTolerance || dot >= CapTolerance) score += entry.Count;
                }

                // The profile direction is square to the extrusion direction
                var radial = Vector3.Cross(t, axis).Normalise();
                var align = Math.Abs(Vector3.Dot(radial, towards));

                if (score > bestScore || (score == bestScore && align > bestAlign + 1e-4f))
                {
                    bestScore = score;
                    bestAlign = align;
                    best = t;
                }
            }

            var direction = Vector3.Cross(best, axis).Normalise();
            if (Vector3.Dot(direction, towards) < 0) direction = -direction;
            if (Vector3.Dot(direction, towards) < 0.2f) return false;

            angle = (float) (Math.Atan2(Vector3.Dot(direction, v), Vector3.Dot(direction, u)) * 180d / Math.PI);
            return true;
        }

        /// <summary>
        /// The planes of one piece of the swept solid. Piece <paramref name="piece"/> occupies the slot
        /// from <c>profileAngle + piece * delta</c> for <c>delta</c> degrees (delta may be negative).
        /// Returns false when a face is neither parallel nor square to the extrusion direction.
        /// </summary>
        /// <param name="normals">Outward face normals of the solid.</param>
        /// <param name="points">A point on each face.</param>
        public static bool TrySweep(IReadOnlyList<Vector3> normals, IReadOnlyList<Vector3> points, Vector3 pivot, Vector3 axis, Vector3 u, Vector3 v,
            float profileAngle, float delta, int piece, out List<SweepPlane> planes)
        {
            planes = new List<SweepPlane>();

            var tangent = Tangent(axis, u, v, profileAngle);
            var profileDirection = Direction(u, v, profileAngle);

            var mid = profileAngle + delta * (piece + 0.5f);
            var slotDirection = Direction(u, v, mid);

            for (var i = 0; i < normals.Count; i++)
            {
                var kind = Classify(normals[i], tangent);
                if (kind == FaceKind.Cap) continue;
                if (kind == FaceKind.Other) return false;

                // The face in profile coordinates: its normal and one point, measured in the
                // plane that holds the axis and the profile direction
                var normal = (normals[i] - tangent * Vector3.Dot(normals[i], tangent)).Normalise();
                var a = Vector3.Dot(normal, profileDirection);
                var b = Vector3.Dot(normal, axis);

                var relative = points[i] - pivot;
                var r = Vector3.Dot(relative, profileDirection);
                var z = Vector3.Dot(relative, axis);

                // The same face, laid in the plane of this slot's middle direction
                planes.Add(new SweepPlane
                {
                    Normal = slotDirection * a + axis * b,
                    Point = pivot + slotDirection * r + axis * z,
                    Source = i
                });
            }

            var half = Math.Abs(delta) / 2f;
            var lo = mid - half;
            var hi = mid + half;

            // Outward-facing seams through the pivot
            planes.Add(new SweepPlane { Normal = -Tangent(axis, u, v, lo), Point = pivot, Source = -1 });
            planes.Add(new SweepPlane { Normal = Tangent(axis, u, v, hi), Point = pivot, Source = -1 });

            return planes.Count >= 5;
        }

        /// <summary>
        /// Builds a piece from its planes. Returns null unless the result is a valid convex solid.
        /// </summary>
        public static Polyhedron BuildPiece(IReadOnlyList<SweepPlane> planes)
        {
            var piece = new Polyhedron(planes.Select(x => x.ToPlane()));
            if (piece.Polygons.Count < 4) return null;
            return piece.IsValid() ? piece : null;
        }

        /// <summary>The index of the plane a polygon lies on, or -1.</summary>
        public static int MatchPlane(Polygon polygon, IReadOnlyList<SweepPlane> planes)
        {
            return MatchPlane(polygon.Vertices, planes);
        }

        /// <summary>The index of the plane a set of vertices lies on, or -1.</summary>
        public static int MatchPlane(IReadOnlyList<Vector3> vertices, IReadOnlyList<SweepPlane> planes)
        {
            var best = -1;
            var bestError = 0.05f;
            for (var i = 0; i < planes.Count; i++)
            {
                var n = planes[i].Normal.Normalise();
                var error = vertices.Max(x => Math.Abs(Vector3.Dot(x - planes[i].Point, n)));
                if (error < bestError)
                {
                    bestError = error;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The outward normal of a face, from its first three vertices (the same plane the map export writes).</summary>
        public static Vector3 FaceNormal(IReadOnlyList<Vector3> vertices)
        {
            return new Plane(vertices[0], vertices[1], vertices[2]).Normal;
        }

        /// <summary>
        /// The cross-section of a solid: the vertices of one end cap, as (radial distance, height)
        /// in the plane that holds the axis and the profile direction.
        /// </summary>
        public static bool TryGetProfile(IReadOnlyList<Vector3> capVertices, Vector3 pivot, Vector3 axis, Vector3 u, Vector3 v, float profileAngle,
            out List<(double R, double Z)> profile)
        {
            profile = new List<(double R, double Z)>();
            if (capVertices.Count < 3) return false;

            var d = Direction(u, v, profileAngle);
            foreach (var vertex in capVertices)
            {
                var relative = vertex - pivot;
                profile.Add((Vector3.Dot(relative, d), Vector3.Dot(relative, axis)));
            }
            return true;
        }

        /// <summary>
        /// One piece built from explicit vertices: the profile at the seam where the slot starts and at the
        /// seam where it ends, joined by one quad per profile edge. Returns the faces, each wound outward,
        /// or null if the result isn't a convex solid.
        ///
        /// A seam's vertices are computed once, in double precision, from the seam's angle alone, so the two
        /// pieces that meet there get identical vertices. Their seam faces also start with the same three
        /// vertices in opposite order, which is what the map export writes as the plane: the compiler sees
        /// one plane facing both ways, and the two pieces join exactly.
        /// </summary>
        public static List<List<Vector3>> BuildLoft(IReadOnlyList<(double R, double Z)> profile, Vector3 pivot, Vector3 axis, Vector3 u, Vector3 v,
            float profileAngle, float delta, int piece)
        {
            var count = profile.Count;
            if (count < 3) return null;

            var cosHalf = Math.Cos(Math.Abs((double) delta) / 2d * Math.PI / 180d);

            Vector3[] Seam(int k)
            {
                var theta = ((double) profileAngle + (double) delta * k) * Math.PI / 180d;
                var c = Math.Cos(theta);
                var s = Math.Sin(theta);
                var seam = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    var rho = profile[i].R / cosHalf;
                    var z = profile[i].Z;
                    seam[i] = new Vector3(
                        (float) (pivot.X + (u.X * c + v.X * s) * rho + axis.X * z),
                        (float) (pivot.Y + (u.Y * c + v.Y * s) * rho + axis.Y * z),
                        (float) (pivot.Z + (u.Z * c + v.Z * s) * rho + axis.Z * z));
                }
                return seam;
            }

            var start = Seam(piece);
            var end = Seam(piece + 1);

            var all = start.Concat(end).ToList();
            var centre = all.Aggregate(Vector3.Zero, (a, b) => a + b) / all.Count;

            // Turn a vertex list so the face looks away from the middle of the piece
            List<Vector3> Outward(List<Vector3> face)
            {
                var middle = face.Aggregate(Vector3.Zero, (a, b) => a + b) / face.Count;
                if (Vector3.Dot(FaceNormal(face), middle - centre) >= 0) return face;
                var reversed = new List<Vector3>(face);
                reversed.Reverse();
                return reversed;
            }

            // A seam face: either the profile order, or the profile order reversed but starting at
            // vertex 2, so the first three vertices are the other side's first three, backwards
            List<Vector3> SeamFace(Vector3[] seam)
            {
                var forward = seam.ToList();
                var middle = forward.Aggregate(Vector3.Zero, (a, b) => a + b) / forward.Count;
                if (Vector3.Dot(FaceNormal(forward), middle - centre) >= 0) return forward;

                var backward = new List<Vector3> { seam[2], seam[1], seam[0] };
                for (var i = count - 1; i >= 3; i--) backward.Add(seam[i]);
                return backward;
            }

            var faces = new List<List<Vector3>> { SeamFace(start), SeamFace(end) };
            for (var i = 0; i < count; i++)
            {
                var next = (i + 1) % count;
                faces.Add(Outward(new List<Vector3> { start[i], start[next], end[next], end[i] }));
            }

            // Every vertex must lie behind every face
            foreach (var face in faces)
            {
                var normal = FaceNormal(face).Normalise();
                if (all.Any(x => Vector3.Dot(x - face[0], normal) > 0.01f)) return null;
            }

            return faces;
        }

        /// <summary>A rotation about the axis through the pivot.</summary>
        public static Matrix4x4 Rotation(Vector3 pivot, Vector3 axis, float degrees)
        {
            return Matrix4x4.CreateTranslation(-pivot)
                   * Matrix4x4.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f)
                   * Matrix4x4.CreateTranslation(pivot);
        }

        /// <summary>
        /// A reflection about the plane with the given normal through the pivot.
        /// Determinant is -1, which Texture.TransformUniform supports.
        /// </summary>
        public static Matrix4x4 Reflection(Vector3 normal, Vector3 pivot)
        {
            var m = Matrix4x4.Identity;
            m.M11 -= 2f * normal.X * normal.X;
            m.M12 -= 2f * normal.X * normal.Y;
            m.M13 -= 2f * normal.X * normal.Z;
            m.M21 -= 2f * normal.Y * normal.X;
            m.M22 -= 2f * normal.Y * normal.Y;
            m.M23 -= 2f * normal.Y * normal.Z;
            m.M31 -= 2f * normal.Z * normal.X;
            m.M32 -= 2f * normal.Z * normal.Y;
            m.M33 -= 2f * normal.Z * normal.Z;

            return Matrix4x4.CreateTranslation(-pivot) * m * Matrix4x4.CreateTranslation(pivot);
        }
    }
}
