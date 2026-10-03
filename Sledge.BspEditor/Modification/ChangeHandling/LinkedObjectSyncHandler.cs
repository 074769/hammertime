using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Keeps linked objects in sync.
    ///
    /// A link is a group of objects, and the copies of that group are its instances. Each object in an instance has a
    /// matching object in every other instance (together they are a "slot"). Objects in the same instance are never
    /// tied to each other: each one is edited individually, and the edit is shared with its slot only.
    ///
    /// Solids: moving, rotating or flipping a linked solid only affects that solid. Editing its shape (vertices,
    /// clipping, scaling...) changes the shape of the whole slot, each solid keeping its own position and orientation.
    /// See <see cref="LinkGeometry"/> for how the two are told apart.
    ///
    /// Everything else (entities and so on): edits to properties and children are shared, position and angles are not.
    ///
    /// The other members are always derived from the edited one, so undo and redo need no special handling:
    /// reversing the edit raises another change, which is synced the same way.
    ///
    /// Runs before the visgroup handlers so that anything it changes is processed by them.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    public class LinkedObjectSyncHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "J";

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

            foreach (var added in addedLinked.OfType<Solid>())
            {
                lock (snaps) snaps[added.ID] = LinkGeometry.Snapshot.Take(added);
            }

            foreach (var slotId in updatedLinked.Select(x => LinkedObjects.GetLinkId(x).Value).Distinct())
            {
                if (!index.Slots.TryGetValue(slotId, out var members)) continue;

                var solids = members.OfType<Solid>().ToList();
                if (solids.Count > 0) SyncSolids(change, index, slotId, solids, updatedLinked, snaps);

                var others = members.Where(x => !(x is Solid)).ToList();
                if (others.Count > 1) SyncOthers(change, index, slotId, others, updatedLinked);
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
                    instance = System.Math.Max(existing, already) + 1;
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

        private static IMapObject PickSource(LinkedObjects.LinkIndex index, long slotId, List<IMapObject> edited)
        {
            // If several were edited at once the origin object wins
            var origin = index.OriginOf(slotId);
            return edited.FirstOrDefault(x => ReferenceEquals(x, origin)) ?? edited[0];
        }

        private static void SyncSolids(Change change, LinkedObjects.LinkIndex index, long slotId, List<Solid> solids, List<IMapObject> touched, Dictionary<long, LinkGeometry.Snapshot> snaps)
        {
            // A solid we know nothing about yet starts from how it is now
            lock (snaps)
            {
                foreach (var s in solids)
                {
                    if (!snaps.ContainsKey(s.ID)) snaps[s.ID] = LinkGeometry.Snapshot.Take(s);
                }
            }

            // Work out which of the touched solids had their shape edited
            var edited = new List<IMapObject>();
            foreach (var s in solids.Where(x => Contains(touched, x)))
            {
                LinkGeometry.Snapshot pre;
                lock (snaps) pre = snaps[s.ID];

                var now = LinkGeometry.Snapshot.Take(s);
                if (pre.SamePoints(now)) continue; // eg it was just selected

                if (LinkGeometry.Rigid.Fit(pre, now) != null)
                {
                    // Moved, rotated or flipped: it's only a different placement, so nothing is shared
                    lock (snaps) snaps[s.ID] = now;
                    continue;
                }

                edited.Add(s);
            }

            if (edited.Count == 0) return;

            var source = (Solid) PickSource(index, slotId, edited);

            LinkGeometry.Snapshot sourcePre;
            lock (snaps) sourcePre = snaps[source.ID];

            // Take every target's frame from the old snapshots before anything is overwritten
            var plan = new List<KeyValuePair<Solid, LinkGeometry.Rigid>>();
            lock (snaps)
            {
                foreach (var target in solids)
                {
                    if (ReferenceEquals(target, source)) continue;

                    var targetPre = snaps[target.ID];
                    var rigid = LinkGeometry.Rigid.Fit(sourcePre, targetPre);

                    // Not the same shape yet (they were linked while different): line up their centres
                    if (rigid == null) rigid = LinkGeometry.Rigid.Translation(targetPre.Center - sourcePre.Center);

                    plan.Add(new KeyValuePair<Solid, LinkGeometry.Rigid>(target, rigid));
                }
            }

            foreach (var kv in plan)
            {
                LinkGeometry.Apply(change.Document, source, kv.Key, kv.Value, change);
            }

            lock (snaps)
            {
                snaps[source.ID] = LinkGeometry.Snapshot.Take(source);
                foreach (var kv in plan) snaps[kv.Key.ID] = LinkGeometry.Snapshot.Take(kv.Key);
            }
        }

        private static void SyncOthers(Change change, LinkedObjects.LinkIndex index, long slotId, List<IMapObject> all, List<IMapObject> touched)
        {
            var updated = all.Where(x => Contains(touched, x)).ToList();
            var untouched = all.Where(x => !Contains(updated, x)).ToList();

            // If every member was changed at once (eg moving a selection) there's nothing to copy from
            if (untouched.Count == 0) return;

            // Selecting an object also counts as an update, so look for a member that
            // actually differs from the members that weren't touched
            var baseline = untouched[0];
            var edited = updated.Where(x => x.GetType() == baseline.GetType() && !LinkedObjects.AreEquivalent(x, baseline)).ToList();
            if (edited.Count == 0) return;

            var source = PickSource(index, slotId, edited);

            foreach (var target in all)
            {
                if (ReferenceEquals(target, source)) continue;
                if (target.GetType() != source.GetType()) continue;
                if (LinkedObjects.AreEquivalent(source, target)) continue;

                LinkedObjects.Sync(change.Document, source, target, change);
            }
        }
    }
}
