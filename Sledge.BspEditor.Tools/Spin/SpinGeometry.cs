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
    /// A spin sweeps a profile around an axis. The profile is a solid (or a group of solids) that is
    /// extruded along one direction, the tangent: its side faces are parallel to that direction and its
    /// two end caps are square to it. The sweep puts one copy of the profile on every radial seam and
    /// joins neighbouring copies with one planar quad per profile edge, so the vertices of a seam lie
    /// exactly on the circles the profile's own points lie on. The first and last seam are exactly the
    /// profile, so the result joins straight pieces that continue the profile on either side.
    /// </summary>
    public static class SpinGeometry
    {
        /// <summary>Faces within this of parallel to the extrusion direction are side faces.</summary>
        private const float SideTolerance = 0.02f;

        /// <summary>Faces within this of square to the extrusion direction are end caps.</summary>
        private const float CapTolerance = 0.98f;

        public enum FaceKind
        {
            /// <summary>Parallel to the extrusion direction: part of the profile.</summary>
            Side,

            /// <summary>Square to the extrusion direction: replaced by a seam.</summary>
            Cap,

            /// <summary>Neither: the solid can't be swept without changing its shape.</summary>
            Other
        }

        /// <summary>A face reduced to what the geometry needs: its outward normal and its vertices.</summary>
        public sealed class PieceFace
        {
            public Vector3 Normal;
            public IReadOnlyList<Vector3> Vertices;

            public PieceFace(Vector3 normal, IReadOnlyList<Vector3> vertices)
            {
                Normal = normal;
                Vertices = vertices;
            }
        }

        /// <summary>An arch worked out from a start piece and an end piece.</summary>
        public sealed class ArchDefinition
        {
            public Vector3 Pivot;
            public Vector3 Axis;

            /// <summary>Spin plane frame: U points from the pivot at the start piece, (U, V, Axis) is right-handed.</summary>
            public Vector3 U;
            public Vector3 V;

            /// <summary>Total sweep in degrees, signed (positive turns counter-clockwise about Axis).</summary>
            public float Sweep;

            /// <summary>How wide the start piece is, in degrees, seen from the pivot.</summary>
            public float PieceAngle;

            /// <summary>Number of pieces that matches the start piece's thickness.</summary>
            public int Steps;

            /// <summary>Distance from the axis to the middle of the start piece.</summary>
            public float Radius;

            /// <summary>Thickness of the start piece along its extrusion direction.</summary>
            public float Thickness;
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

        #region Manual spin: pivot and axis given

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

            // A spin leaves many faces with the same few normals, so work on the distinct ones
            var unique = Distinct(normals);

            // Candidate extrusion directions come from the faces themselves
            var candidates = new Dictionary<(int, int, int), Vector3>();
            foreach (var entry in unique)
            {
                var flat = entry.Normal - axis * Vector3.Dot(entry.Normal, axis);
                if (flat.Length() < 0.2f) continue;
                flat = flat.Normalise();
                foreach (var c in new[] { flat, Vector3.Cross(axis, flat).Normalise() })
                {
                    candidates[Key(c)] = c;
                }
            }
            if (candidates.Count == 0) return false;

            var bestScore = -1;
            var bestAlign = -1f;
            var best = Vector3.Zero;
            foreach (var t in candidates.Values)
            {
                var score = Score(unique, t);

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

        private static (int, int, int) Key(Vector3 n)
        {
            return ((int) MathF.Round(n.X * 1000f), (int) MathF.Round(n.Y * 1000f), (int) MathF.Round(n.Z * 1000f));
        }

        private static List<(Vector3 Normal, int Count)> Distinct(IEnumerable<Vector3> normals)
        {
            var distinct = new Dictionary<(int, int, int), (Vector3 Normal, int Count)>();
            foreach (var n in normals)
            {
                var key = Key(n);
                distinct[key] = distinct.TryGetValue(key, out var found) ? (found.Normal, found.Count + 1) : (n, 1);
            }
            return distinct.Values.ToList();
        }

        /// <summary>How many faces are parallel or square to a direction.</summary>
        private static int Score(List<(Vector3 Normal, int Count)> unique, Vector3 t)
        {
            var score = 0;
            foreach (var entry in unique)
            {
                var dot = Math.Abs(Vector3.Dot(entry.Normal, t));
                if (dot <= SideTolerance || dot >= CapTolerance) score += entry.Count;
            }
            return score;
        }

        #endregion

        #region Start and end piece

        /// <summary>
        /// The extrusion direction of a set of faces: the direction every face is parallel or square to.
        /// When several fit (a plain box) the one along which the piece is thinnest wins.
        /// </summary>
        public static bool TryFindExtrusion(IReadOnlyList<PieceFace> faces, out Vector3 tangent)
        {
            tangent = Vector3.Zero;
            if (faces.Count == 0) return false;

            var unique = Distinct(faces.Select(x => x.Normal));
            var vertices = faces.SelectMany(x => x.Vertices).ToList();

            var bestScore = -1;
            var bestExtent = float.MaxValue;
            foreach (var entry in unique)
            {
                var t = entry.Normal.Normalise();
                var score = Score(unique, t);
                var projections = vertices.Select(x => Vector3.Dot(x, t)).ToList();
                var extent = projections.Max() - projections.Min();

                if (score > bestScore || (score == bestScore && extent < bestExtent - 1e-3f))
                {
                    bestScore = score;
                    bestExtent = extent;
                    tangent = t;
                }
            }

            // Solids that aren't extruded (sloped, triangulated pieces) don't fit; they are swept as a hull instead.
            // The direction only has to fit the solids that are extruded.
            if (bestScore < 2) return false;

            // A stable sign, so the same piece always gives the same direction
            var largest = Math.Abs(tangent.X) >= Math.Abs(tangent.Y) && Math.Abs(tangent.X) >= Math.Abs(tangent.Z) ? tangent.X
                : Math.Abs(tangent.Y) >= Math.Abs(tangent.Z) ? tangent.Y : tangent.Z;
            if (largest < 0) tangent = -tangent;
            return true;
        }

        /// <summary>The positions along the extrusion direction of the piece's two end caps.</summary>
        private static bool TryGetCaps(IReadOnlyList<PieceFace> faces, Vector3 tangent, out float low, out float high)
        {
            var offsets = faces
                .Where(x => Classify(x.Normal, tangent) == FaceKind.Cap && x.Vertices.Count > 0)
                .Select(x => Vector3.Dot(x.Vertices[0], tangent))
                .ToList();

            low = offsets.Count > 0 ? offsets.Min() : 0f;
            high = offsets.Count > 0 ? offsets.Max() : 0f;
            return offsets.Count > 0 && high - low > 0.01f;
        }

        private static Vector3 Middle(IReadOnlyList<PieceFace> faces)
        {
            var vertices = faces.SelectMany(x => x.Vertices).ToList();
            var min = vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            var max = vertices.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            return (min + max) / 2f;
        }

        /// <summary>
        /// Works out an arch from a start piece and an end piece. Each piece is an extruded profile; the
        /// arch's axis is square to both extrusion directions, its pivot is where the two pieces' outer
        /// end caps (the caps facing away from the other piece) meet, and it runs from the start piece's
        /// outer cap to the end piece's outer cap, covering both pieces.
        /// </summary>
        public static bool TryDefineArch(IReadOnlyList<PieceFace> start, IReadOnlyList<PieceFace> end, int maxSteps, out ArchDefinition arch, out string error)
        {
            arch = null;
            error = null;

            if (!TryFindExtrusion(start, out var tS)) { error = "The start piece isn't an extruded profile: every face must be parallel or square to one direction."; return false; }
            if (!TryFindExtrusion(end, out var tE)) { error = "The end piece isn't an extruded profile: every face must be parallel or square to one direction."; return false; }
            if (!TryGetCaps(start, tS, out var lowS, out var highS)) { error = "The start piece has no two end caps."; return false; }
            if (!TryGetCaps(end, tE, out var lowE, out var highE)) { error = "The end piece has no two end caps."; return false; }

            var axis = Vector3.Cross(tS, tE);
            if (axis.Length() < 0.1f) { error = "The start and end pieces are extruded along the same direction, so there is nothing to turn between them."; return false; }
            axis = axis.Normalise();

            var cS = Middle(start);
            var cE = Middle(end);

            // The outer cap of each piece is the one farther from the other piece
            var capS = Math.Abs(Vector3.Dot(cE, tS) - lowS) >= Math.Abs(Vector3.Dot(cE, tS) - highS) ? lowS : highS;
            var capE = Math.Abs(Vector3.Dot(cS, tE) - lowE) >= Math.Abs(Vector3.Dot(cS, tE) - highE) ? lowE : highE;

            // The pivot: on both outer cap planes, at the start piece's mid height
            var z = Vector3.Dot(cS, axis);
            var m = new Matrix4x4(tS.X, tS.Y, tS.Z, 0, tE.X, tE.Y, tE.Z, 0, axis.X, axis.Y, axis.Z, 0, 0, 0, 0, 1);
            if (!Matrix4x4.Invert(m, out var inverse)) { error = "The pivot can't be worked out from these pieces."; return false; }
            var rhs = new Vector3(capS, capE, z);
            var pivot = new Vector3(
                inverse.M11 * rhs.X + inverse.M12 * rhs.Y + inverse.M13 * rhs.Z,
                inverse.M21 * rhs.X + inverse.M22 * rhs.Y + inverse.M23 * rhs.Z,
                inverse.M31 * rhs.X + inverse.M32 * rhs.Y + inverse.M33 * rhs.Z);

            // The profile direction of each piece: from the pivot, along its outer cap, towards the piece
            var dS = Vector3.Cross(tS, axis).Normalise();
            if (Vector3.Dot(dS, cS - pivot) < 0) dS = -dS;
            var dE = Vector3.Cross(tE, axis).Normalise();
            if (Vector3.Dot(dE, cE - pivot) < 0) dE = -dE;

            var radius = Vector3.Dot(cS - pivot, dS);
            if (radius < 1f || Vector3.Dot(cE - pivot, dE) < 1f) { error = "A piece lies on the axis, so it can't be turned around it."; return false; }

            var u = dS;
            var v = Vector3.Cross(axis, u).Normalise();

            var thetaE = (float) (Math.Atan2(Vector3.Dot(dE, v), Vector3.Dot(dE, u)) * 180d / Math.PI);
            if (Math.Abs(thetaE) < 1f) { error = "The start and end pieces are at the same angle around the pivot."; return false; }

            // The arch leaves the start piece's outer cap through the piece's own body
            var bodyS = (Vector3.Dot(cS, tS) - capS) >= 0 ? tS : -tS;
            var sign = Vector3.Dot(bodyS, v) >= 0 ? 1f : -1f;
            var sweep = sign > 0 ? (thetaE > 0 ? thetaE : thetaE + 360f) : (thetaE < 0 ? thetaE : thetaE - 360f);

            // ... and reaches the end piece's outer cap through that piece's body
            var tauE = Vector3.Cross(axis, dE);
            var bodyE = (Vector3.Dot(cE, tE) - capE) >= 0 ? tE : -tE;
            if (Vector3.Dot(bodyE, tauE * sign) > 0)
            {
                error = "The end piece faces away from the arch. Its body has to lie on the arch side of its outer end cap.";
                return false;
            }

            var thickness = highS - lowS;
            var pieceAngle = (float) (Math.Atan2(thickness, radius) * 180d / Math.PI);
            var steps = Math.Max(1, Math.Min(maxSteps, (int) Math.Round(Math.Abs(sweep) / pieceAngle)));

            arch = new ArchDefinition
            {
                Pivot = pivot, Axis = axis, U = u, V = v,
                Sweep = sweep, PieceAngle = pieceAngle, Steps = steps,
                Radius = radius, Thickness = thickness
            };
            return true;
        }

        #endregion

        #region Building the pieces

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
        /// For each edge of an end cap (vertex i to vertex i + 1), the index of the side face that
        /// contains it, or -1. The swept piece's side face for that edge takes that face's texture.
        /// </summary>
        public static int[] FindEdgeFaces(IReadOnlyList<Vector3> capVertices, IReadOnlyList<PieceFace> faces, Vector3 tangent)
        {
            var result = new int[capVertices.Count];
            for (var i = 0; i < capVertices.Count; i++)
            {
                var a = capVertices[i];
                var b = capVertices[(i + 1) % capVertices.Count];
                result[i] = -1;

                var bestError = 0.05f;
                for (var f = 0; f < faces.Count; f++)
                {
                    if (faces[f].Vertices.Count == 0 || Classify(faces[f].Normal, tangent) != FaceKind.Side) continue;
                    var n = faces[f].Normal.Normalise();
                    var p = faces[f].Vertices[0];
                    var error = Math.Max(Math.Abs(Vector3.Dot(a - p, n)), Math.Abs(Vector3.Dot(b - p, n)));
                    if (error < bestError)
                    {
                        bestError = error;
                        result[i] = f;
                    }
                }
            }
            return result;
        }

        /// <summary>The outward normal of a face, from its first three vertices (the same plane the map export writes).</summary>
        public static Vector3 FaceNormal(IReadOnlyList<Vector3> vertices)
        {
            return new Plane(vertices[0], vertices[1], vertices[2]).Normal;
        }

        /// <summary>
        /// One piece built from explicit vertices: the profile at the seam where the slot starts and at the
        /// seam where it ends, joined by one planar quad per profile edge. Returns the faces, each wound
        /// outward (the first two are the seam faces, then one side face per profile edge), or null if the
        /// result isn't a convex solid.
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

            Vector3[] Seam(int k)
            {
                var theta = ((double) profileAngle + (double) delta * k) * Math.PI / 180d;
                var c = Math.Cos(theta);
                var s = Math.Sin(theta);
                var seam = new Vector3[count];
                for (var i = 0; i < count; i++)
                {
                    var r = profile[i].R;
                    var z = profile[i].Z;
                    seam[i] = new Vector3(
                        (float) (pivot.X + (u.X * c + v.X * s) * r + axis.X * z),
                        (float) (pivot.Y + (u.Y * c + v.Y * s) * r + axis.Y * z),
                        (float) (pivot.Z + (u.Z * c + v.Z * s) * r + axis.Z * z));
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

        #endregion

        #region Swept hull (solids that are not extruded profiles)

        /// <summary>
        /// The faces of the convex hull of a set of points, each wound outward (the first three vertices give the
        /// outward normal). Null if the points don't make a solid. Meant for a few dozen points.
        /// </summary>
        public static List<List<Vector3>> ConvexHullFaces(IReadOnlyList<Vector3> input)
        {
            const float onPlane = 0.01f;

            var pts = new List<Vector3>();
            foreach (var p in input)
            {
                if (!pts.Any(q => (q - p).LengthSquared() < 1e-4f)) pts.Add(p);
            }
            if (pts.Count < 4) return null;

            var centre = pts.Aggregate(Vector3.Zero, (a, b) => a + b) / pts.Count;

            var planes = new List<(Vector3 N, float D)>();
            for (var i = 0; i < pts.Count - 2; i++)
            {
                for (var j = i + 1; j < pts.Count - 1; j++)
                {
                    for (var k = j + 1; k < pts.Count; k++)
                    {
                        var n = Vector3.Cross(pts[j] - pts[i], pts[k] - pts[i]);
                        var length = n.Length();
                        if (length < 1e-3f) continue;
                        n /= length;
                        var d = Vector3.Dot(n, pts[i]);
                        if (Vector3.Dot(n, centre) - d > 0) { n = -n; d = -d; }

                        var ok = true;
                        for (var m = 0; m < pts.Count && ok; m++) ok = Vector3.Dot(n, pts[m]) - d <= onPlane;
                        if (!ok) continue;
                        if (planes.Any(x => Vector3.Dot(x.N, n) > 0.9999f && Math.Abs(x.D - d) < 0.02f)) continue;
                        planes.Add((n, d));
                    }
                }
            }
            if (planes.Count < 4) return null;

            var faces = new List<List<Vector3>>();
            foreach (var (n, d) in planes)
            {
                var onFace = pts.Where(p => Math.Abs(Vector3.Dot(n, p) - d) <= onPlane).ToList();
                if (onFace.Count < 3) return null;

                var middle = onFace.Aggregate(Vector3.Zero, (a, b) => a + b) / onFace.Count;
                var axisU = (onFace[0] - middle).Normalise();
                var axisW = Vector3.Cross(n, axisU);
                var ordered = onFace.OrderBy(p => Math.Atan2(Vector3.Dot(p - middle, axisW), Vector3.Dot(p - middle, axisU))).ToList();

                // Points in the middle of an edge would make the first three vertices a degenerate plane
                for (var m = ordered.Count - 1; m >= 0 && ordered.Count > 3; m--)
                {
                    var before = ordered[(m + ordered.Count - 1) % ordered.Count];
                    var after = ordered[(m + 1) % ordered.Count];
                    if (Vector3.Cross(ordered[m] - before, after - before).Length() < 1e-2f) ordered.RemoveAt(m);
                }
                if (ordered.Count < 3) return null;

                if (Vector3.Dot(FaceNormal(ordered), n) < 0) ordered.Reverse();
                faces.Add(ordered);
            }
            return faces;
        }

        /// <summary>The angle of a point around the pivot, in degrees relative to the profile line, or null if it is on the axis.</summary>
        public static float? AngleOffset(Vector3 point, Vector3 pivot, Vector3 axis, Vector3 u, Vector3 v, float profileAngle)
        {
            var relative = point - pivot;
            var planar = relative - axis * Vector3.Dot(relative, axis);
            if (planar.Length() < 1e-3f) return null;
            var angle = (float) (Math.Atan2(Vector3.Dot(relative, v), Vector3.Dot(relative, u)) * 180d / Math.PI) - profileAngle;
            return ((angle % 360f) + 540f) % 360f - 180f;
        }

        #endregion
    }
}
