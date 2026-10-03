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
    /// Link the selected objects together as a sublink of an existing link.
    /// A sublink is a link group of its own, listed under its parent in the visgroups. Its objects share
    /// their shape with each other but are not affected by edits to the parent link, and don't affect it.
    /// The right-click menu passes the parent in the "GroupId" parameter; run from the menu bar, it asks which link to use.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkAddSublink")]
    [MenuItem("Tools", "", "Link", "F")]
    public class LinkAddSublink : BaseCommand
    {
        public override string Name { get; set; } = "Add as sublink";
        public override string Details { get; set; } = "Link the selected objects as a sublink of an existing link. They stay independent of the parent link.";

        public string Title { get; set; } = "Add as sublink";
        public string GroupLabel { get; set; } = "Parent link";
        public string GroupNameLabel { get; set; } = "Sublink name";
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

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var targets = document.Selection.GetSelectedParents().ToList();
            if (targets.Count == 0) return;

            var groups = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());
            var parentId = parameters.Get<long>("GroupId", 0);

            if (parentId == 0)
            {
                var inUse = LinkedObjects.GetKeptGroupIds(document);
                var choices = groups.Values.Where(x => inUse.Contains(x.ID)).OrderBy(x => x.Name).Select(x => new GroupChoice { Group = x }).ToList();
                if (choices.Count == 0) return;

                using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.ComboBox("Group", GroupLabel, choices).OkCancel(OK, Cancel))
                {
                    if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                    var choice = qf.Object("Group") as GroupChoice;
                    if (choice == null) return;
                    parentId = choice.Group.ID;
                }
            }

            if (!groups.TryGetValue(parentId, out var parent)) return;

            var id = LinkedObjects.NextGroupId(document);
            var name = parent.Name + "." + (groups.Values.Count(x => x.ParentID == parentId) + 1);

            using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.TextBox("Name", GroupNameLabel, name).OkCancel(OK, Cancel))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                var entered = qf.String("Name")?.Trim();
                if (!string.IsNullOrEmpty(entered)) name = entered;
            }

            if (groups.Values.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{name} ({id})";
            }

            var group = new LinkGroup
            {
                ID = id,
                Name = name,
                Colour = LinkedObjects.ColourFor(id),
                ParentID = parentId,
                OriginID = targets.OrderBy(x => x.ID).First().ID
            };

            var ops = new List<IOperation> { new AddMapData(group) };
            foreach (var t in targets)
            {
                // An object can only be in one link, so this takes it out of the one it was in
                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(id)));
            }

            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }
}
