using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
namespace Sledge.BspEditor.Editing.Commands.Quick
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("View", "", "Quick", "D")]
    [CommandID("BspEditor:View:QuickHideUnselected")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_HideUnselected))]
    [DefaultHotkey("Ctrl+H")]
    public class HideUnselectedObjects : BaseCommand
    {
        public override string Name { get; set; } = "Quick hide unselected";
        public override string Details { get; set; } = "Quick hide unselected objects";
        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            // Single-pass collection: skip the root, the selection itself, ancestors of the
            // selection (hiding a parent would hide the selected child too), and anything
            // that is already hidden. One bulk operation keeps this O(N) instead of O(N^2).
            var selection = document.Selection.ToHashSet();
            var excluded = new HashSet<IMapObject>();
            foreach (var sel in document.Selection) excluded.UnionWith(sel.Hierarchy.GetParentList());
            var ids = new List<long>();
            foreach (var mo in document.Map.Root.FindAll())
            {
                if (mo is Root) continue;
                if (selection.Contains(mo)) continue;
                if (excluded.Contains(mo)) continue;
                if (mo.Data.GetOne<QuickHidden>() != null) continue;
                ids.Add(mo.ID);
            }
            if (ids.Count == 0) return;
            await MapDocumentOperation.Perform(document, new SetQuickHidden(ids, null));
        }
    }
}
