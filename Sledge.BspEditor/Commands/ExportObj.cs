using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Providers;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Shell.Settings;
using Sledge.Shell.Components;

namespace Sledge.BspEditor.Commands
{
    /// <summary>
    /// File > Export as OBJ...
    /// Opens the Save As dialog with only the OBJ format; the OBJ options are shown inside it.
    /// The options are remembered between sessions.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:File:ExportObj")]
    [MenuItem("File", "", "File", "M")]
    public class ExportObj : BaseCommand, ISettingsContainer
    {
        private static readonly FileExtensionInfo[] ObjFormat = { new FileExtensionInfo("Wavefront OBJ", ".obj") };

        private readonly Lazy<SaveAsService> _saveAs;

        public override string Name { get; set; } = "Export as OBJ...";
        public override string Details { get; set; } = "Export the map (or the selection) to a Wavefront OBJ file";
        string ISettingsContainer.Name => "Sledge.BspEditor.Commands.ExportObj";

        public bool ValuesLoaded { get; private set; }

        [ImportingConstructor]
        public ExportObj([Import] Lazy<SaveAsService> saveAs)
        {
            _saveAs = saveAs;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var result = _saveAs.Value.Prompt(document, ObjFormat, "Export as OBJ", "Export");
            if (result == null) return;

            var options = result.Options as ObjExportOptions ?? new ObjExportOptions();

            IEnumerable<IMapObject> roots = options.SelectedOnly
                ? document.Selection.GetSelectedParents().ToList()
                : new[] { document.Map.Root };

            var solids = roots
                .SelectMany(x => x.Find(o => o is Solid))
                .OfType<Solid>()
                .Distinct()
                .ToList();

            if (solids.Count == 0)
            {
                MessageBox.Show("There are no solids to export.", "Export as OBJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var filename = result.FileName;
            var sizes = await ObjExporter.GetTextureSizes(document, solids);

            await Task.Run(() =>
            {
                using (var writer = new StreamWriter(filename, false, new UTF8Encoding(false)))
                {
                    ObjExporter.Write(solids, options, writer, sizes);
                }
            });
        }

        #region Settings

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            // The dialog's OBJ panel reads its starting values from here
            var last = ObjExportOptionsForm.Last;
            last.SelectedOnly = store.Get("selectedOnly", false);
            last.ZeroOrigin = store.Get("zeroOrigin", false);
            last.Axis = Enum.TryParse(store.Get<string>("axis"), out ObjAxisPreset axis) ? axis : ObjAxisPreset.Source;
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            var last = ObjExportOptionsForm.Last;
            store.Set("selectedOnly", last.SelectedOnly);
            store.Set("zeroOrigin", last.ZeroOrigin);
            store.Set("axis", last.Axis.ToString());
        }

        #endregion
    }
}
