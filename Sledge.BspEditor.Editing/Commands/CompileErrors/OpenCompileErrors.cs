using System.ComponentModel.Composition;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Properties;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;

namespace Sledge.BspEditor.Editing.Commands.CompileErrors
{
    [Export(typeof(ICommand))]
    [MenuItem("File", "", "Compile log", "L")]
    [MenuImage(typeof(Resources), "Menu_CompileLog")]
    [CommandID("BspEditor:File:CompileLog")]
    public class OpenCompileErrors : BaseCommand
    {
        public override string Name { get; set; } = "Compile log";
        public override string Details { get; set; } = "Show the errors and warnings from the last compile";

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            await Oy.Publish("Context:Add", new ContextInfo("BspEditor:CompileErrors"));
        }
    }
}
