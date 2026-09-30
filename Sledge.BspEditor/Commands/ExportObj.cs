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
using Sledge.Common.Shell.Menu;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Commands
{
    /// <summary>
    /// File > Export as OBJ...
    /// Clicking it pops up a small menu of export options; "Export..." then asks for a file name.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:File:ExportObj")]
    [MenuItem("File", "", "File", "M")]
    public class ExportObj : BaseCommand, ISettingsContainer
    {
        public override string Name { get; set; } = "Export as OBJ...";
        public override string Details { get; set; } = "Export the map (or the selection) to a Wavefront OBJ file";
        string ISettingsContainer.Name => "Sledge.BspEditor.Commands.ExportObj";

        public bool ValuesLoaded { get; private set; }

        // Remembered between uses
        private bool _selectedOnly;
        private bool _zeroOrigin;
        private ObjAxisPreset _axis = ObjAxisPreset.Source;

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var hasSelection = document.Selection.GetSelectedParents().Any();

            var options = await ShowOptionsMenu(hasSelection);
            if (options == null) return;

            _selectedOnly = options.SelectedOnly;
            _zeroOrigin = options.ZeroOrigin;
            _axis = options.Axis;

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

            string filename;
            using (var sfd = new SaveFileDialog
            {
                Filter = "Wavefront OBJ (*.obj)|*.obj",
                DefaultExt = "obj",
                AddExtension = true,
                FileName = GetDefaultFileName(document)
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                filename = sfd.FileName;
            }

            var sizes = await ObjExporter.GetTextureSizes(document, solids);

            await Task.Run(() =>
            {
                using (var writer = new StreamWriter(filename, false, new UTF8Encoding(false)))
                {
                    ObjExporter.Write(solids, options, writer, sizes);
                }
            });
        }

        private static string GetDefaultFileName(MapDocument document)
        {
            try
            {
                var name = Path.GetFileNameWithoutExtension(document.FileName);
                return string.IsNullOrWhiteSpace(name) ? "export.obj" : name + ".obj";
            }
            catch
            {
                return "export.obj";
            }
        }

        /// <summary>
        /// Shows the options popup at the mouse cursor. Toggling an option keeps the popup open;
        /// clicking "Export..." completes the task with the chosen options. Anything else that closes
        /// the popup (Esc, clicking away) completes it with null.
        /// </summary>
        private Task<ObjExportOptions> ShowOptionsMenu(bool hasSelection)
        {
            var tcs = new TaskCompletionSource<ObjExportOptions>();
            var exportClicked = false;

            var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };

            var selectedOnly = new ToolStripMenuItem("Export selected object(s) only")
            {
                CheckOnClick = true,
                Enabled = hasSelection,
                Checked = hasSelection && _selectedOnly
            };
            var zeroOrigin = new ToolStripMenuItem("Zero out origin (centre on 0,0,0)")
            {
                CheckOnClick = true,
                Checked = _zeroOrigin
            };

            var axisHeader = new ToolStripLabel("Axis") { Enabled = false };
            var axisItems = new Dictionary<ObjAxisPreset, ToolStripMenuItem>
            {
                { ObjAxisPreset.Source, new ToolStripMenuItem("Source (Z-up, as-is)") },
                { ObjAxisPreset.Blender, new ToolStripMenuItem("Blender (Y-up, -Z forward)") },
                { ObjAxisPreset.Max, new ToolStripMenuItem("3ds Max (Z-up)") }
            };
            foreach (var kv in axisItems)
            {
                var preset = kv.Key;
                kv.Value.Checked = preset == _axis;
                kv.Value.Click += (s, e) =>
                {
                    // Radio behaviour
                    foreach (var other in axisItems) other.Value.Checked = other.Key == preset;
                };
            }

            var export = new ToolStripMenuItem("Export...") { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) };
            export.Click += (s, e) => exportClicked = true;

            menu.Items.Add(selectedOnly);
            menu.Items.Add(zeroOrigin);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(axisHeader);
            foreach (var item in axisItems.Values) menu.Items.Add(item);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(export);

            menu.Closing += (s, e) =>
            {
                // Keep the popup open while the user toggles options
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && !exportClicked)
                {
                    e.Cancel = true;
                }
            };

            menu.Closed += (s, e) =>
            {
                if (!exportClicked)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                tcs.TrySetResult(new ObjExportOptions
                {
                    SelectedOnly = selectedOnly.Enabled && selectedOnly.Checked,
                    ZeroOrigin = zeroOrigin.Checked,
                    Axis = axisItems.First(x => x.Value.Checked).Key
                });
            };

            menu.Show(Control.MousePosition);

            return tcs.Task;
        }

        #region Settings

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            _selectedOnly = store.Get("selectedOnly", false);
            _zeroOrigin = store.Get("zeroOrigin", false);
            _axis = Enum.TryParse(store.Get<string>("axis"), out ObjAxisPreset axis) ? axis : ObjAxisPreset.Source;
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("selectedOnly", _selectedOnly);
            store.Set("zeroOrigin", _zeroOrigin);
            store.Set("axis", _axis.ToString());
        }

        #endregion
    }
}
