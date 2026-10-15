using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Commands.Clipboard
{
    /// <summary>
    /// Helpers for pasting objects onto the surface of a brush, like putting a teacup on a table:
    /// find the face under a ray, snap onto it, rest the pasted objects against it, and make sure
    /// they don't end up inside another brush.
    /// </summary>
    internal static class PasteSurface
    {
        public class SurfaceHit
        {
            public Solid Solid { get; set; }
            public Vector3 Point { get; set; }

            /// <summary>
            /// The face normal, always pointing back towards where the ray came from
            /// </summary>
            public Vector3 Normal { get; set; }
        }

        /// <summary>
        /// Finds the closest brush face hit by a ray.
        /// </summary>
        public static SurfaceHit Raycast(IEnumerable<Solid> solids, Vector3 start, Vector3 direction, float length)
        {
            var line = new Line(start, start + direction * length);
            SurfaceHit best = null;
            var bestDistance = float.MaxValue;

            foreach (var solid in solids)
            {
                if (solid.BoundingBox == null || !solid.BoundingBox.IntersectsWith(line)) continue;

                foreach (var face in solid.Faces)
                {
                    if (face.Vertices.Count < 3) continue;

                    // Same as picking: you can't see these, so don't rest things on them
                    var texture = face.Texture?.Name?.ToLowerInvariant() ?? "";
                    if (texture.StartsWith("clip") || texture.StartsWith("null")) continue;

                    var point = face.GetIntersectionPoint(line);
                    if (point == null) continue;

                    var distance = (point.Value - start).Length();
                    if (distance >= bestDistance) continue;

                    var normal = face.Plane.Normal;
                    if (Vector3.Dot(normal, direction) > 0) normal = -normal;

                    bestDistance = distance;
                    best = new SurfaceHit { Solid = solid, Point = point.Value, Normal = normal };
                }
            }

            return best;
        }

        private static int DominantAxis(Vector3 v)
        {
            var x = Math.Abs(v.X);
            var y = Math.Abs(v.Y);
            var z = Math.Abs(v.Z);
            return x >= y && x >= z ? 0 : (y >= z ? 1 : 2);
        }

        private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

        private static Vector3 Set(Vector3 v, int axis, float value)
        {
            if (axis == 0) v.X = value;
            else if (axis == 1) v.Y = value;
            else v.Z = value;
            return v;
        }

        /// <summary>
        /// Snaps a point on a plane to the grid without leaving the plane: the two axes the plane
        /// runs along are snapped, and the third is worked out from the plane.
        /// </summary>
        public static Vector3 SnapOnPlane(Vector3 point, Vector3 normal, Func<Vector3, Vector3> snap)
        {
            var axis = DominantAxis(normal);
            var na = Get(normal, axis);
            if (Math.Abs(na) < 0.0001f) return point;

            var snapped = snap(point);
            var d = Vector3.Dot(normal, point);
            var rest = Vector3.Dot(normal, snapped) - na * Get(snapped, axis);
            return Set(snapped, axis, (d - rest) / na);
        }

        /// <summary>
        /// How far a box reaches from its centre in the direction opposite the normal,
        /// i.e. how high its centre has to be above a surface for the box to sit on it.
        /// </summary>
        public static float RestingDistance(Box box, Vector3 normal)
        {
            var half = (box.End - box.Start) / 2f;
            return Math.Abs(normal.X) * half.X + Math.Abs(normal.Y) * half.Y + Math.Abs(normal.Z) * half.Z;
        }

        /// <summary>
        /// Moves the box (already shifted by offset) along the up direction until it is no longer
        /// inside any other brush. The brush it is deliberately resting on is ignored.
        /// </summary>
        public static Vector3 LiftClear(Box box, Vector3 offset, Vector3 up, IEnumerable<Solid> solids, Solid ignore)
        {
            const float tolerance = 0.05f;
            var axis = DominantAxis(up);
            var sign = Get(up, axis) >= 0 ? 1f : -1f;
            var list = solids.Where(x => x != ignore && x.BoundingBox != null).ToList();

            // Touching a surface doesn't count as being inside it
            var inset = new Vector3(tolerance);

            for (var i = 0; i < 64; i++)
            {
                var min = box.Start + offset + inset;
                var max = box.End + offset - inset;
                var need = 0f;

                foreach (var s in list)
                {
                    var b = s.BoundingBox;
                    var overlaps = b.Start.X < max.X && b.End.X > min.X
                                && b.Start.Y < max.Y && b.End.Y > min.Y
                                && b.Start.Z < max.Z && b.End.Z > min.Z;
                    if (!overlaps) continue;

                    var shift = sign > 0
                        ? Get(b.End, axis) - Get(box.Start + offset, axis)
                        : Get(box.End + offset, axis) - Get(b.Start, axis);
                    need = Math.Max(need, shift);
                }

                if (need <= 0) break;
                offset = Set(offset, axis, Get(offset, axis) + sign * need);
            }

            return offset;
        }
    }
}
