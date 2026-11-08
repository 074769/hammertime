using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;

namespace Sledge.BspEditor.Tools.Extrude
{
    [Export(typeof(IHotkeyProvider))]
    public sealed class ExtrudeHotkeyProvider : IHotkeyProvider
    {
        public string OrderHint => "Z";

        public IEnumerable<IHotkey> GetHotkeys()
        {
            yield return new ExtrudeFillHotkey();
        }
    }

    /// <summary>Fill (default F, changeable in Settings > Hotkeys): with the Extrude tool active and two faces picked, makes the brush between them.</summary>
    internal sealed class ExtrudeFillHotkey : IHotkey
    {
        public string ID => "Extrude:Fill";
        public string Name => "Extrude Tool: Fill Between Faces";
        public string Description => "With two faces picked in the Extrude tool, creates a brush that fills the space between them";
        public string DefaultHotkey => "F";

        public bool IsInContext(IContext context)
        {
            return context?.Get<ITool>("ActiveTool") is ExtrudeTool;
        }

        public Task Invoke()
        {
            var tool = Sledge.Common.Container.Get<IContext>()?.Get<ITool>("ActiveTool") as ExtrudeTool;
            return tool?.Fill() ?? Task.CompletedTask;
        }
    }
}
