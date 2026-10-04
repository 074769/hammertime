using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
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

            LinkGroupSelection.Update(change);

            foreach (var removed in change.Removed)
            {
                lock (snaps) snaps.Remove(removed);
            }

            var addedLinked = change.Added.Where(x => LinkedObjects.GetLinkId(x) != null).ToList();
            var updatedLinked = change.Updated.Select(LinkedObjects.FindLinkedOwner).Where(x => x != null).Distinct().ToList();
            if (addedLinked.Count == 0 && updatedLinked.Count == 0) return Task.CompletedTask;

            var index = LinkedObjects.BuildIndex(doc);

            if (addedLinked.Count > 0) SeparateCopies(change, addedLinked, index);

            foreach (var added in addedLinked)
            {
                lock (snaps) snaps[added] = LinkGeometry.Snapshot.TakeAny(added);
            }

            var slotIds = updatedLinked.Select(x => LinkedObjects.GetLinkId(x).Value).Distinct().ToList();

            // Anything we know nothing about yet starts from how it is now
            lock (snaps)
            {
                foreach (var m in slotIds.Where(index.Slots.ContainsKey).SelectMany(x => index.Slots[x]))
                {
                    if (!snaps.ContainsKey(m)) snaps[m] = LinkGeometry.Snapshot.TakeAny(m);
                }
            }

            // Moving a whole instance (every object of it together) is moving the group: that's decided now, while every
            // object still has its snapshot from before the change, because syncing refreshes the snapshots one slot at a time
            var groupMoves = new HashSet<string>();
            foreach (var o in updatedLinked)
            {
                var key = InstanceKey(o);
                if (groupMoves.Contains(key)) continue;
                if (IsGroupMove(doc, index, o, updatedLinked, snaps)) groupMoves.Add(key);
            }

            foreach (var slotId in slotIds)
            {
                if (!index.Slots.TryGetValue(slotId, out var members)) continue;

                var origin = index.OriginOf(slotId);
                var groupMove = groupMoves.Contains(InstanceKey(origin));
                if (origin is Solid) SyncSolids(change, members, origin, updatedLinked, snaps, groupMove);
                else SyncOthers(change, members, origin, updatedLinked, snaps, groupMove);
            }

            // Anything that isn't a solid is measured by its bounding box, which moves with the brushes inside it:
            // take its snapshot again now that they've all been dealt with
            foreach (var slotId in slotIds)
            {
                if (!index.Slots.TryGetValue(slotId, out var members)) continue;
                foreach (var m in members.Where(x => !(x is Solid)))
                {
                    lock (snaps) snaps[m] = LinkGeometry.Snapshot.TakeAny(m);
                }
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

        private static string InstanceKey(IMapObject o)
        {
            return LinkedObjects.GetTopId(o) + ":" + LinkedObjects.GetInstance(o);
        }

        /// <summary>
        /// True if every (visible) object of this object's instance was moved, rotated or flipped in this change.
        /// That's somebody moving the whole group, which only places the group: it isn't an edit to its objects, so
        /// nothing is passed on. (Moving some of the objects, or the only object of an instance, is an edit to those objects.)
        /// </summary>
        private static bool IsGroupMove(MapDocument doc, LinkedObjects.LinkIndex index, IMapObject o, List<IMapObject> touched, LinkGeometry.SnapshotStore snaps)
        {
            if (!index.Links.TryGetValue(LinkedObjects.GetTopId(o), out var instances)) return false;
            if (!instances.TryGetValue(LinkedObjects.GetInstance(o), out var all)) return false;

            var visible = all.Where(x => !x.Data.OfType<IObjectVisibility>().Any(v => v.IsHidden)).ToList();

            // The whole instance was selected as a group (by clicking its label): that is the group, however many objects it has
            if (LinkGroupSelection.Covers(doc, LinkedObjects.GetTopId(o), LinkedObjects.GetInstance(o), visible)) return true;

            if (visible.Count < 2) return false;

            foreach (var m in visible)
            {
                // An entity with brushes of its own: it was only placed if every one of its brushes was only placed
                var brushes = LinkedObjects.ChildSolids(m).Where(x => LinkedObjects.GetLinkId(x) != null).ToList();
                if (!(m is Solid) && brushes.Count > 0)
                {
                    if (!Contains(touched, m) && !brushes.Any(x => Contains(touched, x))) return false;
                    foreach (var b in brushes)
                    {
                        LinkGeometry.Snapshot brushPre;
                        lock (snaps)
                        {
                            if (!snaps.TryGetValue(b, out brushPre)) return false;
                        }
                        if (Classify(brushPre, LinkGeometry.Snapshot.Take(b, brushPre.FaceIds)) != Kind.Placement) return false;
                    }
                    continue;
                }

                if (!Contains(touched, m)) return false;

                LinkGeometry.Snapshot pre;
                lock (snaps)
                {
                    if (!snaps.TryGetValue(m, out pre)) return false;
                }

                if (m is Solid solid)
                {
                    if (Classify(pre, LinkGeometry.Snapshot.Take(solid, pre.FaceIds)) != Kind.Placement) return false;
                }
                else
                {
                    var now = LinkGeometry.Snapshot.TakeAny(m);
                    var sameForm = pre.Reference != null && LinkedObjects.Equivalent(pre.Reference, now.Reference);
                    if (!sameForm || (pre.Center - now.Center).Length() <= 0.0001f) return false;
                }
            }
            return true;
        }

        private static Kind Classify(LinkGeometry.Snapshot pre, LinkGeometry.Snapshot now)
        {
            if (!pre.SameTopology(now)) return Kind.Shape;
            if (!pre.SamePoints(now)) return LinkGeometry.Rigid.Fit(pre, now) != null ? Kind.Placement : Kind.Shape;
            if (!pre.SameTextures(now)) return Kind.Texture;
            return Kind.None;
        }

        private static void SyncSolids(Change change, List<IMapObject> members, IMapObject originObject, List<IMapObject> touched, LinkGeometry.SnapshotStore snaps, bool groupMove)
        {
            var origin = (Solid) originObject;
            var solids = members.OfType<Solid>().ToList();

            // What was done to each touched solid since we last looked
            var edits = new Dictionary<Solid, Kind>();
            var nows = new Dictionary<Solid, LinkGeometry.Snapshot>();
            foreach (var s in solids.Where(x => Contains(touched, x)))
            {
                LinkGeometry.Snapshot pre;
                lock (snaps) pre = snaps[s];

                var now = LinkGeometry.Snapshot.Take(s, pre.FaceIds);
                var kind = Classify(pre, now);
                if (kind == Kind.None) continue;

                nows[s] = now;
                edits[s] = kind;

                // Objects outside the origin instance are edited locally: nothing is passed on
                if (!ReferenceEquals(s, origin)) lock (snaps) snaps[s] = now;
            }

            if (!edits.TryGetValue(origin, out var originKind)) return;

            // The whole group was placed somewhere else: the other instances stay where they are
            if (originKind == Kind.Placement && groupMove)
            {
                lock (snaps) snaps[origin] = nows[origin];
                return;
            }

            LinkGeometry.Snapshot originPre;
            lock (snaps) originPre = snaps[origin];
            var originNow = nows[origin];

            // Anything that was edited in this same change (eg a whole selection moved at once) already is where it should be
            var targets = solids.Where(x => !ReferenceEquals(x, origin) && !edits.ContainsKey(x)).ToList();

            // Take each target's frame from the old snapshots before anything is overwritten
            var plan = new List<KeyValuePair<Solid, LinkGeometry.Rigid>>();
            var targetPres = new Dictionary<Solid, LinkGeometry.Snapshot>();
            LinkGeometry.Rigid delta = null;
            lock (snaps)
            {
                if (originKind == Kind.Placement) delta = LinkGeometry.Rigid.Fit(originPre, originNow);

                foreach (var target in targets)
                {
                    // The target wasn't edited in this change, so how it is right now is how it was before.
                    // Its faces are listed in the order they were last matched with the origin's, whatever order they're stored in now.
                    // If that order has been lost (eg the map was saved and reopened), the faces are matched by where they are.
                    snaps.TryGetValue(target, out var last);
                    var targetPre = LinkGeometry.AlignTo(originPre, target, last, out var rigid);

                    // Not the same shape (the target was edited locally): line up their centres
                    if (rigid == null) rigid = LinkGeometry.Rigid.Translation(targetPre.Center - originPre.Center);

                    targetPres[target] = targetPre;
                    plan.Add(new KeyValuePair<Solid, LinkGeometry.Rigid>(target, rigid));
                }
            }

            var newOrders = new Dictionary<Solid, IList<long>>();
            foreach (var kv in plan)
            {
                var target = kv.Key;
                var targetPre = targetPres[target];
                newOrders[target] = targetPre.FaceIds;

                switch (originKind)
                {
                    case Kind.Placement:
                        if (delta == null) break;
                        LinkGeometry.ApplyPlacement(change.Document, target, targetPre, delta, originNow.Centroid - originPre.Centroid, change);
                        break;

                    case Kind.Shape:
                        newOrders[target] = LinkGeometry.Apply(change.Document, origin, originNow, target, targetPre, kv.Value, change);
                        break;

                    case Kind.Texture:
                        LinkGeometry.ApplyTextures(origin, originNow, target, targetPre, kv.Value, change);
                        break;
                }

                // Keep the target's faces stored in the origin's order, so they stay matched after saving and reopening
                if (originKind == Kind.Texture || originKind == Kind.Shape)
                {
                    LinkGeometry.MirrorFaceOrder(origin, originNow.FaceIds, target, newOrders[target]);
                }
            }

            lock (snaps)
            {
                snaps[origin] = LinkGeometry.Snapshot.Take(origin, originNow.FaceIds);
                foreach (var kv in plan) snaps[kv.Key] = LinkGeometry.Snapshot.Take(kv.Key, newOrders[kv.Key]);
            }
        }

        private static void SyncOthers(Change change, List<IMapObject> members, IMapObject origin, List<IMapObject> touched, LinkGeometry.SnapshotStore snaps, bool groupMove)
        {
            // Objects outside the origin instance are edited locally: nothing is passed on
            foreach (var m in members.Where(x => !ReferenceEquals(x, origin) && Contains(touched, x)))
            {
                lock (snaps) snaps[m] = LinkGeometry.Snapshot.TakeAny(m);
            }

            if (!Contains(touched, origin)) return;

            LinkGeometry.Snapshot pre;
            lock (snaps) pre = snaps[origin];
            var now = LinkGeometry.Snapshot.TakeAny(origin);

            var sameForm = pre.Reference != null && LinkedObjects.Equivalent(pre.Reference, now.Reference);
            var moved = (pre.Center - now.Center).Length() > 0.0001f;
            var targets = members.Where(x => !ReferenceEquals(x, origin) && x.GetType() == origin.GetType()).ToList();

            if (sameForm && !moved) return; // eg it was just selected

            // The whole group was placed somewhere else: the other instances stay where they are
            if (sameForm && groupMove)
            {
                lock (snaps) snaps[origin] = now;
                return;
            }

            if (sameForm)
            {
                // Only moved: the others move the same amount
                var offset = now.Center - pre.Center;
                var matrix = Matrix4x4.CreateTranslation(offset);
                var textureLock = (change.Document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags()).TextureLock;

                foreach (var target in targets.Where(x => !Contains(touched, x)))
                {
                    // The brushes inside a linked entity have slots of their own and follow the origin's brushes themselves
                    var ownBrushes = target.FindAll().Any(x => !ReferenceEquals(x, target) && LinkedObjects.GetLinkId(x) != null);
                    if (ownBrushes)
                    {
                        foreach (var d in target.Data.OfType<ITransformable>()) d.Transform(matrix);
                    }
                    else
                    {
                        target.Transform(matrix);
                        if (textureLock)
                        {
                            foreach (var o in target.FindAll())
                            {
                                foreach (var t in o.Data.OfType<ITextured>()) t.Texture?.TransformUniform(matrix);
                            }
                        }
                    }
                    target.DescendantsChanged();
                    change.Update(target);
                    lock (snaps) snaps[target] = LinkGeometry.Snapshot.TakeAny(target);
                }
            }
            else
            {
                // Its properties were edited: the others take them, each keeping its own position and angles.
                // (An entity's brushes are left alone: replacing them is what leaves ghost brushes behind.)
                foreach (var target in targets.Where(x => !Contains(touched, x)))
                {
                    LinkedObjects.SyncProperties(origin, target, change);
                    lock (snaps) snaps[target] = LinkGeometry.Snapshot.TakeAny(target);
                }
            }

            lock (snaps) snaps[origin] = LinkGeometry.Snapshot.TakeAny(origin);
        }
    }
}
