using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

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

            public static Snapshot Take(Solid solid)
            {
                var faces = solid.Faces.ToList();
                return new Snapshot
                {
                    Counts = faces.Select(x => x.Vertices.Count).ToArray(),
                    Points = faces.SelectMany(x => x.Vertices).ToArray()
                };
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
        /// A rotation and translation, or just a translation.
        /// </summary>
        public class Rigid
        {
            private readonly bool _translateOnly;
            private readonly Vector3 _offset;
            private readonly Vector3 _fromOrigin;
            private readonly Vector3 _toOrigin;
            private readonly Vector3[] _from;
            private readonly Vector3[] _to;

            private Rigid(Vector3 offset)
            {
                _translateOnly = true;
                _offset = offset;
            }

            private Rigid(Vector3 fromOrigin, Vector3 toOrigin, Vector3[] from, Vector3[] to)
            {
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
                       + _to[2] * Vector3.Dot(_from[2], d);
            }

            private Vector3 Direction(Vector3 v)
            {
                if (_translateOnly) return v;
                return _to[0] * Vector3.Dot(_from[0], v)
                       + _to[1] * Vector3.Dot(_from[1], v)
                       + _to[2] * Vector3.Dot(_from[2], v);
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
            /// Find the rotation and translation that takes each point in <paramref name="from"/> onto the point at the same
            /// index in <paramref name="to"/>. Returns null if there isn't one (the shapes aren't the same shape).
            /// </summary>
            public static Rigid Fit(Vector3[] from, Vector3[] to)
            {
                if (from == null || to == null || from.Length != to.Length || from.Length < 3) return null;

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

                var rigid = new Rigid(from[i0], to[i0], ff, tf);

                // Every point has to land where it should, otherwise it's a different shape (or a mirrored one)
                for (var i = 0; i < from.Length; i++)
                {
                    if ((rigid.Point(from[i]) - to[i]).Length() > Epsilon) return null;
                }
                return rigid;
            }
        }

        private static readonly ConditionalWeakTable<MapDocument, Dictionary<long, Snapshot>> Store =
            new ConditionalWeakTable<MapDocument, Dictionary<long, Snapshot>>();

        /// <summary>
        /// The snapshots for a document, by object ID. Lock the dictionary while using it.
        /// </summary>
        public static Dictionary<long, Snapshot> Snapshots(MapDocument document)
        {
            return Store.GetValue(document, _ => new Dictionary<long, Snapshot>());
        }

        /// <summary>
        /// Take a snapshot of every linked solid in the document as it is right now.
        /// </summary>
        public static void SeedAll(MapDocument document)
        {
            var snaps = Snapshots(document);
            var members = LinkedObjects.GetMembers(document);
            lock (snaps)
            {
                snaps.Clear();
                foreach (var solid in members.SelectMany(x => x.Value).OfType<Solid>())
                {
                    snaps[solid.ID] = Snapshot.Take(solid);
                }
            }
        }

        /// <summary>
        /// Make <paramref name="target"/> the same shape as <paramref name="source"/>, with the target keeping its
        /// own position and orientation. <paramref name="rigid"/> takes the source's space into the target's space.
        /// </summary>
        public static void Apply(MapDocument document, Solid source, Solid target, Rigid rigid, Change change)
        {
            var srcFaces = source.Faces.ToList();
            var tgtFaces = target.Faces.ToList();
            var matrix = rigid.ToMatrix();

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

                var sf = srcFaces[i];
                tf.Vertices.Reset(sf.Vertices.Select(rigid.Point).ToList());

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
        }
    }
}
