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
    /// Add the selected objects to a link as a new instance of it.
    /// Each selected object is matched to the slot whose objects it is a copy of (same shape, similar place in the group).
    /// Selected objects that match no slot become new slots, present in this instance only.
    /// The right-click menu passes the link in the "GroupId" parameter; run from the menu bar, it asks which link to use.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkAddTo")]
    [MenuItem("Tools", "", "Link", "B")]
    public class LinkAddTo : BaseCommand
    {
        public override string Name { get; set; } = "Add to link as instance";
        public override string Details { get; set; } = "Add the selected objects to a link as a new instance (copy) of its group.";

        public string Title { get; set; } = "Add to link";
        public string GroupLabel { get; set; } = "Link";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && !document.Selection.IsEmpty
                   && document.Map.Data.Get<LinkedObjectsVisgroup>().Any();
        }

        private class GroupChoice
        {
            public LinkGroup Group { get; set; }
            public override string ToString() => Group.Name;
        }

        /// <summary>
        /// Ask which link to use, unless the menu already said.
        /// </summary>
        public static async Task<long> ChooseLink(MapDocument document, CommandParameters parameters, string title, string label, string ok, string cancel)
        {
            var linkId = parameters.Get<long>("GroupId", 0);
            if (linkId != 0) return linkId;

            var index = LinkedObjects.BuildIndex(document);
            var choices = index.Links.Keys
                .Where(x => index.Groups.ContainsKey(x))
                .Select(x => new GroupChoice { Group = index.Groups[x] })
                .OrderBy(x => x.Group.Name)
                .ToList();
            if (choices.Count == 0) return 0;

            using (var qf = new QuickForm(title) { UseShortcutKeys = true, Width = 360 }.ComboBox("Group", label, choices).OkCancel(ok, cancel))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return 0;
                return (qf.Object("Group") as GroupChoice)?.Group.ID ?? 0;
            }
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var linkId = await ChooseLink(document, parameters, Title, GroupLabel, OK, Cancel);
            if (linkId == 0) return;

            var index = LinkedObjects.BuildIndex(document);
            if (!index.Links.TryGetValue(linkId, out var instances) || instances.Count == 0) return;
            index.Groups.TryGetValue(linkId, out var link);

            // Objects already in this link can't be added to it again
            var targets = document.Selection.GetSelectedParents().Where(x => LinkedObjects.GetTopId(x) != linkId).ToList();
            if (targets.Count == 0) return;

            var newInstance = instances.Keys.Max() + 1;
            var originInstance = index.OriginInstance(linkId);

            // The slots, represented by their object in the origin instance
            var slots = instances[originInstance]
                .Select(x => LinkedObjects.GetLinkId(x).Value)
                .Distinct()
                .Select(x => new { Id = x, Origin = index.OriginOf(x) })
                .ToList();

            var originCentre = Centre(instances[originInstance]);
            var selectionCentre = Centre(targets);

            // Match objects to slots: the same shape, and the closest to the same place relative to the rest of its group
            var pairs = new List<Tuple<float, IMapObject, long>>();
            foreach (var t in targets)
            {
                foreach (var s in slots)
                {
                    if (s.Origin == null || !LinkedObjects.Congruent(t, s.Origin)) continue;
                    var distance = ((t.BoundingBox.Center - selectionCentre) - (s.Origin.BoundingBox.Center - originCentre)).Length();
                    pairs.Add(Tuple.Create(distance, t, s.Id));
                }
            }

            var assigned = new Dictionary<IMapObject, long>();
            var usedSlots = new HashSet<long>();
            foreach (var p in pairs.OrderBy(x => x.Item1))
            {
                if (assigned.ContainsKey(p.Item2) || usedSlots.Contains(p.Item3)) continue;
                assigned[p.Item2] = p.Item3;
                usedSlots.Add(p.Item3);
            }

            var ops = new List<IOperation>();
            var next = LinkedObjects.NextGroupId(document);
            foreach (var t in targets)
            {
                if (!assigned.TryGetValue(t, out var slotId))
                {
                    // Nothing like it in the group: it gets a slot of its own
                    slotId = next++;
                    ops.Add(new AddMapData(new LinkGroup { ID = slotId, Name = (link?.Name ?? "Link") + " / " + slotId, Colour = LinkedObjects.ColourFor(linkId), ParentID = linkId }));
                }

                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(slotId, linkId, newInstance)));
            }

            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }

        private static Vector3 Centre(IEnumerable<IMapObject> objects)
        {
            var boxes = objects.Select(x => x.BoundingBox).ToList();
            var min = boxes.Select(x => x.Start).Aggregate(Vector3.Min);
            var max = boxes.Select(x => x.End).Aggregate(Vector3.Max);
            return (min + max) / 2;
        }
    }
}
