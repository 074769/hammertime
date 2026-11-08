using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.DataStructures.Geometric;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Tools.Bevel
{
    /// <summary>
    /// The maths of the Bevel tool, with no editor types in it.
    ///
    /// A bevel (chamfer) cuts a selected edge back by a distance along both faces that meet at it: the cut is
    /// a plane through the point that distance inside the first face and the point that distance inside the second.
    /// The solid is rebuilt from its own planes plus the cut planes, so the result is always one convex brush.
    ///
    /// Nothing is ever returned unless the rebuilt solid passes the same check the Problem Checker uses
    /// (Polyhedron.IsValid) and no face has been swallowed, collapsed to a sliver or turned into NaN.
    /// </summary>
    internal static class BevelGeometry
    {
        /// <summary>Two vertices closer than this are the same vertex.</summary>
        public const float PositionTolerance = 0.02f;

        /// <summary>A cut may not come closer than this to the far side of a face (units).</summary>
        public const float SliverMargin = 1f;

        /// <summary>A face with an edge shorter than this is a sliver.</summary>
        public const float MinEdgeLength = 0.25f;

        /// <summary>A face smaller than this has collapsed.</summary>
        public const float MinFaceArea = 1f;

        /// <summary>A vertex may sit this far outside a plane before the solid counts as broken.</summary>
        public const float OutsideTolerance = 0.05f;

        /// <summary>Most faces a brush may end up with (the compilers' limit per brush).</summary>
        public const int MaxFaces = 128;

        /// <summary>Most segments one bevel can have.</summary>
        public const int MaxSegments = 32;

        /// <summary>A clamped distance is rounded down to a multiple of this.</summary>
        public const float Step = 0.125f;

        public sealed class FaceInput
        {
            public Plane Plane { get; }
            public IReadOnlyList<Vector3> Vertices { get; }

            public FaceInput(Plane plane, IReadOnlyList<Vector3> vertices)
            {
                Plane = plane;
                Vertices = vertices;
            }
        }

        /// <summary>An edge of a solid: where it runs and the two faces that meet at it.</summary>
        public sealed class SolidEdge
        {
            public Vector3 Start { get; }
            public Vector3 End { get; }
            public int FaceA { get; }
            public int FaceB { get; }

            public SolidEdge(Vector3 start, Vector3 end, int faceA, int faceB)
            {
                Start = start;
                End = end;
                FaceA = faceA;
                FaceB = faceB;
            }
        }

        public sealed class ResultFace
        {
            public Polygon Polygon { get; }

            /// <summary>Index of the original face this one came from, or -1 for a new bevel face.</summary>
            public int SourceFace { get; }

            /// <summary>For a new bevel face: the original face whose texture it borrows.</summary>
            public int ParentFace { get; }

            public bool IsNew => SourceFace < 0;

            public ResultFace(Polygon polygon, int sourceFace, int parentFace)
            {
                Polygon = polygon;
                SourceFace = sourceFace;
                ParentFace = parentFace;
            }
        }

        public sealed class Result
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public float Requested { get; set; }
            public float Applied { get; set; }
            public bool Clamped { get; set; }
            public List<ResultFace> Faces { get; set; } = new List<ResultFace>();
        }

        private sealed class Cut
        {
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Along;
            public Vector3 InA;
            public Vector3 InB;
            public int FaceA;
            public int FaceB;

            /// <summary>The corners of the profile for a distance of 1, as amounts of InA and InB (X = along the first face, Y = along the second).</summary>
            public List<Vector2> Profile = new List<Vector2>();

            public float MinChord;

            /// <summary>The furthest the cut can go along each face: to the far side of it, or to the middle where another bevel meets it.</summary>
            public float LimitA;
            public float LimitB;

            /// <summary>
            /// The planes of the bevel when it goes distanceA along the first face and distanceB along the second.
            /// The profile is stretched to fit, so it stays flat-faced and convex whatever the two distances are. Null if it degenerates.
            /// </summary>
            public List<Plane> PlanesAt(float distanceA, float distanceB)
            {
                var corners = Profile.Select(c => InA * (c.X * distanceA) + InB * (c.Y * distanceB)).ToList();
                var planes = new List<Plane>();
                for (var k = 0; k + 1 < corners.Count; k++)
                {
                    var step = corners[k + 1] - corners[k];
                    var normal = Vector3.Cross(Along, step);
                    if (normal.LengthSquared() < 1e-10f) return null;
                    normal = Vector3.Normalize(normal);

                    // Faces away from the solid: the corner being cut off is on its outer side
                    var chordMid = (corners[k] + corners[k + 1]) / 2;
                    var away = Vector3.Dot(normal, -chordMid);
                    if (Math.Abs(away) < 1e-4f) return null;
                    if (away < 0) normal = -normal;

                    planes.Add(new Plane(normal, Start + chordMid));
                }
                return planes;
            }

            /// <summary>
            /// How far along each face to cut for a requested distance. Cut per side, a side that has run out of face stops
            /// where it is (at the far side, or where it meets another bevel) while the other side carries on.
            /// </summary>
            public (float A, float B) DistancesFor(float distance, bool perSide)
            {
                if (!perSide) return (distance, distance);
                return (Limited(distance, LimitA), Limited(distance, LimitB));
            }

            private static float Limited(float distance, float limit)
            {
                // Stopping just short of the limit would leave a sliver, so close to it counts as at it
                return distance >= limit - MinEdgeLength ? limit : distance;
            }
        }

        #region Edges

        public static bool Same(Vector3 a, Vector3 b)
        {
            return (a - b).LengthSquared() < PositionTolerance * PositionTolerance;
        }

        private static bool HasVertex(FaceInput face, Vector3 v)
        {
            for (var i = 0; i < face.Vertices.Count; i++)
            {
                if (Same(face.Vertices[i], v)) return true;
            }
            return false;
        }

        /// <summary>Every edge of the solid, once each, with the two faces that share it.</summary>
        public static List<SolidEdge> FindEdges(IReadOnlyList<FaceInput> faces)
        {
            var edges = new List<SolidEdge>();
            for (var i = 0; i < faces.Count; i++)
            {
                var verts = faces[i].Vertices;
                for (var k = 0; k < verts.Count; k++)
                {
                    var a = verts[k];
                    var b = verts[(k + 1) % verts.Count];
                    if (Same(a, b)) continue;

                    for (var j = i + 1; j < faces.Count; j++)
                    {
                        if (!HasVertex(faces[j], a) || !HasVertex(faces[j], b)) continue;
                        edges.Add(new SolidEdge(a, b, i, j));
                        break;
                    }
                }
            }
            return edges;
        }

        /// <summary>Finds the edge running between two points (either direction). False if it isn't an edge of the solid any more.</summary>
        public static bool TryFindEdge(IReadOnlyList<FaceInput> faces, Vector3 start, Vector3 end, out SolidEdge edge)
        {
            edge = null;
            var shared = new List<int>();
            for (var i = 0; i < faces.Count; i++)
            {
                if (HasVertex(faces[i], start) && HasVertex(faces[i], end)) shared.Add(i);
            }
            if (shared.Count != 2) return false;
            edge = new SolidEdge(start, end, shared[0], shared[1]);
            return true;
        }

        #endregion

        #region Bevel

        private static Vector3 Centre(IEnumerable<Vector3> points)
        {
            var sum = Vector3.Zero;
            var count = 0;
            foreach (var p in points)
            {
                sum += p;
                count++;
            }
            return count == 0 ? Vector3.Zero : sum / count;
        }

        /// <summary>The direction, in the face and square to the edge, that points into the face.</summary>
        private static Vector3? InwardInFace(FaceInput face, Vector3 along, Vector3 mid)
        {
            var dir = Vector3.Cross(face.Plane.Normal, along);
            if (dir.LengthSquared() < 1e-8f) return null;
            dir = Vector3.Normalize(dir);
            if (Vector3.Dot(dir, Centre(face.Vertices) - mid) < 0) dir = -dir;
            return dir;
        }

        /// <summary>
        /// The corners of a bevel profile for a distance of 1, relative to the edge. One segment is the flat chamfer from
        /// the point on the first face to the point on the second. More segments follow the arc that touches both faces
        /// at those two points (Blender's default profile), so every segment is flat and the solid stays convex.
        /// Null if the edge is too flat or too sharp for an arc.
        /// </summary>
        private static List<Vector3> ProfilePoints(Vector3 inA, Vector3 inB, int segments)
        {
            if (segments <= 1) return new List<Vector3> { inA, inB };

            var theta = (float) Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(inA, inB))));
            var half = theta / 2;
            var cosHalf = (float) Math.Cos(half);
            if (cosHalf < 1e-3f) return null;

            var centre = Vector3.Normalize(inA + inB) / cosHalf;
            var radius = (float) Math.Tan(half);
            var u = Vector3.Normalize(inA - centre);
            var w = Vector3.Normalize(inB - centre);
            var phi = (float) Math.Acos(Math.Max(-1f, Math.Min(1f, Vector3.Dot(u, w))));
            var sinPhi = (float) Math.Sin(phi);
            if (sinPhi < 0.02f) return null;

            var points = new List<Vector3>();
            for (var k = 0; k <= segments; k++)
            {
                var t = k / (float) segments;
                var dir = u * ((float) Math.Sin((1 - t) * phi) / sinPhi) + w * ((float) Math.Sin(t * phi) / sinPhi);
                points.Add(centre + dir * radius);
            }
            return points;
        }

        public static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                     || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        /// <summary>A polygon's normal from all of its vertices (Newell's method), facing the same way as the hint.</summary>
        private static Vector3 FaceNormal(IReadOnlyList<Vector3> verts, Vector3 hint)
        {
            var n = Vector3.Zero;
            for (var i = 0; i < verts.Count; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % verts.Count];
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            if (n.LengthSquared() < 1e-12f) return hint;
            n = Vector3.Normalize(n);
            return Vector3.Dot(n, hint) < 0 ? -n : n;
        }

        private static float Area(IReadOnlyList<Vector3> verts)
        {
            var sum = Vector3.Zero;
            for (var i = 0; i < verts.Count; i++)
            {
                sum += Vector3.Cross(verts[i], verts[(i + 1) % verts.Count]);
            }
            return sum.Length() * 0.5f;
        }

        private static string Fail(Result result, string message)
        {
            result.Success = false;
            result.Message = message;
            result.Faces = new List<ResultFace>();
            return message;
        }

        /// <summary>
        /// Bevels the given edges of a solid.
        /// </summary>
        /// <param name="faces">The faces of the solid (planes facing outwards, plus their vertices).</param>
        /// <param name="edges">The edges to bevel, as the pairs of end points the user picked.</param>
        /// <param name="requested">How far to cut back along each face.</param>
        /// <param name="clamp">True: both sides go the same distance, shrunk until it is valid, so a bevel stops where it meets. False: each side stops where it runs out of face while the other side keeps going, and the distance is never shrunk.</param>
        /// <param name="segments">How many flat faces each bevel is made of (1 = a flat chamfer, more = a rounded profile).</param>
        public static Result Bevel(IReadOnlyList<FaceInput> faces, IReadOnlyList<(Vector3 Start, Vector3 End)> edges, float requested, bool clamp, int segments = 1)
        {
            var result = new Result { Requested = requested };

            if (faces == null || faces.Count < 4) { Fail(result, "The solid has too few faces."); return result; }
            if (edges == null || edges.Count == 0) { Fail(result, "No edges selected."); return result; }
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested < Step) { Fail(result, "The distance is too small."); return result; }
            if (faces.Any(f => f.Vertices.Any(v => !IsFinite(v)))) { Fail(result, "The solid has invalid vertices."); return result; }
            if (segments < 1 || segments > MaxSegments) { Fail(result, "Segments must be between 1 and " + MaxSegments + "."); return result; }

            var allVerts = new List<Vector3>();
            foreach (var f in faces)
            {
                foreach (var v in f.Vertices)
                {
                    if (!allVerts.Any(x => Same(x, v))) allVerts.Add(v);
                }
            }
            var centre = Centre(allVerts);

            // One cut per distinct edge
            var cuts = new List<Cut>();
            foreach (var (start, end) in edges)
            {
                if (cuts.Any(c => (Same(c.Start, start) && Same(c.End, end)) || (Same(c.Start, end) && Same(c.End, start)))) continue;

                if (!TryFindEdge(faces, start, end, out var edge)) { Fail(result, "A selected edge is no longer part of the solid."); return result; }

                var along = end - start;
                if (along.LengthSquared() < 1e-6f) { Fail(result, "A selected edge has no length."); return result; }
                along = Vector3.Normalize(along);
                var mid = (start + end) / 2;

                var inA = InwardInFace(faces[edge.FaceA], along, mid);
                var inB = InwardInFace(faces[edge.FaceB], along, mid);
                if (inA == null || inB == null) { Fail(result, "A selected edge is degenerate."); return result; }

                // The profile of the bevel, and a plane for each of its flat faces
                var sum = inA.Value + inB.Value;
                if (sum.LengthSquared() < 1e-4f) { Fail(result, "A selected edge lies between flat faces; there is nothing to bevel."); return result; }

                var profile = ProfilePoints(inA.Value, inB.Value, segments);
                if (profile == null) { Fail(result, "A selected edge is too flat or too sharp for " + segments + " segments."); return result; }

                var cut = new Cut { Start = start, End = end, Along = along, InA = inA.Value, InB = inB.Value, FaceA = edge.FaceA, FaceB = edge.FaceB, MinChord = float.MaxValue };

                // The profile as amounts of InA and InB, so it can be stretched differently along each face
                var gram = Vector3.Dot(inA.Value, inB.Value);
                var det = 1 - gram * gram;
                if (det < 1e-4f) { Fail(result, "A selected edge is degenerate."); return result; }
                foreach (var point in profile)
                {
                    var pa = Vector3.Dot(point, inA.Value);
                    var pb = Vector3.Dot(point, inB.Value);
                    cut.Profile.Add(new Vector2((pa - gram * pb) / det, (pb - gram * pa) / det));
                }
                for (var k = 0; k < segments; k++) cut.MinChord = Math.Min(cut.MinChord, (profile[k + 1] - profile[k]).Length());
                if (cut.PlanesAt(1, 1) == null) { Fail(result, "A selected edge is degenerate."); return result; }

                // Where the cut has to stop on each face: its far side, or the middle if another bevel comes at it from the other side
                cut.LimitA = Reach(faces[edge.FaceA], inA.Value, start);
                cut.LimitB = Reach(faces[edge.FaceB], inB.Value, start);
                cuts.Add(cut);
            }

            // Two bevels coming at each other across a face meet in the middle of it
            foreach (var cut in cuts)
            {
                foreach (var other in cuts)
                {
                    if (ReferenceEquals(cut, other)) continue;
                    cut.LimitA = Math.Min(cut.LimitA, MeetingPoint(cut, cut.FaceA, cut.InA, other));
                    cut.LimitB = Math.Min(cut.LimitB, MeetingPoint(cut, cut.FaceB, cut.InB, other));
                }
            }

            if (faces.Count + cuts.Count * segments > MaxFaces)
            {
                Fail(result, "That would give the brush " + (faces.Count + cuts.Count * segments) + " faces; the limit is " + MaxFaces + ". Use fewer segments or edges.");
                return result;
            }

            // The least that keeps every bevel face from becoming a sliver
            var minDistance = Step;
            foreach (var cut in cuts)
            {
                minDistance = Math.Max(minDistance, MinEdgeLength * 1.05f / cut.MinChord);
            }

            if (requested < minDistance)
            {
                Fail(result, segments > 1
                    ? "Too many segments for this distance: the faces would be tiny. Use fewer segments or a larger distance."
                    : "The distance is too small.");
                return result;
            }

            // Clamp on: both sides always go the same distance, which stops where the first side runs out of face.
            // Clamp off: each side goes as far as it can on its own, so the distance is never shrunk.
            var perSide = !clamp;
            var distance = requested;
            if (clamp)
            {
                // Within a sliver of the limit counts as at it
                var reach = cuts.Min(c => Math.Min(c.LimitA, c.LimitB));
                distance = requested >= reach - MinEdgeLength ? reach : requested;
            }
            if (distance < minDistance)
            {
                Fail(result, "There is not enough room to bevel here.");
                return result;
            }

            var built = Build(faces, cuts, distance, perSide, segments);
            if (built == null && clamp)
            {
                // Something other than a face's own limit is in the way: find the largest even distance that works
                // Zero is the untouched solid, so it counts as valid; tiny distances are never tried because the search only moves up from there
                float lo = 0, hi = distance;
                for (var i = 0; i < 24 && hi - lo > Step * 0.25f; i++)
                {
                    var mid = (lo + hi) / 2;
                    if (Build(faces, cuts, mid, perSide, segments) != null) lo = mid; else hi = mid;
                }

                var rounded = (float) Math.Floor(lo / Step) * Step;
                if (rounded >= minDistance)
                {
                    distance = rounded;
                    built = Build(faces, cuts, distance, perSide, segments);
                }
                if (built == null && lo >= minDistance)
                {
                    // Rounding can only fail if validity is not monotonic; fall back to the unrounded value
                    distance = lo;
                    built = Build(faces, cuts, distance, perSide, segments);
                }
            }

            if (built == null)
            {
                Fail(result, clamp
                    ? "There is not enough room to bevel here."
                    : "There is not enough room to bevel here at this distance. Turn Clamp on to bevel both sides by the same smaller distance.");
                return result;
            }

            result.Success = true;
            result.Applied = distance;
            var shrunk = clamp && distance < requested - 0.001f;
            var held = perSide && cuts.Any(c =>
            {
                var (a, b) = c.DistancesFor(distance, true);
                return a < distance - 0.001f || b < distance - 0.001f;
            });
            result.Clamped = shrunk || held;
            result.Faces = built;
            var notes = new List<string>();
            if (shrunk) notes.Add("Stopped at " + distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " units: that is as far as both sides can go.");
            if (held) notes.Add("One side stopped where the bevel meets the far side of the face or another bevel; the other side carried on.");
            result.Message = notes.Count > 0 ? string.Join(" ", notes) : null;
            return result;
        }

        /// <summary>How far a face goes in from an edge, in the direction the bevel cuts: the furthest any of its vertices is.</summary>
        private static float Reach(FaceInput face, Vector3 inward, Vector3 start)
        {
            var reach = 0f;
            foreach (var v in face.Vertices) reach = Math.Max(reach, Vector3.Dot(v - start, inward));
            return reach;
        }

        /// <summary>
        /// Where another bevel on the same face, coming from the opposite side, meets this one: half way between the two edges
        /// (both go the same distance). No limit when there is no such bevel.
        /// </summary>
        private static float MeetingPoint(Cut cut, int face, Vector3 inward, Cut other)
        {
            Vector3 otherInward;
            if (other.FaceA == face) otherInward = other.InA;
            else if (other.FaceB == face) otherInward = other.InB;
            else return float.MaxValue;

            if (Vector3.Dot(inward, otherInward) > -0.98f) return float.MaxValue;
            var gap = Vector3.Dot((other.Start + other.End) / 2 - (cut.Start + cut.End) / 2, inward);
            return gap > 0 ? gap / 2 : float.MaxValue;
        }

        /// <summary>
        /// True when a face has been squeezed out by the other planes: nothing of it is left, or what is left has no width
        /// (the bevels on either side have met in the middle of it). A face with any real width left is kept, so its plane still caps the solid.
        /// </summary>
        private static bool IsSqueezedOut(IReadOnlyList<Plane> planes, int index)
        {
            var verts = new Polygon(planes[index]).Vertices.ToList();
            for (var j = 0; j < planes.Count && verts.Count >= 3; j++)
            {
                if (j == index) continue;
                verts = ClipToInside(verts, planes[j]);
            }
            if (verts.Count < 3) return true;

            var perimeter = 0f;
            for (var i = 0; i < verts.Count; i++) perimeter += (verts[i] - verts[(i + 1) % verts.Count]).Length();
            var area = Area(verts);
            return area < 0.05f || (perimeter > 0 && 2 * area / perimeter < 0.02f);
        }

        /// <summary>The part of a convex polygon on the inside (behind) of a plane.</summary>
        private static List<Vector3> ClipToInside(List<Vector3> verts, Plane plane)
        {
            const float epsilon = 0.001f;
            var result = new List<Vector3>();
            for (var i = 0; i < verts.Count; i++)
            {
                var a = verts[i];
                var b = verts[(i + 1) % verts.Count];
                var da = Vector3.Dot(plane.Normal, a) - plane.DistanceFromOrigin;
                var db = Vector3.Dot(plane.Normal, b) - plane.DistanceFromOrigin;
                var aIn = da <= epsilon;
                var bIn = db <= epsilon;

                if (aIn) result.Add(a);
                if (aIn != bIn)
                {
                    var t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
            return result;
        }

        /// <summary>Builds the solid at the given distance. Null if the result is anything but a clean, valid, convex solid.</summary>
        private static List<ResultFace> Build(IReadOnlyList<FaceInput> faces, List<Cut> cuts, float distance, bool perSide, int segments)
        {
            var cutPlanes = new List<Plane>();
            foreach (var cut in cuts)
            {
                var (distanceA, distanceB) = cut.DistancesFor(distance, perSide);
                var cutFaces = cut.PlanesAt(distanceA, distanceB);
                if (cutFaces == null) return null;
                cutPlanes.AddRange(cutFaces);
            }
            if (cutPlanes.Any(p => !IsFinite(p.Normal) || float.IsNaN(p.DistanceFromOrigin) || float.IsInfinity(p.DistanceFromOrigin))) return null;

            // A face between two bevels can be squeezed out of existence: the bevels meet in the middle of it and merge into one edge.
            // That is fine as long as what is left is valid, so such a face is dropped before the solid is built.
            // Only faces that a bevelled edge runs along can go, and they are found by clipping, not by trusting the plane solver.
            var everyPlane = faces.Select(f => f.Plane).Concat(cutPlanes).ToList();
            var beveled = new HashSet<int>(cuts.SelectMany(c => new[] { c.FaceA, c.FaceB }));
            var planes = new List<Plane>();
            var planeFace = new List<int>(); // for each plane: the original face it came from, or -1 for a bevel plane
            for (var i = 0; i < faces.Count; i++)
            {
                if (beveled.Contains(i) && IsSqueezedOut(everyPlane, i)) continue;
                planes.Add(faces[i].Plane);
                planeFace.Add(i);
            }
            var keptFaces = planes.Count;
            foreach (var plane in cutPlanes)
            {
                planes.Add(plane);
                planeFace.Add(-1);
            }
            if (planes.Count < 4) return null;

            Polyhedron poly;
            try
            {
                poly = new Polyhedron(planes);
            }
            catch (Exception)
            {
                return null;
            }

            var polygons = poly.Polygons;

            // Every original face must survive and every cut must have produced exactly one face
            if (polygons.Count != planes.Count) return null;

            var used = new bool[polygons.Count];
            var result = new List<ResultFace>();
            for (var p = 0; p < planes.Count; p++)
            {
                // Each plane's face is the unused polygon whose vertices all lie on it and whose normal agrees best. The
                // polygon's own plane is not used for this: on a long thin face it is too noisy to tell neighbouring segments apart
                var match = -1;
                var bestDot = 0.99f;
                for (var q = 0; q < polygons.Count; q++)
                {
                    if (used[q] || polygons[q].Vertices.Count < 3) continue;
                    var qv = polygons[q].Vertices;
                    var onPlane = true;
                    for (var i = 0; i < qv.Count && onPlane; i++)
                    {
                        if (Math.Abs(Vector3.Dot(planes[p].Normal, qv[i]) - planes[p].DistanceFromOrigin) > OutsideTolerance) onPlane = false;
                    }
                    if (!onPlane) continue;

                    var dot = Vector3.Dot(FaceNormal(qv, polygons[q].Plane.Normal), planes[p].Normal);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        match = q;
                    }
                }
                if (match < 0) return null;
                used[match] = true;

                var polygon = polygons[match];
                var verts = polygon.Vertices;
                if (verts.Any(v => !IsFinite(v))) return null;
                if (Area(verts) < MinFaceArea) return null;
                for (var i = 0; i < verts.Count; i++)
                {
                    if ((verts[i] - verts[(i + 1) % verts.Count]).Length() < MinEdgeLength) return null;
                }

                if (planeFace[p] >= 0) result.Add(new ResultFace(polygon, planeFace[p], -1));
                else result.Add(new ResultFace(polygon, -1, cuts[(p - keptFaces) / segments].FaceA));
            }

            // The plane solver does not clip reliably when a cut swallows a face (it can hand back the unclipped face),
            // so check independently that no vertex of any face lies outside any plane of the solid
            foreach (var polygon in polygons)
            {
                foreach (var v in polygon.Vertices)
                {
                    foreach (var plane in planes)
                    {
                        if (Vector3.Dot(plane.Normal, v) - plane.DistanceFromOrigin > OutsideTolerance) return null;
                    }
                }
            }

            // The editor's own validity check (convex faces, no coplanar faces, no backwards faces, vertices on their planes)
            if (!poly.IsValid()) return null;

            return result;
        }

        #endregion
    }
}
