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
    /// Link the selected objects together as a group. The selection becomes the first instance of the link, and the origin.
    /// Copies of the group (see "Add to link") are further instances: editing an object in one instance edits the
    /// matching object in every other instance, and no other object in its own instance.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkObjects")]
    [MenuItem("Tools", "", "Link", "A")]
    public class LinkObjects : BaseCommand
    {
        public override string Name { get; set; } = "Link selected objects";
        public override string Details { get; set; } = "Link the selected objects together as a group, ready to be copied as linked instances.";

        public string Title { get; set; } = "Link objects";
        public string GroupNameLabel { get; set; } = "Link name";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var targets = document.Selection.GetSelectedParents().OrderBy(x => x.ID).ToList();
            if (targets.Count == 0) return;

            var linkId = LinkedObjects.NextGroupId(document);
            var name = "Link " + linkId;

            using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.TextBox("Name", GroupNameLabel, name).OkCancel(OK, Cancel))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                var entered = qf.String("Name")?.Trim();
                if (!string.IsNullOrEmpty(entered)) name = entered;
            }

            // Names show up in the visgroup list, so keep them distinguishable
            if (document.Map.Data.Get<LinkGroup>().Any(x => x.ParentID == 0 && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{name} ({linkId})";
            }

            var colour = LinkedObjects.ColourFor(linkId);
            var link = new LinkGroup { ID = linkId, Name = name, Colour = colour, OriginInstance = 1 };

            var ops = new List<IOperation> { new AddMapData(link) };

            // Each object gets a slot of its own, so the objects of this instance stay independent of each other
            var next = linkId;
            foreach (var t in targets)
            {
                var slotId = ++next;
                ops.Add(new AddMapData(new LinkGroup { ID = slotId, Name = name + " / " + slotId, Colour = colour, ParentID = linkId }));

                // An object can only be in one link, so this takes it out of the one it was in
                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(slotId, linkId, 1)));

                // The brushes inside an entity (or group) are linked too, each with a slot of its own, so they can be edited individually
                foreach (var child in LinkedObjects.ChildSolids(t))
                {
                    var childSlot = ++next;
                    ops.Add(new AddMapData(new LinkGroup { ID = childSlot, Name = name + " / " + childSlot, Colour = colour, ParentID = linkId }));

                    var childExisting = child.Data.GetOne<LinkGroupID>();
                    if (childExisting != null) ops.Add(new RemoveMapObjectData(child.ID, childExisting));
                    ops.Add(new AddMapObjectData(child.ID, new LinkGroupID(childSlot, linkId, 1)));
                }
            }

            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }
}
