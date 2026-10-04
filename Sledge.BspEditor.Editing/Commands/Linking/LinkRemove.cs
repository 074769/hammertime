using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Remove the selected objects from their link groups. The objects keep their current shape,
    /// they just stop following (and stop affecting) the rest of the group. Taking an object out of the origin instance
    /// also removes its copies from the other instances, as they would no longer be copies of anything.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkRemove")]
    [MenuItem("Tools", "", "Link", "C")]
    public class LinkRemove : BaseCommand
    {
        public override string Name { get; set; } = "Remove from link";
        public override string Details { get; set; } = "Remove the selected objects from their link group.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document)
                   && document.Selection.GetSelectedParents().Any(x => x.Data.GetOne<LinkGroupID>() != null);
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var ops = new List<IOperation>();
            var selected = document.Selection.GetSelectedParents().ToList();

            // An object of the origin instance that leaves the link leaves nothing for the other instances to be copies of,
            // so its copies there go (the same as when it is deleted). Worked out before anything changes.
            var orphaned = LinkedObjects.GetCounterpartsToDelete(document, selected);
            foreach (var g in orphaned.GroupBy(x => x.Hierarchy.Parent.ID))
            {
                ops.Add(new Detatch(g.Key, g));
            }

            foreach (var t in selected)
            {
                // The brushes inside a linked entity are removed along with it
                foreach (var o in t.FindAll())
                {
                    var existing = o.Data.GetOne<LinkGroupID>();
                    if (existing != null) ops.Add(new RemoveMapObjectData(o.ID, existing));
                }
            }

            if (ops.Count == 0) return;
            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }
    }
}
