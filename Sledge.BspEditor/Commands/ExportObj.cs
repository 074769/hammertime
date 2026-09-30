using System;
using System.Collections.Generic;
using System.Drawing;
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
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Commands
{
    /// <summary>
    /// File > Export as OBJ...
    /// Clicking the entry exports straight away (asks for a file name) using the remembered options.
    /// The handle on the right of the entry opens a popup to choose the options.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:File:ExportObj")]
    [MenuItem("File", "", "File", "M")]
    public class ExportObj : BaseCommand, IMenuItemOptions, ISettingsContainer
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

            if (_selectedOnly && !hasSelection)
            {
                MessageBox.Show("\"Export selected object(s) only\" is turned on but nothing is selected.\nSelect something, or turn the option off with the handle on the right of the menu entry.", "Export as OBJ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var options = new ObjExportOptions
            {
                SelectedOnly = _selectedOnly,
                ZeroOrigin = _zeroOrigin,
                Axis = _axis
            };

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
        /// Shows the options popup next to the menu entry. Toggling an option keeps the popup open
        /// (radio behaviour for the axis choices); Esc or clicking away closes it.
        /// The options are remembered and used the next time the entry is clicked.
        /// </summary>
        public void ShowOptions(IContext context, Point screenLocation)
        {
            var menu = new ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };

            var selectedOnly = new ToolStripMenuItem("Export selected object(s) only")
            {
                CheckOnClick = true,
                Checked = _selectedOnly
            };
            selectedOnly.CheckedChanged += (s, e) => _selectedOnly = selectedOnly.Checked;

            var zeroOrigin = new ToolStripMenuItem("Zero out origin (centre on 0,0,0)")
            {
                CheckOnClick = true,
                Checked = _zeroOrigin
            };
            zeroOrigin.CheckedChanged += (s, e) => _zeroOrigin = zeroOrigin.Checked;

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
                    _axis = preset;
                    foreach (var other in axisItems) other.Value.Checked = other.Key == preset;
                };
            }

            menu.Items.Add(selectedOnly);
            menu.Items.Add(zeroOrigin);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(axisHeader);
            foreach (var item in axisItems.Values) menu.Items.Add(item);

            menu.Closing += (s, e) =>
            {
                // Keep the popup open while the user toggles options
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                {
                    e.Cancel = true;
                }
            };

            menu.Show(screenLocation);
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
