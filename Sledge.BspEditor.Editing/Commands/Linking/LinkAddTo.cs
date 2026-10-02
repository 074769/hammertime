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
    /// Add the selected objects to an existing link group.
    /// The right-click menu passes the group in the "GroupId" parameter; run from the menu bar, it asks which group to use.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkAddTo")]
    [MenuItem("Tools", "", "Link", "B")]
    public class LinkAddTo : BaseCommand
    {
        public override string Name { get; set; } = "Add to link";
        public override string Details { get; set; } = "Add the selected objects to an existing link group.";

        public string Title { get; set; } = "Add to link";
        public string GroupLabel { get; set; } = "Link group";
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

            var groups = document.Map.Data.Get<LinkGroup>().OrderBy(x => x.Name).ToList();
            var groupId = parameters.Get<long>("GroupId", 0);

            if (groupId == 0)
            {
                var inUse = LinkedObjects.GetMembers(document).Keys.ToList();
                var choices = groups.Where(x => inUse.Contains(x.ID)).Select(x => new GroupChoice { Group = x }).ToList();
                if (choices.Count == 0) return;

                using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.ComboBox("Group", GroupLabel, choices).OkCancel(OK, Cancel))
                {
                    if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                    var choice = qf.Object("Group") as GroupChoice;
                    if (choice == null) return;
                    groupId = choice.Group.ID;
                }
            }

            if (groups.All(x => x.ID != groupId)) return;

            var ops = new List<IOperation>();
            foreach (var t in targets)
            {
                var existing = t.Data.GetOne<LinkGroupID>();
                if (existing != null && existing.ID == groupId) continue;
                if (existing != null) ops.Add(new RemoveMapObjectData(t.ID, existing));
                ops.Add(new AddMapObjectData(t.ID, new LinkGroupID(groupId)));
            }

            if (ops.Count == 0) return;
            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }
}
