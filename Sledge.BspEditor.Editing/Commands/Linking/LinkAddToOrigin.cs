using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.QuickForms;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Add the selected objects to the origin instance of a link. The link is updated with them: every other
    /// instance gets its own linked copy of each added object, placed the way the nearest existing object of the group is.
    /// The right-click menu passes the link in the "GroupId" parameter; run from the menu bar, it asks which link to use.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkAddToOrigin")]
    [MenuItem("Tools", "", "Link", "C")]
    public class LinkAddToOrigin : BaseCommand
    {
        public override string Name { get; set; } = "Add to link origin";
        public override string Details { get; set; } = "Add the selected objects to the origin of a link, and to every other instance of it.";

        public string Title { get; set; } = "Add to link origin";
        public string GroupLabel { get; set; } = "Link";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && !document.Selection.IsEmpty
                   && document.Map.Data.Get<LinkedObjectsVisgroup>().Any();
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var linkId = await LinkAddTo.ChooseLink(document, parameters, Title, GroupLabel, OK, Cancel);
            if (linkId == 0) return;

            var index = LinkedObjects.BuildIndex(document);
            if (!index.Links.TryGetValue(linkId, out var instances) || instances.Count == 0) return;
            index.Groups.TryGetValue(linkId, out var link);

            var originInstance = index.OriginInstance(linkId);
            var otherInstances = instances.Keys.Where(x => x != originInstance).ToList();

            var targets = document.Selection.GetSelectedParents().Where(x => LinkedObjects.GetTopId(x) != linkId).ToList();
            if (targets.Count == 0) return;

            // The existing objects of the origin instance, to work out where the other instances put an added object
            var originMembers = instances[originInstance];

            var ops = new List<IOperation>();
            var next = LinkedObjects.NextGroupId(document);

            foreach (var t in targets)
            {
                var slotId = next++;
                ops.Add(new AddMapData(new LinkGroup { ID = slotId, Name = (link?.Name ?? "Link") + " / " + slotId, Colour = LinkedObjects.ColourFor(linkId), ParentID = linkId }));

                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(slotId, linkId, originInstance)));

                // The brushes inside it get slots of their own
                var childSlots = new Dictionary<Solid, long>();
                foreach (var child in LinkedObjects.ChildSolids(t))
                {
                    var childSlot = next++;
                    childSlots[child] = childSlot;
                    ops.Add(new AddMapData(new LinkGroup { ID = childSlot, Name = (link?.Name ?? "Link") + " / " + childSlot, Colour = LinkedObjects.ColourFor(linkId), ParentID = linkId }));

                    var childExisting = child.Data.GetOne<LinkGroupID>();
                    if (childExisting != null) ops.Add(new RemoveMapObjectData(child.ID, childExisting));
                    ops.Add(new AddMapObjectData(child.ID, new LinkGroupID(childSlot, linkId, originInstance)));
                }

                // The closest object of the origin instance that every other instance also has: the added object sits relative to it
                var reference = originMembers
                    .OrderBy(x => (x.BoundingBox.Center - t.BoundingBox.Center).LengthSquared())
                    .FirstOrDefault();

                foreach (var instance in otherInstances)
                {
                    var counterpart = reference == null ? null : instances[instance]
                        .FirstOrDefault(x => LinkedObjects.GetLinkId(x) == LinkedObjects.GetLinkId(reference));

                    // How the other instance's object sits compared with the origin's: a rotation or flip if they're shaped alike, else just an offset
                    // The instance as a whole is the origin turned / flipped and moved, so that is worked out from all of its brushes,
                    // not from one object: an entity or a group has no shape of its own to compare, and would only give an offset.
                    var matrix = Matrix4x4.Identity;
                    var mirrored = false;
                    var rigid = reference == null ? null : InstanceTransform(index, linkId, originInstance, instance, t);
                    if (rigid == null && reference != null && counterpart != null)
                    {
                        if (reference is Solid rs && counterpart is Solid cs)
                        {
                            rigid = LinkGeometry.Rigid.Fit(LinkGeometry.Snapshot.Take(rs), LinkGeometry.Snapshot.Take(cs));
                        }
                        if (rigid == null)
                        {
                            rigid = LinkGeometry.Rigid.Translation(counterpart.BoundingBox.Center - reference.BoundingBox.Center);
                        }
                    }
                    if (rigid != null)
                    {
                        matrix = rigid.ToMatrix();
                        mirrored = rigid.Mirrored;
                    }

                    var copy = (IMapObject) t.Copy(document.Map.NumberGenerator);
                    foreach (var o in copy.FindAll()) o.Data.Remove(x => x is LinkGroupID);

                    // Which brush of the copy is which brush of the original (they're still the same size and place)
                    var copyChildren = new Dictionary<Solid, long>();
                    var unmatched = LinkedObjects.ChildSolids(t).ToList();
                    foreach (var cc in LinkedObjects.ChildSolids(copy))
                    {
                        var original = unmatched.FirstOrDefault(x => x.BoundingBox.Start == cc.BoundingBox.Start && x.BoundingBox.End == cc.BoundingBox.End);
                        if (original == null) continue;
                        unmatched.Remove(original);
                        copyChildren[cc] = childSlots[original];
                    }

                    LinkedObjects.TransformObject(copy, matrix, mirrored);
                    copy.Data.Add(new LinkGroupID(slotId, linkId, instance));
                    foreach (var kv in copyChildren) kv.Key.Data.Add(new LinkGroupID(kv.Value, linkId, instance));

                    var parentId = t.Hierarchy.Parent?.ID ?? document.Map.Root.ID;
                    ops.Add(new Attach(parentId, copy));
                }
            }

            await MapDocumentOperation.Perform(document, new Transaction(ops));
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
        /// that suits the most brushes is the one used.
        /// </summary>
        private static LinkGeometry.Rigid InstanceTransform(LinkedObjects.LinkIndex index, long linkId, long originInstance, long instance, IMapObject near)
        {
            var pairs = new List<(Solid origin, Solid other)>();
            foreach (var members in index.Slots.Values)
            {
                var o = members.FirstOrDefault(x => LinkedObjects.GetTopId(x) == linkId && LinkedObjects.GetInstance(x) == originInstance) as Solid;
                var c = members.FirstOrDefault(x => LinkedObjects.GetTopId(x) == linkId && LinkedObjects.GetInstance(x) == instance) as Solid;
                if (o != null && c != null) pairs.Add((o, c));
            }
            if (pairs.Count == 0) return null;

            var centre = near.BoundingBox.Center;
            pairs = pairs.OrderBy(p => (p.origin.BoundingBox.Center - centre).LengthSquared()).ToList();

            var originPoints = pairs.Select(p => PointsOf(p.origin)).ToList();
            var otherPoints = pairs.Select(p => PointsOf(p.other)).ToList();

            // Candidates: what the nearest brushes fit exactly (face for face), then every other way they could be placed on each other
            var candidates = new List<LinkGeometry.Rigid>();
            for (var i = 0; i < Math.Min(6, pairs.Count); i++)
            {
                var exact = LinkGeometry.Rigid.Fit(LinkGeometry.Snapshot.Take(pairs[i].origin), LinkGeometry.Snapshot.Take(pairs[i].other));
                if (exact != null) candidates.Add(exact);
            }
            for (var i = 0; i < Math.Min(6, pairs.Count); i++)
            {
                candidates.AddRange(LinkGeometry.Rigid.FitSets(originPoints[i], otherPoints[i]));
            }

            LinkGeometry.Rigid best = null;
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
    }
}
