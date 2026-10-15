using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Grid;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Components;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Remembers the grid size of a saved map whenever the grid changes.
    /// </summary>
    [Export(typeof(IMapDocumentChangeHandler))]
    [OrderHint("Z")]
    public class MapGridSettingsHandler : IMapDocumentChangeHandler
    {
        public string OrderHint => "Z";

        public async Task Changed(Change change)
        {
            if (!change.HasDataChanges || !change.AffectedData.Any(x => x is GridData)) return;

            var path = change.Document.FileName;
            if (string.IsNullOrWhiteSpace(path)) return;

            var gd = change.Document.Map.Data.GetOne<GridData>();
            if (MapGridSettingsManager.GetInstance()?.Set(path, gd) == true)
            {
                await Oy.Publish("Settings:Save");
            }
        }
    }
}
