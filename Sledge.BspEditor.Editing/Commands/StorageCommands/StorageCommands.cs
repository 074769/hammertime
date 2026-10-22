using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Commands.Clipboard;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Storage;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.QuickForms;

namespace Sledge.BspEditor.Editing.Commands.StorageCommands
{
    /// <summary>
    /// Shared helpers for the storage commands.
    /// </summary>
    internal static class StorageChoice
    {
        /// <summary>
        /// The group to move objects into: the only group, or the one the user picks.
        /// If the map has no storage yet, a group is created first.
        /// </summary>
        public static async Task<StorageGroup> Choose(MapDocument document, string title)
        {
            var groups = StorageRules.Groups(document).OrderBy(x => x.ID).ToList();

            if (groups.Count == 0)
            {
                var created = new StorageGroup { ID = document.Map.NumberGenerator.Next("StorageGroup"), Name = "Storage" };
                await MapDocumentOperation.Perform(document, new AddMapData(created));
                return created;
            }
            if (groups.Count == 1) return groups[0];

            using (var qf = new QuickForm(title) { UseShortcutKeys = true, Width = 360 }.ComboBox("Group", "Storage group", groups).OkCancel("OK", "Cancel"))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return null;
                return qf.Object("Group") as StorageGroup;
            }
        }

        public static void Warn(string text)
        {
            MessageBox.Show(text, "Storage", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Moves the selected world objects into a storage group.
    /// </summary>
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Storage:MoveToStorage")]
    [MenuItem("Tools", "", "Storage", "S")]
    public class MoveSelectedToStorage : BaseCommand
    {
        public override string Name { get; set; } = "Move to storage";
        public override string Details { get; set; } = "Move the selected objects into a storage group. Storage is kept with the map but never saved into the map file.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && document.Selection.GetSelectedParents().Any(x => !StorageRules.IsStored(x));
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var selected = document.Selection.GetSelectedParents().Where(x => !StorageRules.IsStored(x)).ToList();
            if (selected.Count == 0) return;

            if (selected.Any(StorageRules.IsLinked))
            {
                StorageChoice.Warn("Linked objects can't be moved into storage. Remove them from their link first.");
                return;
            }

            var group = await StorageChoice.Choose(document, "Move to storage");
            if (group == null) return;

            var ops = new List<IOperation> { new MoveToStorage(group.ID, selected) };

            // Like deleting, a group left empty when its objects leave the map goes too.
            // Objects that stay in the map (visible group) don't empty anything.
            if (!group.Visible) ops.AddRange(GroupCleanup.DetatchEmptiedGroups(selected));

            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }

    /// <summary>
    /// Takes the selected storage objects out of their group and leaves them in the map.
    /// </summary>
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Storage:MoveOutOfStorage")]
    [MenuItem("Tools", "", "Storage", "S")]
    public class MoveSelectedOutOfStorage : BaseCommand
    {
        public override string Name { get; set; } = "Move out of storage";
        public override string Details { get; set; } = "Keep the selected storage objects in the map, removing them from their storage group.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && document.Selection.GetSelectedParents().Any(StorageRules.IsStored);
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var stored = document.Selection.GetSelectedParents().Where(StorageRules.IsStored).ToList();
            if (stored.Count == 0) return;

            await MapDocumentOperation.Perform(document, new MoveOutOfStorage(stored));
        }
    }

    /// <summary>
    /// Adds the storage commands to the viewport right-click menu.
    /// </summary>
    public static class StorageContextMenu
    {
        public static void Populate(RightClickMenuBuilder builder, MapDocument document)
        {
            if (document == null || document.Selection.IsEmpty) return;

            var selected = document.Selection.GetSelectedParents().ToList();
            var added = false;

            if (selected.Any(x => !StorageRules.IsStored(x)))
            {
                builder.AddCommand("BspEditor:Storage:MoveToStorage");
                added = true;
            }
            if (selected.Any(StorageRules.IsStored))
            {
                builder.AddCommand("BspEditor:Storage:MoveOutOfStorage");
                added = true;
            }

            if (added) builder.AddSeparator();
        }
    }
}
