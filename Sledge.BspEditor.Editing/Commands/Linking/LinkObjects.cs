using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.QuickForms;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Link the selected objects together in a new link group. Once linked, editing the shape of one
    /// of the objects edits all of them (each keeps its own position and rotation).
    /// The first object becomes the origin object of the link.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkObjects")]
    [MenuItem("Tools", "", "Link", "A")]
    public class LinkObjects : BaseCommand
    {
        public override string Name { get; set; } = "Link selected objects";
        public override string Details { get; set; } = "Link the selected objects together. Editing one of them will edit all of them.";

        public string Title { get; set; } = "Link objects";
        public string GroupNameLabel { get; set; } = "Link group name";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var targets = document.Selection.GetSelectedParents().ToList();
            if (targets.Count == 0) return;

            var id = LinkedObjects.NextGroupId(document);
            var name = "Link " + id;

            using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.TextBox("Name", GroupNameLabel, name).OkCancel(OK, Cancel))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                var entered = qf.String("Name")?.Trim();
                if (!string.IsNullOrEmpty(entered)) name = entered;
            }

            // Names show up in the visgroup list, so keep them distinguishable
            if (document.Map.Data.Get<LinkGroup>().Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{name} ({id})";
            }

            // The first object (the one made earliest) is the origin object of the link
            var group = new LinkGroup
            {
                ID = id,
                Name = name,
                Colour = LinkedObjects.ColourFor(id),
                OriginID = targets.OrderBy(x => x.ID).First().ID
            };

            var ops = new List<IOperation> { new AddMapData(group) };
            foreach (var t in targets)
            {
                // An object can only be in one group, so moving it here takes it out of any other group
                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(id)));
            }

            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }
}
