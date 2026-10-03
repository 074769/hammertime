using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Keeps linked objects in sync.
    ///
    /// Solids: moving or rotating a linked solid only moves that solid. Editing its shape (vertices, clipping,
    /// scaling...) changes the shape of every solid in the link, each keeping its own position and orientation.
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

            // New linked solids (paste, duplicate, linking) start from how they are now
            foreach (var added in change.Added.OfType<Solid>().Where(x => LinkedObjects.GetLinkId(x) != null))
            {
                lock (snaps) snaps[added.ID] = LinkGeometry.Snapshot.Take(added);
            }
            foreach (var removed in change.Removed)
            {
                lock (snaps) snaps.Remove(removed.ID);
            }

            if (!change.Updated.Any()) return Task.CompletedTask;

            // The linked objects touched by this change (an edit to a linked entity's brush counts as an edit to the entity)
            var owners = change.Updated
                .Select(LinkedObjects.FindLinkedOwner)
                .Where(x => x != null)
                .Distinct()
                .ToList();
            if (owners.Count == 0) return Task.CompletedTask;

            var touched = owners.Select(x => LinkedObjects.GetLinkId(x).Value).Distinct().ToList();
            var members = LinkedObjects.GetMembers(doc);
            var groups = doc.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());

            foreach (var groupId in touched)
            {
                if (!members.TryGetValue(groupId, out var all)) continue;
                groups.TryGetValue(groupId, out var group);

                var solids = all.OfType<Solid>().ToList();
                if (solids.Count > 0) SyncSolids(change, group, solids, owners, snaps);

                var others = all.Where(x => !(x is Solid)).ToList();
                if (others.Count > 1) SyncOthers(change, group, others, owners);
            }

            return Task.CompletedTask;
        }

        private static bool Contains(IEnumerable<IMapObject> list, IMapObject o)
        {
            return list.Any(x => ReferenceEquals(x, o));
        }

        private static void SyncSolids(Change change, LinkGroup group, List<Solid> solids, List<IMapObject> owners, Dictionary<long, LinkGeometry.Snapshot> snaps)
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
            var edited = new List<Solid>();
            foreach (var s in solids.Where(x => Contains(owners, x)))
            {
                LinkGeometry.Snapshot pre;
                lock (snaps) pre = snaps[s.ID];

                var now = LinkGeometry.Snapshot.Take(s);
                if (pre.SamePoints(now)) continue; // eg it was just selected

                if (pre.SameTopology(now) && LinkGeometry.Rigid.Fit(pre.Points, now.Points) != null)
                {
                    // Moved or rotated: it's only a different placement, so nothing is shared
                    lock (snaps) snaps[s.ID] = now;
                    continue;
                }

                edited.Add(s);
            }

            if (edited.Count == 0) return;

            // If several were edited at once the origin object wins
            var origin = LinkedObjects.GetOrigin(group, edited);
            var source = (Solid) origin;

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
                    var rigid = sourcePre.SameTopology(targetPre) ? LinkGeometry.Rigid.Fit(sourcePre.Points, targetPre.Points) : null;

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

        private static void SyncOthers(Change change, LinkGroup group, List<IMapObject> all, List<IMapObject> owners)
        {
            var updated = all.Where(x => Contains(owners, x)).ToList();
            var untouched = all.Where(x => !Contains(updated, x)).ToList();

            // If every member was changed at once (eg moving a selection) there's nothing to copy from
            if (untouched.Count == 0) return;

            // Selecting an object also counts as an update, so look for a member that
            // actually differs from the members that weren't touched
            var baseline = untouched[0];
            var edited = updated.Where(x => x.GetType() == baseline.GetType() && !LinkedObjects.AreEquivalent(x, baseline)).ToList();
            if (edited.Count == 0) return;

            var source = LinkedObjects.GetOrigin(group, edited);

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
