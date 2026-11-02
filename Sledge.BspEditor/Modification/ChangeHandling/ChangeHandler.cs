using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Logging;
using Sledge.Common.Shell.Hooks;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    [Export(typeof(IInitialiseHook))]
    public class ChangeHandler : IInitialiseHook
    {
        private readonly IMapDocumentChangeHandler[] _changeHandlers;

        [ImportingConstructor]
        public ChangeHandler(
            [ImportMany] IMapDocumentChangeHandler[] changeHandlers
        )
        {
            // The order never changes, so it is worked out once instead of for every change
            _changeHandlers = changeHandlers.OrderBy(x => x.OrderHint).ToArray();
        }

        public Task OnInitialise()
        {
            Oy.Subscribe<Change>("MapDocument:Changed:Early", Changed);
            Oy.Subscribe<MapDocument>("Document:Opened", Opened);
            return Task.CompletedTask;
        }

        private Task Opened(MapDocument doc)
        {
            var ch = new Change(doc);
            ch.AddRange(doc.Map.Root.FindAll());
            foreach (var d in doc.Map.Data) ch.Update(d);
            return Changed(ch);
        }

        private async Task Changed(Change change)
        {
            foreach (var ch in _changeHandlers)
            {
                using (PerfLog.Measure("Handler", ch)) await ch.Changed(change);
            }
        }
    }
}