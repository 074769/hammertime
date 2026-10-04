using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
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
    /// they just stop following (and stop affecting) the rest of the group.
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
            foreach (var t in document.Selection.GetSelectedParents().ToList())
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
