using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.QuickForms;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Rename the link of the selected object(s).
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkRename")]
    [MenuItem("Tools", "", "Link", "D")]
    public class LinkRename : BaseCommand
    {
        public override string Name { get; set; } = "Rename link";
        public override string Details { get; set; } = "Rename the link of the selected objects.";

        public string Title { get; set; } = "Rename link";
        public string GroupNameLabel { get; set; } = "Link name";
        public string OK { get; set; } = "OK";
        public string Cancel { get; set; } = "Cancel";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && document.Selection.GetSelectedParents().Any(x => LinkedObjects.GetTopId(x) != 0);
        }

        private class GroupChoice
        {
            public LinkGroup Group { get; set; }
            public override string ToString() => Group.Name;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var ids = document.Selection.GetSelectedParents()
                .Select(LinkedObjects.GetTopId)
                .Where(x => x != 0)
                .Distinct()
                .ToList();
            var groups = document.Map.Data.Get<LinkGroup>().Where(x => ids.Contains(x.ID)).ToList();
            if (groups.Count == 0) return;

            LinkGroup group;
            string name;

            if (groups.Count == 1)
            {
                group = groups[0];
                using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }.TextBox("Name", GroupNameLabel, group.Name).OkCancel(OK, Cancel))
                {
                    if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                    name = qf.String("Name")?.Trim();
                }
            }
            else
            {
                // The selection touches several groups, so ask which one to rename
                using (var qf = new QuickForm(Title) { UseShortcutKeys = true, Width = 360 }
                    .ComboBox("Group", "", groups.Select(x => new GroupChoice { Group = x }))
                    .TextBox("Name", GroupNameLabel, groups[0].Name)
                    .OkCancel(OK, Cancel))
                {
                    if (await qf.ShowDialogAsync() != DialogResult.OK) return;
                    group = (qf.Object("Group") as GroupChoice)?.Group;
                    name = qf.String("Name")?.Trim();
                }
            }

            if (group == null || string.IsNullOrEmpty(name) || name == group.Name) return;
            if (document.Map.Data.Get<LinkGroup>().Any(x => x.ParentID == 0 && x.ID != group.ID && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{name} ({group.ID})";
            }

            await MapDocumentOperation.Perform(document, new RenameLinkGroup(group.ID, name));
        }

        private class RenameLinkGroup : IOperation
        {
            private readonly long _id;
            private readonly string _name;
            private string _oldName;

            public bool Trivial => false;

            public RenameLinkGroup(long id, string name)
            {
                _id = id;
                _name = name;
            }

            public Task<Change> Perform(MapDocument document)
            {
                var ch = new Change(document);
                var group = document.Map.Data.Get<LinkGroup>().FirstOrDefault(x => x.ID == _id);
                if (group != null)
                {
                    _oldName = group.Name;
                    group.Name = _name;
                    ch.Update(group);
                }
                return Task.FromResult(ch);
            }

            public Task<Change> Reverse(MapDocument document)
            {
                var ch = new Change(document);
                var group = document.Map.Data.Get<LinkGroup>().FirstOrDefault(x => x.ID == _id);
                if (group != null)
                {
                    group.Name = _oldName;
                    ch.Update(group);
                }
                return Task.FromResult(ch);
            }
        }
    }
}
