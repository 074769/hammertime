using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// For tools that build geometry themselves (rather than editing an existing object, which the sync handler already
    /// follows): works out what the other instances of a link need so that they follow an edit made in the origin instance.
    ///
    /// Create one before the tool's transaction is built, then ask it for the extra operations as the tool queues its own.
    /// Edits made outside the origin instance are local, so nothing is produced for them.
    /// </summary>
    public class LinkFollower
    {
        private readonly MapDocument _document;
        private readonly LinkedObjects.LinkIndex _index;
        private long _next;

        public LinkFollower(MapDocument document)
        {
            _document = document;
            _index = LinkedObjects.BuildIndex(document);
            _next = LinkedObjects.NextGroupId(document);
        }

        /// <summary>True if the object is in the origin instance of a link that has other instances.</summary>
        public bool IsFollowed(IMapObject obj)
        {
            return OtherInstances(obj).Count > 0;
        }

        private List<long> OtherInstances(IMapObject obj)
        {
            var slot = LinkedObjects.GetLinkId(obj);
            if (slot == null) return new List<long>();

            var top = LinkedObjects.GetTopId(obj);
            if (!_index.Links.TryGetValue(top, out var instances)) return new List<long>();

            var origin = _index.OriginInstance(top);
            if (LinkedObjects.GetInstance(obj) != origin) return new List<long>();
            return instances.Keys.Where(x => x != origin).ToList();
        }

        private IMapObject CounterpartOf(IMapObject obj, long instance)
        {
            var slot = LinkedObjects.GetLinkId(obj);
            if (slot == null || !_index.Slots.TryGetValue(slot.Value, out var members)) return null;
            return members.FirstOrDefault(x => LinkedObjects.GetInstance(x) == instance && !ReferenceEquals(x, obj));
        }

        /// <summary>How the other instance sits compared with the origin instance, or null if that can't be worked out.</summary>
        private LinkGeometry.Rigid PlacementOf(IMapObject source, IMapObject counterpart, long instance, IMapObject near)
        {
            var top = LinkedObjects.GetTopId(source);
            var rigid = LinkGeometry.InstanceTransform(_index, top, _index.OriginInstance(top), instance, near);
            if (rigid == null && counterpart != null)
            {
                if (source is Solid s && counterpart is Solid c)
                {
                    rigid = LinkGeometry.Rigid.Fit(LinkGeometry.Snapshot.Take(s), LinkGeometry.Snapshot.Take(c));
                }
                if (rigid == null)
                {
                    rigid = LinkGeometry.Rigid.Translation(counterpart.BoundingBox.Center - source.BoundingBox.Center);
                }
            }
            return rigid != null && rigid.IsFinite ? rigid : null;
        }

        /// <summary>
        /// A new object was made next to <paramref name="source"/> (a linked object in the origin instance) and has not been attached yet.
        /// It joins the link, and each other instance gets a linked copy of it beside that instance's own copy of <paramref name="source"/>.
        /// Call this before queueing the Attach of <paramref name="created"/>, then queue that Attach as usual.
        /// </summary>
        public void Added(Action<IOperation> add, IMapObject source, Solid created)
        {
            var others = OtherInstances(source);
            if (others.Count == 0) return;

            var top = LinkedObjects.GetTopId(source);
            var origin = _index.OriginInstance(top);
            _index.Groups.TryGetValue(top, out var link);

            var slotId = _next++;
            add(new AddMapData(new LinkGroup { ID = slotId, Name = (link?.Name ?? "Link") + " / " + slotId, Colour = LinkedObjects.ColourFor(top), ParentID = top }));

            // Copy first, so the copies carry no link data of their own
            var copies = new List<KeyValuePair<long, Solid>>();
            foreach (var instance in others)
            {
                var counterpart = CounterpartOf(source, instance);
                var rigid = PlacementOf(source, counterpart, instance, created);
                if (rigid == null) continue; // no safe way to place it: that instance simply doesn't get it

                var copy = (Solid) created.Copy(_document.Map.NumberGenerator);
                foreach (var o in copy.FindAll()) o.Data.Remove(x => x is LinkGroupID);
                LinkedObjects.TransformObject(copy, rigid.ToMatrix(), rigid.Mirrored);
                copy.Data.Add(new LinkGroupID(slotId, top, instance));

                copies.Add(new KeyValuePair<long, Solid>(counterpart?.Hierarchy.Parent?.ID ?? _document.Map.Root.ID, copy));
            }

            created.Data.Add(new LinkGroupID(slotId, top, origin));
            foreach (var kv in copies) add(new Attach(kv.Key, kv.Value));
        }

        /// <summary>
        /// A face of <paramref name="source"/> (a linked solid in the origin instance) is being replaced by <paramref name="replacement"/>
        /// (same place, new texture). The same face of the other instances' solids is replaced the same way.
        /// </summary>
        public void FaceReplaced(Action<IOperation> add, Solid source, Face old, Face replacement)
        {
            foreach (var instance in OtherInstances(source))
            {
                var counterpart = CounterpartOf(source, instance) as Solid;
                if (counterpart == null) continue;

                var rigid = PlacementOf(source, counterpart, instance, source);
                if (rigid == null) continue;

                // Where the face is in the other instance
                var wanted = old.Vertices.Select(rigid.Point).ToList();
                var match = counterpart.Faces.FirstOrDefault(f => SameCorners(f.Vertices, wanted));
                if (match == null) continue;

                var texture = replacement.Texture.Clone();
                texture.TransformUniform(rigid.ToMatrix());

                var face = new Face(_document.Map.NumberGenerator.Next("Face")) { Texture = texture };
                face.Vertices.AddRange(match.Vertices);
                face.Plane = match.Plane;

                add(new RemoveMapObjectData(counterpart.ID, new[] { match }));
                add(new AddMapObjectData(counterpart.ID, new[] { face }));
            }
        }

        private static bool SameCorners(IEnumerable<Vector3> a, IEnumerable<Vector3> b)
        {
            var left = a.ToList();
            var right = b.ToList();
            if (left.Count != right.Count || left.Count == 0) return false;
            return left.All(p => right.Any(q => (p - q).Length() <= 0.05f));
        }
    }
}
