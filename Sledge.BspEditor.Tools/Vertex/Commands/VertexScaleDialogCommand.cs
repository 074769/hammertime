using LogicAndTrick.Oy;
using Sledge.BspEditor.Tools.Vertex.Tools;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Translations;
using System.ComponentModel.Composition;
using System.Threading.Tasks;

namespace Sledge.BspEditor.Tools.Vertex.Commands
{
	[AutoTranslate]
	[Export(typeof(ICommand))]
	[CommandID("BspEditor:VertexScale")]
	[DefaultHotkey("Alt+E")]
	internal class VertexScaleDialogCommand : ICommand
	{
		public string Name => "Scale vertices";

		public string Details => "Open the per-axis scale dialog for the selected vertices";

		public async Task Invoke(IContext context, CommandParameters parameters)
		{
			if (!context.TryGet("ActiveTool", out VertexTool tool)) return;

			var scale = tool.GetSubTool(typeof(VertexScaleTool)) as VertexScaleTool;
			if (scale == null) return;

			// Activate the scale subtool, open its dialog directly (the dialog call blocks until it closes),
			// then go back to whichever subtool was active before.
			var previous = tool.CurrentSubTool;
			tool.CurrentSubTool = scale;
			scale.OpenScaleDialog();
			if (previous != null && previous != scale) tool.CurrentSubTool = previous;
		}

		public bool IsInContext(IContext context)
		{
			return context.TryGet("ActiveTool", out VertexTool _);
		}
	}
}
