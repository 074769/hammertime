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
            public Vector3 InA;
            public Vector3 InB;
            public Vector3 Normal;
            public float K;
            public int FaceA;
            public int FaceB;

            public Plane PlaneAt(float distance) => new Plane(Normal, Start + InA * distance);
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

        public static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                     || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        private static bool SamePlane(Plane a, Plane b)
        {
            return Vector3.Dot(a.Normal, b.Normal) > 0.99995f && Math.Abs(a.DistanceFromOrigin - b.DistanceFromOrigin) < 0.05f;
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
        /// <param name="clamp">True: shrink the distance until the result is valid. False: all or nothing.</param>
        public static Result Bevel(IReadOnlyList<FaceInput> faces, IReadOnlyList<(Vector3 Start, Vector3 End)> edges, float requested, bool clamp)
        {
            var result = new Result { Requested = requested };

            if (faces == null || faces.Count < 4) { Fail(result, "The solid has too few faces."); return result; }
            if (edges == null || edges.Count == 0) { Fail(result, "No edges selected."); return result; }
            if (float.IsNaN(requested) || float.IsInfinity(requested) || requested < Step) { Fail(result, "The distance is too small."); return result; }
            if (faces.Any(f => f.Vertices.Any(v => !IsFinite(v)))) { Fail(result, "The solid has invalid vertices."); return result; }

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

                // The cut faces away from the bisector of the two faces' inward directions
                var sum = inA.Value + inB.Value;
                if (sum.LengthSquared() < 1e-4f) { Fail(result, "A selected edge lies between flat faces; there is nothing to bevel."); return result; }
                var normal = -Vector3.Normalize(sum);
                if (Vector3.Dot(normal, centre - start) > 0) normal = -normal;

                var k = -Vector3.Dot(normal, inA.Value);
                if (k < 1e-3f) { Fail(result, "A selected edge is degenerate."); return result; }

                cuts.Add(new Cut
                {
                    Start = start, End = end, InA = inA.Value, InB = inB.Value, Normal = normal, K = k,
                    FaceA = edge.FaceA, FaceB = edge.FaceB
                });
            }

            // The most each cut can go in before it reaches a vertex that isn't on its own edge
            var maxDistance = float.MaxValue;
            foreach (var cut in cuts)
            {
                foreach (var v in allVerts)
                {
                    if (Same(v, cut.Start) || Same(v, cut.End)) continue;
                    var limit = (Vector3.Dot(cut.Normal, cut.Start - v) - SliverMargin) / cut.K;
                    if (limit < maxDistance) maxDistance = limit;
                }
            }

            var distance = clamp ? Math.Min(requested, maxDistance) : requested;
            if (distance < Step)
            {
                Fail(result, "There is not enough room to bevel here.");
                return result;
            }

            var built = Build(faces, cuts, distance);
            if (built == null && clamp)
            {
                // Something other than a single cut's own limit is in the way (cuts meeting each other): find the largest distance that works
                float lo = 0, hi = distance;
                for (var i = 0; i < 24; i++)
                {
                    var mid = (lo + hi) / 2;
                    if (Build(faces, cuts, mid) != null) lo = mid; else hi = mid;
                }

                var rounded = (float) Math.Floor(lo / Step) * Step;
                if (rounded >= Step)
                {
                    distance = rounded;
                    built = Build(faces, cuts, distance);
                    if (built == null)
                    {
                        // Rounding can only fail if validity is not monotonic; fall back to the unrounded value
                        distance = lo;
                        built = lo >= Step ? Build(faces, cuts, distance) : null;
                    }
                }
            }

            if (built == null)
            {
                Fail(result, clamp ? "There is not enough room to bevel here." : "That distance would make invalid geometry. Turn Clamp on or use a smaller distance.");
                return result;
            }

            result.Success = true;
            result.Applied = distance;
            result.Clamped = clamp && distance < requested - 0.001f;
            result.Faces = built;
            result.Message = result.Clamped
                ? "Clamped to " + distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " units to keep the geometry valid."
                : null;
            return result;
        }

        /// <summary>Builds the solid at the given distance. Null if the result is anything but a clean, valid, convex solid.</summary>
        private static List<ResultFace> Build(IReadOnlyList<FaceInput> faces, List<Cut> cuts, float distance)
        {
            var cutPlanes = cuts.Select(c => c.PlaneAt(distance)).ToList();
            if (cutPlanes.Any(p => !IsFinite(p.Normal) || float.IsNaN(p.DistanceFromOrigin) || float.IsInfinity(p.DistanceFromOrigin))) return null;

            var planes = faces.Select(f => f.Plane).Concat(cutPlanes).ToList();

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
                var match = -1;
                for (var q = 0; q < polygons.Count; q++)
                {
                    if (used[q] || polygons[q].Vertices.Count < 3) continue;
                    if (!SamePlane(planes[p], polygons[q].Plane)) continue;
                    match = q;
                    break;
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

                if (p < faces.Count) result.Add(new ResultFace(polygon, p, -1));
                else result.Add(new ResultFace(polygon, -1, cuts[p - faces.Count].FaceA));
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
