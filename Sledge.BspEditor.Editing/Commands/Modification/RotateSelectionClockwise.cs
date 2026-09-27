using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    // Rotates the current selection by a fixed step, clockwise, around whichever axis
    // the currently focused 2D viewport is looking down (Top -> Z, Front -> X, Side -> Y).
    // The actual rotation direction/axis is resolved per-viewport in MapViewport, which
    // forwards to the existing "BspEditor:Tools:Rotate" command with the resolved Axis/Angle.
    // If no 2D viewport is focused (e.g. the 3D viewport is focused, or none is), nothing happens,
    // since there is no single well-defined rotation axis in that case.
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:RotateClockwise")]
    [DefaultHotkey("Ctrl+R")]
    public class RotateSelectionClockwise : BaseCommand
    {
        public override string Name { get; set; } = "Rotate Clockwise";
        public override string Details { get; set; } = "Rotate the selection clockwise around the focused 2D viewport's axis";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            await Oy.Publish<string>("BspEditor:Viewport:RotateSelection", "CW");
        }
    }
}
