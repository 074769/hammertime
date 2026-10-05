using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// The geometry side of linked solids.
    ///
    /// Every linked solid has a snapshot of its vertices from before the current edit. Comparing a solid
    /// to its snapshot tells us what the edit was:
    ///  - nothing changed (eg it was only selected): ignore it
    ///  - every vertex moved the same rigid way (move / rotate): the solid is just placed differently. Nothing is shared.
    ///  - anything else (vertex edit, clip, scale...): the shape changed, and the other members follow.
    ///
    /// Each other member follows by taking the edited vertices through the rigid transform that matched
    /// the edited solid's old snapshot to that member's snapshot, so rotated and moved members keep their own
    /// position and orientation while sharing the new shape.
    /// </summary>
    public static class LinkGeometry
    {
        // Vertices closer than this (in map units) are the same vertex. Absorbs float error from rotations at large coordinates.
        public const float Epsilon = 0.05f;

        public class Snapshot
        {
            public int[] Counts;
            public Vector3[] Points;

            /// <summary>
            /// The IDs of the faces, in the order the points and textures are listed. Editing a texture takes a face out of the
            /// solid and puts a copy back at the end, so a solid's faces can be reordered without anything about the shape changing.
            /// Keeping this order is what lets a face of one solid keep being matched with the same face of another.
            /// </summary>
            public long[] FaceIds;

            /// <summary>The texture of each face, so a texture-only edit can be told apart from nothing happening.</summary>
            public Texture[] Textures = new Texture[0];

            /// <summary>For things that aren't solids: the object's normalised serialised form, to tell what kind of edit was made.</summary>
            public SerialisedObject Reference;

            public static Snapshot Take(Solid solid, IList<long> order = null)
            {
                return FromFaces(OrderedFaces(solid, order));
            }

            /// <summary>A snapshot of the given faces, in the given order.</summary>
            public static Snapshot FromFaces(IList<Face> faces)
            {
                return new Snapshot
                {
                    FaceIds = faces.Select(x => x.ID).ToArray(),
                    Counts = faces.Select(x => x.Vertices.Count).ToArray(),
                    Points = faces.SelectMany(x => x.Vertices).ToArray(),
                    Textures = faces.Select(x => x.Texture.Clone()).ToArray()
                };
            }

            /// <summary>
            /// A snapshot of anything linked: solids as <see cref="Take"/>, everything else as its bounding box and form.
            /// </summary>
            public static Snapshot TakeAny(IMapObject obj, Snapshot previous = null)
            {
                if (obj is Solid solid) return Take(solid, previous?.FaceIds);
                var box = obj.BoundingBox;
                return new Snapshot
                {
                    Counts = new[] { 2 },
                    Points = new[] { box.Start, box.End },
                    Reference = LinkedObjects.NormalisedForm(obj)
                };
            }

            public bool SameTextures(Snapshot other)
            {
                if (other == null || Textures.Length != other.Textures.Length) return false;
                for (var i = 0; i < Textures.Length; i++)
                {
                    var a = Textures[i];
                    var b = other.Textures[i];
                    if (a.Name != b.Name) return false;
                    if (Math.Abs(a.Rotation - b.Rotation) > 0.001f) return false;
                    if (Math.Abs(a.XShift - b.XShift) > 0.001f || Math.Abs(a.YShift - b.YShift) > 0.001f) return false;
                    if (Math.Abs(a.XScale - b.XScale) > 0.0001f || Math.Abs(a.YScale - b.YScale) > 0.0001f) return false;
                    if ((a.UAxis - b.UAxis).Length() > 0.0001f || (a.VAxis - b.VAxis).Length() > 0.0001f) return false;
                    if (a.LightmapScale != b.LightmapScale) return false;
                }
                return true;
            }

            /// <summary>The average of the points.</summary>
            public Vector3 Centroid
            {
                get
                {
                    if (Points.Length == 0) return Vector3.Zero;
                    var sum = Vector3.Zero;
                    foreach (var p in Points) sum += p;
                    return sum / Points.Length;
                }
            }

            public bool SameTopology(Snapshot other)
            {
                return other != null && Counts.Length == other.Counts.Length && Counts.SequenceEqual(other.Counts);
            }

            public bool SamePoints(Snapshot other)
            {
                if (!SameTopology(other)) return false;
                for (var i = 0; i < Points.Length; i++)
                {
                    if ((Points[i] - other.Points[i]).Length() > 0.0001f) return false;
                }
                return true;
            }

            /// <summary>
            /// The same points with each face's vertices in the opposite order (which is what flipping an object does).
            /// </summary>
            public Snapshot ReversedFaces()
            {
                var points = new Vector3[Points.Length];
                var offset = 0;
                foreach (var count in Counts)
                {
                    for (var i = 0; i < count; i++) points[offset + i] = Points[offset + count - 1 - i];
                    offset += count;
                }
                return new Snapshot { FaceIds = FaceIds, Counts = Counts, Points = points, Textures = Textures, Reference = Reference };
            }

            /// <summary>False if any point isn't a real number (a broken edit can leave NaN / infinity behind).</summary>
            public bool IsFinite => Points.All(IsFinitePoint);

            public Vector3 Center
            {
                get
                {
                    if (Points.Length == 0) return Vector3.Zero;
                    var min = Points[0];
                    var max = Points[0];
                    foreach (var p in Points)
                    {
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                    return (min + max) / 2;
                }
            }
        }

        /// <summary>
        /// A rotation and translation, a mirrored rotation and translation (a flip), or just a translation.
        /// </summary>
        public class Rigid
        {
            private readonly bool _translateOnly;
            private readonly Vector3 _offset;
            private readonly Vector3 _fromOrigin;
            private readonly Vector3 _toOrigin;
            private readonly Vector3[] _from;
            private readonly Vector3[] _to;
            private readonly float _side = 1;

            /// <summary>True if this flips the shape inside out. Face vertices have to be reversed to keep faces pointing outwards.</summary>
            public bool Mirrored => _side < 0;

            /// <summary>False if the movement has NaN / infinity in it, which would wreck everything it is applied to.</summary>
            public bool IsFinite => LinkGeometry.IsFinite(ToMatrix());

            private Rigid(Vector3 offset)
            {
                _translateOnly = true;
                _offset = offset;
            }

            private Rigid(Vector3 fromOrigin, Vector3 toOrigin, Vector3[] from, Vector3[] to, bool mirrored)
            {
                _side = mirrored ? -1 : 1;
                _fromOrigin = fromOrigin;
                _toOrigin = toOrigin;
                _from = from;
                _to = to;
            }

            public static Rigid Translation(Vector3 offset)
            {
                return new Rigid(offset);
            }

            public Vector3 Point(Vector3 p)
            {
                if (_translateOnly) return p + _offset;
                var d = p - _fromOrigin;
                return _toOrigin
                       + _to[0] * Vector3.Dot(_from[0], d)
                       + _to[1] * Vector3.Dot(_from[1], d)
                       + _to[2] * (_side * Vector3.Dot(_from[2], d));
            }

            /// <summary>Turn a direction (no translation).</summary>
            public Vector3 Direction(Vector3 v)
            {
                if (_translateOnly) return v;
                return _to[0] * Vector3.Dot(_from[0], v)
                       + _to[1] * Vector3.Dot(_from[1], v)
                       + _to[2] * (_side * Vector3.Dot(_from[2], v));
            }

            public Matrix4x4 ToMatrix()
            {
                var x = Direction(Vector3.UnitX);
                var y = Direction(Vector3.UnitY);
                var z = Direction(Vector3.UnitZ);
                var t = Point(Vector3.Zero);
                return new Matrix4x4(
                    x.X, x.Y, x.Z, 0,
                    y.X, y.Y, y.Z, 0,
                    z.X, z.Y, z.Z, 0,
                    t.X, t.Y, t.Z, 1
                );
            }

            // An orthonormal frame built from three of the points: x along the first edge, z across the plane of the three
            private static bool BuildFrame(Vector3[] pts, int i0, int i1, int i2, Vector3[] frame)
            {
                var e1 = pts[i1] - pts[i0];
                if (e1.LengthSquared() < 1e-6f) return false;
                e1 = Vector3.Normalize(e1);
                var n = Vector3.Cross(e1, pts[i2] - pts[i0]);
                if (n.LengthSquared() < 1e-6f) return false;
                var e3 = Vector3.Normalize(n);
                var e2 = Vector3.Cross(e3, e1);
                frame[0] = e1;
                frame[1] = e2;
                frame[2] = e3;
                return true;
            }

            /// <summary>
            /// Find the movement that takes each point of <paramref name="from"/> onto the matching point of <paramref name="to"/>:
            /// a rotation and translation, or a flip (mirror) and translation. Returns null if there isn't one (the shapes are different).
            /// A flipped shape has its face vertices in the opposite order, which is accounted for.
            /// </summary>
            public static Rigid Fit(Snapshot from, Snapshot to)
            {
                if (from == null || !from.SameTopology(to)) return null;
                return FitPoints(from.Points, to.Points, false) ?? FitPoints(from.Points, to.ReversedFaces().Points, true);
            }

            /// <summary>
            /// Every movement (rotation / flip and translation) that takes the set of points <paramref name="from"/> onto the set
            /// of points <paramref name="to"/>, in any order. A symmetrical shape has several (a cube has 24 rotations).
            /// </summary>
            public static List<Rigid> FitSets(Vector3[] from, Vector3[] to)
            {
                var result = new List<Rigid>();
                if (from.Length != to.Length || from.Length < 3) return result;

                var i0 = 0;
                var i1 = 0;
                var best = 0f;
                for (var i = 1; i < from.Length; i++)
                {
                    var d = (from[i] - from[i0]).LengthSquared();
                    if (d > best) { best = d; i1 = i; }
                }
                if (i1 == 0) return result;

                var i2 = -1;
                best = 0f;
                var edge = from[i1] - from[i0];
                for (var i = 1; i < from.Length; i++)
                {
                    if (i == i1) continue;
                    var a = Vector3.Cross(edge, from[i] - from[i0]).LengthSquared();
                    if (a > best) { best = a; i2 = i; }
                }
                if (i2 < 0) return result;

                var ff = new Vector3[3];
                if (!BuildFrame(from, i0, i1, i2, ff)) return result;

                var d01 = (from[i1] - from[i0]).Length();
                var d02 = (from[i2] - from[i0]).Length();
                var d12 = (from[i2] - from[i1]).Length();
                const float tol = 0.1f;

                for (var a = 0; a < to.Length; a++)
                {
                    for (var b = 0; b < to.Length; b++)
                    {
                        if (b == a || Math.Abs((to[b] - to[a]).Length() - d01) > tol) continue;
                        for (var c = 0; c < to.Length; c++)
                        {
                            if (c == a || c == b) continue;
                            if (Math.Abs((to[c] - to[a]).Length() - d02) > tol) continue;
                            if (Math.Abs((to[c] - to[b]).Length() - d12) > tol) continue;

                            var tf = new Vector3[3];
                            if (!BuildFrame(to, a, b, c, tf)) continue;

                            foreach (var mirrored in new[] { false, true })
                            {
                                var rigid = new Rigid(from[i0], to[a], ff, tf, mirrored);
                                var ok = true;
                                foreach (var p in from)
                                {
                                    var m = rigid.Point(p);
                                    if (!to.Any(t => (t - m).Length() <= Epsilon)) { ok = false; break; }
                                }
                                if (ok) result.Add(rigid);
                            }
                        }
                    }
                }
                return result;
            }

            private static Rigid FitPoints(Vector3[] from, Vector3[] to, bool mirrored)
            {
                if (from.Length != to.Length || from.Length < 3) return null;

                // Pick a well spread triple of points so the frame is stable
                var i0 = 0;
                var i1 = 0;
                var best = 0f;
                for (var i = 1; i < from.Length; i++)
                {
                    var d = (from[i] - from[i0]).LengthSquared();
                    if (d > best) { best = d; i1 = i; }
                }
                if (i1 == 0) return null;

                var i2 = -1;
                best = 0f;
                var edge = from[i1] - from[i0];
                for (var i = 1; i < from.Length; i++)
                {
                    if (i == i1) continue;
                    var a = Vector3.Cross(edge, from[i] - from[i0]).LengthSquared();
                    if (a > best) { best = a; i2 = i; }
                }
                if (i2 < 0) return null;

                var ff = new Vector3[3];
                var tf = new Vector3[3];
                if (!BuildFrame(from, i0, i1, i2, ff) || !BuildFrame(to, i0, i1, i2, tf)) return null;

                var rigid = new Rigid(from[i0], to[i0], ff, tf, mirrored);

                // Every point has to land where it should, otherwise it's a different shape
                for (var i = 0; i < from.Length; i++)
                {
                    // Written so that NaN fails too: a comparison with NaN is never true
                    if (!((rigid.Point(from[i]) - to[i]).Length() <= Epsilon)) return null;
                }
                return rigid.IsFinite ? rigid : null;
            }
        }

        /// <summary>
        /// The solid's faces in the given order (a list of face IDs), or in the solid's own order if the list doesn't fit the faces any more.
        /// </summary>
        public static List<Face> OrderedFaces(Solid solid, IList<long> order)
        {
            var faces = solid.Faces.ToList();
            if (order == null || order.Count != faces.Count) return faces;

            var byId = new Dictionary<long, Face>();
            foreach (var f in faces)
            {
                if (byId.ContainsKey(f.ID)) return faces;
                byId[f.ID] = f;
            }

            var result = new List<Face>(faces.Count);
            foreach (var id in order)
            {
                if (!byId.TryGetValue(id, out var f)) return faces;
                result.Add(f);
            }
            return result;
        }

        private static Vector3[] PointsOf(Solid solid)
        {
            var result = new List<Vector3>();
            foreach (var v in solid.Faces.SelectMany(x => x.Vertices))
            {
                if (!result.Any(x => (x - v).Length() <= 0.05f)) result.Add(v);
            }
            return result.ToArray();
        }

        /// <summary>
        /// How the given instance sits compared with the origin instance (the movement that takes the origin onto it), found from
        /// the brushes the two have in common. A symmetrical brush fits several movements on its own, so the movement
        /// that suits the most brushes is the one used. Brushes with broken geometry take no part.
        /// </summary>
        public static Rigid InstanceTransform(LinkedObjects.LinkIndex index, long linkId, long originInstance, long instance, IMapObject near)
        {
            var pairs = new List<(Solid origin, Solid other)>();
            foreach (var members in index.Slots.Values)
            {
                var o = members.FirstOrDefault(x => LinkedObjects.GetTopId(x) == linkId && LinkedObjects.GetInstance(x) == originInstance) as Solid;
                var c = members.FirstOrDefault(x => LinkedObjects.GetTopId(x) == linkId && LinkedObjects.GetInstance(x) == instance) as Solid;
                if (o == null || c == null) continue;
                if (o.Faces.Any(f => f.Vertices.Any(v => !IsFinitePoint(v))) || c.Faces.Any(f => f.Vertices.Any(v => !IsFinitePoint(v)))) continue;
                pairs.Add((o, c));
            }
            if (pairs.Count == 0) return null;

            var centre = near.BoundingBox.Center;
            if (!IsFinitePoint(centre)) centre = Vector3.Zero;
            pairs = pairs.OrderBy(p => (p.origin.BoundingBox.Center - centre).LengthSquared()).ToList();

            var originPoints = pairs.Select(p => PointsOf(p.origin)).ToList();
            var otherPoints = pairs.Select(p => PointsOf(p.other)).ToList();

            // Candidates: what the nearest brushes fit exactly (face for face), then every other way they could be placed on each other
            var candidates = new List<Rigid>();
            for (var i = 0; i < Math.Min(6, pairs.Count); i++)
            {
                var exact = Rigid.Fit(Snapshot.Take(pairs[i].origin), Snapshot.Take(pairs[i].other));
                if (exact != null) candidates.Add(exact);
            }
            for (var i = 0; i < Math.Min(6, pairs.Count); i++)
            {
                candidates.AddRange(Rigid.FitSets(originPoints[i], otherPoints[i]));
            }

            Rigid best = null;
            var bestScore = 0;
            foreach (var candidate in candidates)
            {
                var score = 0;
                for (var i = 0; i < pairs.Count; i++)
                {
                    if (originPoints[i].Length != otherPoints[i].Length) continue;
                    if (originPoints[i].All(v => { var m = candidate.Point(v); return otherPoints[i].Any(w => (w - m).Length() <= 0.1f); })) score++;
                }
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        public static bool IsFinitePoint(Vector3 v)
        {
            return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z)
                     || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z));
        }

        private static bool IsFinite(Matrix4x4 m)
        {
            return IsFinitePoint(new Vector3(m.M11, m.M12, m.M13)) && IsFinitePoint(new Vector3(m.M21, m.M22, m.M23))
                   && IsFinitePoint(new Vector3(m.M31, m.M32, m.M33)) && IsFinitePoint(new Vector3(m.M41, m.M42, m.M43));
        }

        private static Vector3[] UniquePoints(IEnumerable<Vector3> points)
        {
            var result = new List<Vector3>();
            foreach (var p in points)
            {
                if (!result.Any(x => (x - p).Length() <= Epsilon)) result.Add(p);
            }
            return result.ToArray();
        }

        private static bool SameTexture(Texture a, Texture b)
        {
            return a.Name == b.Name
                   && Math.Abs(a.XShift - b.XShift) <= 0.01f && Math.Abs(a.YShift - b.YShift) <= 0.01f
                   && Math.Abs(a.XScale - b.XScale) <= 0.0001f && Math.Abs(a.YScale - b.YScale) <= 0.0001f
                   && (a.UAxis - b.UAxis).Length() <= 0.001f && (a.VAxis - b.VAxis).Length() <= 0.001f;
        }

        /// <summary>
        /// Lists the faces of <paramref name="target"/> in the order that matches the faces of <paramref name="originPre"/>,
        /// and finds the movement that takes the one onto the other, working it out from where the faces actually are.
        /// Used when the face order the two solids were last matched with has been lost (eg the map was saved and reopened,
        /// which stores the faces in the order they happen to be in). Returns null if the shapes aren't the same.
        /// </summary>
        private static Snapshot MatchByGeometry(Snapshot originPre, Solid target, out Rigid rigid)
        {
            rigid = null;
            var tFaces = target.Faces.ToList();
            if (originPre == null || tFaces.Count != originPre.Counts.Length) return null;

            var oPts = UniquePoints(originPre.Points);
            var tPts = UniquePoints(tFaces.SelectMany(x => x.Vertices));
            if (oPts.Length != tPts.Length) return null;

            // Where each face of the origin starts in its list of points
            var starts = new int[originPre.Counts.Length];
            for (var i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + originPre.Counts[i - 1];

            Snapshot bestSnap = null;
            Rigid bestRigid = null;
            var bestScore = -1;

            foreach (var candidate in Rigid.FitSets(oPts, tPts))
            {
                var matrix = candidate.ToMatrix();
                var used = new HashSet<int>();
                var order = new List<Face>();
                var score = 0;
                var ok = true;

                for (var f = 0; f < starts.Length && ok; f++)
                {
                    var mapped = new List<Vector3>();
                    for (var k = 0; k < originPre.Counts[f]; k++) mapped.Add(candidate.Point(originPre.Points[starts[f] + k]));

                    var found = -1;
                    for (var t = 0; t < tFaces.Count && found < 0; t++)
                    {
                        if (used.Contains(t) || tFaces[t].Vertices.Count != mapped.Count) continue;
                        var verts = tFaces[t].Vertices.ToList();
                        if (mapped.All(m => verts.Any(v => (v - m).Length() <= Epsilon))) found = t;
                    }

                    if (found < 0) { ok = false; break; }
                    used.Add(found);
                    order.Add(tFaces[found]);

                    // A symmetrical shape fits several ways: the way the textures line up decides
                    if (f < originPre.Textures.Length)
                    {
                        var tex = originPre.Textures[f].Clone();
                        tex.TransformUniform(matrix);
                        var other = tFaces[found].Texture;
                        if (tex.Name == other.Name) score++;
                        if (SameTexture(tex, other)) score++;
                    }
                }

                if (!ok || score <= bestScore) continue;
                bestScore = score;
                bestRigid = candidate;
                bestSnap = Snapshot.FromFaces(order);
            }

            rigid = bestRigid;
            return bestSnap;
        }

        /// <summary>
        /// The target's faces listed in the order that matches the origin's snapshot (face for face), with the movement that takes
        /// the origin onto the target. If the order the two were last matched with still holds, that is used. Otherwise
        /// the faces are matched by where they are. The rigid is null if the target isn't the same shape as the origin.
        /// </summary>
        public static Snapshot AlignTo(Snapshot originPre, Solid target, Snapshot targetLast, out Rigid rigid)
        {
            var plain = Snapshot.Take(target, targetLast?.FaceIds);
            rigid = Rigid.Fit(originPre, plain);
            if (rigid != null) return plain;

            var matched = MatchByGeometry(originPre, target, out rigid);
            if (matched != null && rigid != null) return matched;

            rigid = null;
            return plain;
        }

        /// <summary>
        /// Stores the target's faces in the same order as the origin's, so that the pairing of the two survives the map being
        /// saved and reopened (the faces are stored in this order, and that order is what pairs them up on loading).
        /// </summary>
        public static void MirrorFaceOrder(Solid origin, IList<long> originOrder, Solid target, IList<long> targetOrder)
        {
            if (originOrder == null || targetOrder == null || originOrder.Count != targetOrder.Count) return;

            var pair = new Dictionary<long, long>();
            for (var i = 0; i < originOrder.Count; i++)
            {
                if (pair.ContainsKey(originOrder[i])) return;
                pair[originOrder[i]] = targetOrder[i];
            }

            var current = target.Faces.ToList();
            var byId = new Dictionary<long, Face>();
            foreach (var f in current)
            {
                if (byId.ContainsKey(f.ID)) return;
                byId[f.ID] = f;
            }

            var desired = new List<Face>();
            foreach (var of in origin.Faces)
            {
                if (!pair.TryGetValue(of.ID, out var tid) || !byId.TryGetValue(tid, out var tf)) return;
                if (desired.Contains(tf)) return;
                desired.Add(tf);
            }
            if (desired.Count != current.Count || desired.SequenceEqual(current)) return;

            foreach (var f in current) target.Data.Remove(f);
            foreach (var f in desired) target.Data.Add(f);
        }

        /// <summary>
        /// A snapshot of the solid in the face order of its last snapshot.
        /// </summary>
        public static Snapshot TakeOrdered(MapDocument document, Solid solid)
        {
            var snaps = Snapshots(document);
            Snapshot previous;
            lock (snaps) snaps.TryGetValue(solid, out previous);
            return Snapshot.Take(solid, previous?.FaceIds);
        }

        /// <summary>
        /// Snapshots by object. They're keyed by the object itself rather than its ID, so two objects that
        /// somehow share an ID can't be mistaken for each other, and removed objects are forgotten automatically.
        /// </summary>
        public class SnapshotStore
        {
            private ConditionalWeakTable<IMapObject, Snapshot> _table = new ConditionalWeakTable<IMapObject, Snapshot>();

            public Snapshot this[IMapObject obj]
            {
                get => _table.TryGetValue(obj, out var s) ? s : throw new KeyNotFoundException();
                set
                {
                    _table.Remove(obj);
                    _table.Add(obj, value);
                }
            }

            public bool ContainsKey(IMapObject obj)
            {
                return _table.TryGetValue(obj, out _);
            }

            public bool TryGetValue(IMapObject obj, out Snapshot snapshot)
            {
                return _table.TryGetValue(obj, out snapshot);
            }

            public void Remove(IMapObject obj)
            {
                _table.Remove(obj);
            }

            public void Clear()
            {
                _table = new ConditionalWeakTable<IMapObject, Snapshot>();
            }
        }

        private static readonly ConditionalWeakTable<MapDocument, SnapshotStore> Store =
            new ConditionalWeakTable<MapDocument, SnapshotStore>();

        /// <summary>
        /// The snapshots for a document. Lock the store while using it.
        /// </summary>
        public static SnapshotStore Snapshots(MapDocument document)
        {
            return Store.GetValue(document, _ => new SnapshotStore());
        }

        /// <summary>
        /// Take a snapshot of every linked object in the document as it is right now.
        /// </summary>
        public static void SeedAll(MapDocument document)
        {
            var snaps = Snapshots(document);
            var members = LinkedObjects.GetMembers(document);
            lock (snaps)
            {
                snaps.Clear();
                foreach (var o in members.SelectMany(x => x.Value))
                {
                    snaps[o] = Snapshot.TakeAny(o);
                }
            }
        }

        /// <summary>
        /// Make <paramref name="target"/> the same shape as <paramref name="source"/>, with the target keeping its
        /// own position and orientation. <paramref name="rigid"/> takes the source's space into the target's space.
        /// </summary>
        public static List<long> Apply(MapDocument document, Solid source, Snapshot sourceOrder, Solid target, Snapshot targetOrder, Rigid rigid, Change change)
        {
            var srcFaces = OrderedFaces(source, sourceOrder?.FaceIds);
            var tgtFaces = OrderedFaces(target, targetOrder?.FaceIds);
            var matrix = rigid.ToMatrix();
            var order = new List<long>();

            // Never write a broken shape into the other instances: leave them as they are
            if (!IsFinite(matrix) || srcFaces.Any(f => f.Vertices.Any(v => !IsFinitePoint(v))))
            {
                return tgtFaces.Select(x => x.ID).ToList();
            }

            for (var i = 0; i < srcFaces.Count; i++)
            {
                Face tf;
                if (i < tgtFaces.Count)
                {
                    tf = tgtFaces[i];
                }
                else
                {
                    tf = new Face(document.Map.NumberGenerator.Next("Face"));
                    target.Data.Add(tf);
                }
                order.Add(tf.ID);

                var sf = srcFaces[i];
                var mapped = sf.Vertices.Select(rigid.Point).ToList();
                if (rigid.Mirrored) mapped.Reverse(); // a flipped face needs its vertices the other way round to keep facing outwards
                tf.Vertices.Reset(mapped);

                var tex = sf.Texture.Clone();
                tex.TransformUniform(matrix);
                tf.Texture.Unclone(tex);
                tf.Texture.LightmapScale = tex.LightmapScale;
            }

            // The shape lost faces (eg a vertex merge)
            for (var i = srcFaces.Count; i < tgtFaces.Count; i++)
            {
                target.Data.Remove(tgtFaces[i]);
            }

            target.DescendantsChanged();
            change.Update(target);
            return order;
        }

        /// <summary>
        /// Move a solid the way its origin counterpart was just moved: every point is turned by the same rotation (or flip)
        /// around the solid's own centre, and shifted by the same amount as the origin object's centre.
        /// </summary>
        public static void ApplyPlacement(MapDocument document, Solid target, Rigid delta, Rigid instance, Change change)
        {
            // The origin was moved by delta. The target is a copy of the origin that was turned and moved by instance, so the
            // same movement for the target is the origin's as seen from the target: back into the origin's frame, move, and out again.
            // (Moving the origin along its x axis moves a copy that was turned a quarter turn along its own y axis.)
            var movement = delta.ToMatrix();
            var toTarget = instance.ToMatrix();
            if (!Matrix4x4.Invert(toTarget, out var fromTarget)) return;
            var matrix = fromTarget * movement * toTarget;
            if (!IsFinite(matrix)) return;

            var textureLock = (document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags()).TextureLock;

            foreach (var face in target.Faces.ToList())
            {
                var mapped = face.Vertices.Select(v => Vector3.Transform(v, matrix)).ToList();
                if (delta.Mirrored) mapped.Reverse();
                face.Vertices.Reset(mapped);
                if (textureLock) face.Texture.TransformUniform(matrix);
            }

            target.DescendantsChanged();
            change.Update(target);
        }

        /// <summary>
        /// Give <paramref name="target"/> the textures of <paramref name="source"/>, face for face, turned to suit the target's orientation.
        /// The shapes must be the same.
        /// </summary>
        public static void ApplyTextures(Solid source, Snapshot sourceOrder, Solid target, Snapshot targetOrder, Rigid rigid, Change change)
        {
            var srcFaces = OrderedFaces(source, sourceOrder?.FaceIds);
            var tgtFaces = OrderedFaces(target, targetOrder?.FaceIds);
            var matrix = rigid.ToMatrix();

            for (var i = 0; i < Math.Min(srcFaces.Count, tgtFaces.Count); i++)
            {
                var tex = srcFaces[i].Texture.Clone();
                tex.TransformUniform(matrix);
                tgtFaces[i].Texture.Unclone(tex);
                tgtFaces[i].Texture.LightmapScale = tex.LightmapScale;
            }

            change.Update(target);
        }
    }
}
