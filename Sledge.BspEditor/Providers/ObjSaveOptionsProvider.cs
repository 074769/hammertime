using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Documents;

namespace Sledge.BspEditor.Providers
{
    /// <summary>
    /// Shows the OBJ export options inside the Save As / Export dialog.
    /// This is the template for adding options to any other exporter:
    /// export an ISaveOptionsProvider that handles the extension and returns a panel.
    /// </summary>
    [Export(typeof(ISaveOptionsProvider))]
    public class ObjSaveOptionsProvider : ISaveOptionsProvider
    {
        public bool Handles(IDocument document, string extension)
        {
            return document is MapDocument && String.Equals(extension, ".obj", StringComparison.OrdinalIgnoreCase);
        }

        public ISaveOptionsPanel CreatePanel(IDocument document)
        {
            var hasSelection = document is MapDocument map && !map.Selection.IsEmpty;
            return new ObjSaveOptionsPanel(hasSelection);
        }
    }

    public class ObjSaveOptionsPanel : UserControl, ISaveOptionsPanel
    {
        private readonly CheckBox _selectedOnly;
        private readonly ComboBox _axis;
        private readonly CheckBox _zeroOrigin;

        object ISaveOptionsPanel.Control => this;

        public ObjSaveOptionsPanel(bool hasSelection)
        {
            var last = ObjExportOptionsForm.Last;

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top
            };

            _selectedOnly = new CheckBox
            {
                Text = hasSelection ? "Export selected objects only" : "Export selected objects only (nothing is selected)",
                AutoSize = true,
                Enabled = hasSelection,
                Checked = hasSelection && last.SelectedOnly,
                Margin = new Padding(3, 3, 3, 8)
            };

            var axisLabel = new Label { Text = "Axis conversion:", AutoSize = true, Margin = new Padding(3, 3, 3, 2) };
            _axis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(3, 0, 3, 8) };
            _axis.Items.Add("Source (Z-up, as-is)");
            _axis.Items.Add("Blender (Y-up, -Z forward)");
            _axis.Items.Add("3ds Max (Z-up)");
            _axis.SelectedIndex = (int) last.Axis;

            _zeroOrigin = new CheckBox
            {
                Text = "Zero out the origin (centre the geometry on 0, 0, 0)",
                AutoSize = true,
                Checked = last.ZeroOrigin,
                Margin = new Padding(3, 3, 3, 3)
            };

            layout.Controls.Add(_selectedOnly);
            layout.Controls.Add(axisLabel);
            layout.Controls.Add(_axis);
            layout.Controls.Add(_zeroOrigin);
            Controls.Add(layout);
        }

        public object GetOptions()
        {
            var options = new ObjExportOptions
            {
                SelectedOnly = _selectedOnly.Enabled && _selectedOnly.Checked,
                Axis = (ObjAxisPreset) Math.Max(0, _axis.SelectedIndex),
                ZeroOrigin = _zeroOrigin.Checked
            };

            // Remember for next time (shared with the old options popup)
            var last = ObjExportOptionsForm.Last;
            last.SelectedOnly = options.SelectedOnly;
            last.Axis = options.Axis;
            last.ZeroOrigin = options.ZeroOrigin;

            return options;
        }
    }
}
