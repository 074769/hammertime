using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    // Counter-clockwise counterpart to RotateSelectionClockwise. No default hotkey is
    // assigned - the user can bind one in the hotkey settings if they want one.
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:RotateCounterClockwise")]
    public class RotateSelectionCounterClockwise : BaseCommand
    {
        public override string Name { get; set; } = "Rotate Counter-Clockwise";
        public override string Details { get; set; } = "Rotate the selection counter-clockwise around the focused 2D viewport's axis";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            await Oy.Publish<string>("BspEditor:Viewport:RotateSelection", "CCW");
        }
    }
}
