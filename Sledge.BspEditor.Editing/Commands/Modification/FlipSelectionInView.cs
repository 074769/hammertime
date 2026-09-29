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
    // Flips the current selection horizontally / vertically as seen in whichever 2D viewport is
    // focused - the same mapping as the viewport's right-click Flip menu. The axis is resolved
    // per-viewport in MapViewport, which forwards to the existing BspEditor:Tools:FlipX/Y/Z commands.
    // If no 2D viewport is focused (e.g. the 3D viewport is), nothing happens, since "horizontal"
    // and "vertical" aren't defined there. The fixed-axis FlipX/FlipY/FlipZ commands are unchanged.
    public abstract class FlipSelectionInView : BaseCommand
    {
        protected abstract string Direction { get; }

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            await Oy.Publish<string>("BspEditor:Viewport:FlipSelection", Direction);
        }
    }

    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:FlipHorizontal")]
    [DefaultHotkey("Ctrl+L")]
    public class FlipSelectionHorizontal : FlipSelectionInView
    {
        public override string Name { get; set; } = "Flip Horizontally";
        public override string Details { get; set; } = "Flip the selection horizontally in the focused 2D viewport";
        protected override string Direction => "Horizontal";
    }

    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:FlipVertical")]
    [DefaultHotkey("Ctrl+I")]
    public class FlipSelectionVertical : FlipSelectionInView
    {
        public override string Name { get; set; } = "Flip Vertically";
        public override string Details { get; set; } = "Flip the selection vertically in the focused 2D viewport";
        protected override string Direction => "Vertical";
    }
}
