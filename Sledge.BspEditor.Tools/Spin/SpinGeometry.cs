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

            // Candidate extrusion directions come from the faces themselves
            var candidates = new List<Vector3>();
            foreach (var n in normals)
            {
                var flat = n - axis * Vector3.Dot(n, axis);
                if (flat.Length() < 0.2f) continue;
                flat = flat.Normalise();
                candidates.Add(flat);
                candidates.Add(Vector3.Cross(axis, flat).Normalise());
            }
            if (candidates.Count == 0) return false;

            var bestScore = -1;
            var bestAlign = -1f;
            var best = Vector3.Zero;
            foreach (var t in candidates)
            {
                var score = normals.Count(n =>
                {
                    var dot = Math.Abs(Vector3.Dot(n, t));
                    return dot <= SideTolerance || dot >= CapTolerance;
                });

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
            var best = -1;
            var bestError = 0.05f;
            for (var i = 0; i < planes.Count; i++)
            {
                var n = planes[i].Normal.Normalise();
                var error = polygon.Vertices.Max(x => Math.Abs(Vector3.Dot(x - planes[i].Point, n)));
                if (error < bestError)
                {
                    bestError = error;
                    best = i;
                }
            }
            return best;
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
