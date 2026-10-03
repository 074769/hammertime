using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Keeps linked objects in sync.
    ///
    /// A link is a group of objects, and the copies of that group are its instances. Each object in an instance has a
    /// matching object in every other instance (together they are a "slot"). Objects in the same instance are never
    /// tied to each other: each one is edited individually.
    ///
    /// The origin instance is the master. Whatever is done to an object in the origin instance is passed on to the matching
    /// object in every other instance: moving, rotating or flipping it, changing its shape (vertices, clipping,
    /// scaling...), its textures, its properties. Objects in other instances can be edited freely and locally;
    /// what's done to them is never passed on, to the origin or anywhere else.
    ///
    /// See <see cref="LinkGeometry"/> for how the different kinds of edit are told apart.
    ///
    /// The other members are always derived from the edited origin object, so undo and redo need no special handling:
    /// reversing the edit raises another change, which is synced the same way.
    ///
    /// Runs before the visgroup handlers so that anything it changes is processed by them.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectSyncHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "J";

        private enum Kind
        {
            None,
            Placement,
            Shape,
            Texture
        }

        public Task Changed(Change change)
        {
            var doc = change.Document;
            var snaps = LinkGeometry.Snapshots(doc);

            foreach (var removed in change.Removed)
            {
                lock (snaps) snaps.Remove(removed.ID);
            }

            var addedLinked = change.Added.Where(x => LinkedObjects.GetLinkId(x) != null).ToList();
            var updatedLinked = change.Updated.Select(LinkedObjects.FindLinkedOwner).Where(x => x != null).Distinct().ToList();
            if (addedLinked.Count == 0 && updatedLinked.Count == 0) return Task.CompletedTask;

            var index = LinkedObjects.BuildIndex(doc);

            if (addedLinked.Count > 0) SeparateCopies(change, addedLinked, index);

            foreach (var added in addedLinked)
            {
                lock (snaps) snaps[added.ID] = LinkGeometry.Snapshot.TakeAny(added);
            }

            foreach (var slotId in updatedLinked.Select(x => LinkedObjects.GetLinkId(x).Value).Distinct())
            {
                if (!index.Slots.TryGetValue(slotId, out var members)) continue;

                // A member we know nothing about yet starts from how it is now
                lock (snaps)
                {
                    foreach (var m in members)
                    {
                        if (!snaps.ContainsKey(m.ID)) snaps[m.ID] = LinkGeometry.Snapshot.TakeAny(m);
                    }
                }

                var origin = index.OriginOf(slotId);
                if (origin is Solid) SyncSolids(change, members, origin, updatedLinked, snaps);
                else SyncOthers(change, members, origin, updatedLinked, snaps);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// A copy of a linked object (paste, duplicate) keeps its place in the link. If that place is already taken,
        /// the copies are a new instance of the link: all the copies that came from one instance share one new instance.
        /// </summary>
        private static void SeparateCopies(Change change, List<IMapObject> added, LinkedObjects.LinkIndex index)
        {
            var addedSet = new HashSet<IMapObject>(added);
            var taken = new HashSet<string>();
            foreach (var kv in index.Slots)
            {
                foreach (var o in kv.Value.Where(x => !addedSet.Contains(x)))
                {
                    taken.Add(kv.Key + ":" + LinkedObjects.GetInstance(o));
                }
            }

            var newInstance = new Dictionary<string, long>();
            foreach (var o in added)
            {
                var d = o.Data.GetOne<LinkGroupID>();
                var top = LinkedObjects.GetTopId(o);
                if (top == 0 || !taken.Contains(d.ID + ":" + d.Instance)) continue;

                // All the copies from the same instance of the same link go to the same new instance
                var key = top + ":" + d.Instance;
                if (!newInstance.TryGetValue(key, out var instance))
                {
                    var existing = index.Links.TryGetValue(top, out var instances) ? instances.Keys.DefaultIfEmpty(0).Max() : 0;
                    var already = newInstance.Where(x => x.Key.StartsWith(top + ":")).Select(x => x.Value).DefaultIfEmpty(0).Max();
                    instance = Math.Max(existing, already) + 1;
                    newInstance[key] = instance;
                }

                o.Data.Replace(new LinkGroupID(d.ID, d.TopID, instance));
                change.Update(o);
            }
        }

        private static bool Contains(IEnumerable<IMapObject> list, IMapObject o)
        {
            return list.Any(x => ReferenceEquals(x, o));
        }

        private static Kind Classify(LinkGeometry.Snapshot pre, LinkGeometry.Snapshot now)
        {
            if (!pre.SameTopology(now)) return Kind.Shape;
            if (!pre.SamePoints(now)) return LinkGeometry.Rigid.Fit(pre, now) != null ? Kind.Placement : Kind.Shape;
            if (!pre.SameTextures(now)) return Kind.Texture;
            return Kind.None;
        }

        private static void SyncSolids(Change change, List<IMapObject> members, IMapObject originObject, List<IMapObject> touched, Dictionary<long, LinkGeometry.Snapshot> snaps)
        {
            var origin = (Solid) originObject;
            var solids = members.OfType<Solid>().ToList();

            // What was done to each touched solid since we last looked
            var edits = new Dictionary<Solid, Kind>();
            var nows = new Dictionary<Solid, LinkGeometry.Snapshot>();
            foreach (var s in solids.Where(x => Contains(touched, x)))
            {
                LinkGeometry.Snapshot pre;
                lock (snaps) pre = snaps[s.ID];

                var now = LinkGeometry.Snapshot.Take(s);
                var kind = Classify(pre, now);
                if (kind == Kind.None) continue;

                nows[s] = now;
                edits[s] = kind;

                // Objects outside the origin instance are edited locally: nothing is passed on
                if (!ReferenceEquals(s, origin)) lock (snaps) snaps[s.ID] = now;
            }

            if (!edits.TryGetValue(origin, out var originKind)) return;

            LinkGeometry.Snapshot originPre;
            lock (snaps) originPre = snaps[origin.ID];
            var originNow = nows[origin];

            // Anything that was edited in this same change (eg a whole selection moved at once) already is where it should be
            var targets = solids.Where(x => !ReferenceEquals(x, origin) && !edits.ContainsKey(x)).ToList();

            // Take each target's frame from the old snapshots before anything is overwritten
            var plan = new List<KeyValuePair<Solid, LinkGeometry.Rigid>>();
            LinkGeometry.Rigid delta = null;
            lock (snaps)
            {
                if (originKind == Kind.Placement) delta = LinkGeometry.Rigid.Fit(originPre, originNow);

                foreach (var target in targets)
                {
                    var targetPre = snaps[target.ID];
                    var rigid = LinkGeometry.Rigid.Fit(originPre, targetPre);

                    // Not the same shape (the target was edited locally): line up their centres
                    if (rigid == null) rigid = LinkGeometry.Rigid.Translation(targetPre.Center - originPre.Center);

                    plan.Add(new KeyValuePair<Solid, LinkGeometry.Rigid>(target, rigid));
                }
            }

            foreach (var kv in plan)
            {
                var target = kv.Key;
                switch (originKind)
                {
                    case Kind.Placement:
                        if (delta == null) break;
                        LinkGeometry.ApplyPlacement(change.Document, target, LinkGeometry.Snapshot.Take(target), delta, originNow.Centroid - originPre.Centroid, change);
                        break;

                    case Kind.Shape:
                        LinkGeometry.Apply(change.Document, origin, target, kv.Value, change);
                        break;

                    case Kind.Texture:
                        LinkGeometry.ApplyTextures(origin, target, kv.Value, change);
                        break;
                }
            }

            lock (snaps)
            {
                snaps[origin.ID] = LinkGeometry.Snapshot.Take(origin);
                foreach (var kv in plan) snaps[kv.Key.ID] = LinkGeometry.Snapshot.Take(kv.Key);
            }
        }

        private static void SyncOthers(Change change, List<IMapObject> members, IMapObject origin, List<IMapObject> touched, Dictionary<long, LinkGeometry.Snapshot> snaps)
        {
            // Objects outside the origin instance are edited locally: nothing is passed on
            foreach (var m in members.Where(x => !ReferenceEquals(x, origin) && Contains(touched, x)))
            {
                lock (snaps) snaps[m.ID] = LinkGeometry.Snapshot.TakeAny(m);
            }

            if (!Contains(touched, origin)) return;

            LinkGeometry.Snapshot pre;
            lock (snaps) pre = snaps[origin.ID];
            var now = LinkGeometry.Snapshot.TakeAny(origin);

            var sameForm = pre.Reference != null && LinkedObjects.Equivalent(pre.Reference, now.Reference);
            var moved = (pre.Center - now.Center).Length() > 0.0001f;
            var targets = members.Where(x => !ReferenceEquals(x, origin) && x.GetType() == origin.GetType()).ToList();

            if (sameForm && !moved) return; // eg it was just selected

            if (sameForm)
            {
                // Only moved: the others move the same amount
                var offset = now.Center - pre.Center;
                var matrix = Matrix4x4.CreateTranslation(offset);
                var textureLock = (change.Document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags()).TextureLock;

                foreach (var target in targets.Where(x => !Contains(touched, x)))
                {
                    target.Transform(matrix);
                    if (textureLock)
                    {
                        foreach (var o in target.FindAll())
                        {
                            foreach (var t in o.Data.OfType<ITextured>()) t.Texture?.TransformUniform(matrix);
                        }
                    }
                    target.DescendantsChanged();
                    change.Update(target);
                    lock (snaps) snaps[target.ID] = LinkGeometry.Snapshot.TakeAny(target);
                }
            }
            else
            {
                // Its properties or contents were edited: the others take them, each keeping its own position and angles
                foreach (var target in targets.Where(x => !Contains(touched, x)))
                {
                    if (!LinkedObjects.AreEquivalent(origin, target)) LinkedObjects.Sync(change.Document, origin, target, change);
                    lock (snaps) snaps[target.ID] = LinkGeometry.Snapshot.TakeAny(target);
                }
            }

            lock (snaps) snaps[origin.ID] = LinkGeometry.Snapshot.TakeAny(origin);
        }
    }
}
