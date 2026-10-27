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
			// The Point tool owns the vertex selection and handles this
			await Oy.Publish("VertexTool:ScaleDialog");
		}

		public bool IsInContext(IContext context)
		{
			return context.TryGet("ActiveTool", out VertexTool _);
		}
	}
}
