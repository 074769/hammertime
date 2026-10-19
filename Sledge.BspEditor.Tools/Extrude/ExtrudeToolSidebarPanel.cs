using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Windows.Forms;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Extrude
{
    /// <summary>
    /// Sidebar panel for the extrude tool: "Extrude by [n] units [Extrude]", All faces / Clear buttons and a line that says
    /// what is selected. Only shown while the extrude tool is the active tool. Sizes itself to its content and follows the
    /// sidebar's width, the same way the bevel panel does.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [OrderHint("G")]
    public class ExtrudeToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private ExtrudeTool _tool;

        public string Title { get; set; } = "Extrude";
        public object Control => this;

        private readonly NumericUpDown _distance;
        private readonly Button _extrude;
        private readonly Button _all;
        private readonly Button _clear;
        private readonly Label _info;
        private readonly System.Windows.Forms.Timer _timer;

        public ExtrudeToolSidebarPanel()
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Padding = new Padding(6)
            };

            // label | number (takes the spare width) | units | button
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var i = 0; i < 3; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var byLabel = new Label { Text = "Extrude by", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            _distance = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 1000,
                Value = 16,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                MinimumSize = new Size(40, 0),
                Margin = new Padding(3, 6, 3, 6)
            };
            var unitsLabel = new Label { Text = "units", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            _extrude = new Button { Text = "Extrude", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, MinimumSize = new Size(58, 27), Anchor = AnchorStyles.Right, Margin = new Padding(3) };

            // Two buttons that share the width equally, so neither can be squeezed out of view
            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _all = new Button { Text = "All faces", Dock = DockStyle.Fill, AutoSize = false, Height = 26, Margin = new Padding(3) };
            _clear = new Button { Text = "Clear", Dock = DockStyle.Fill, AutoSize = false, Height = 26, Margin = new Padding(3) };
            buttons.Controls.Add(_all, 0, 0);
            buttons.Controls.Add(_clear, 1, 0);
            _info = new Label { AutoSize = true, MaximumSize = new Size(210, 0), Padding = new Padding(0, 4, 0, 4), Margin = new Padding(3) };

            layout.Controls.Add(byLabel, 0, 0);
            layout.Controls.Add(_distance, 1, 0);
            layout.Controls.Add(unitsLabel, 2, 0);
            layout.Controls.Add(_extrude, 3, 0);

            layout.Controls.Add(buttons, 0, 1);
            layout.SetColumnSpan(buttons, 4);

            layout.Controls.Add(_info, 0, 2);
            layout.SetColumnSpan(_info, 4);

            Controls.Add(layout);

            var tips = new ToolTip();
            tips.SetToolTip(_all, "Pick every face of the selected solids.");
            tips.SetToolTip(_clear, "Deselect all faces (Esc).");
            tips.SetToolTip(_extrude, "Extrude the picked faces (Enter).");

            // The info text wraps to the sidebar's width
            Resize += (s, e) => _info.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 28), 0);

            _distance.ValueChanged += (s, e) => { if (_tool != null) _tool.Distance = (float) _distance.Value; };
            _all.Click += (s, e) => _tool?.SelectAllFaces();
            _extrude.Click += async (s, e) => { if (_tool != null) await _tool.Apply(); };
            _clear.Click += (s, e) => _tool?.ClearSelectedFaces();

            // The info line follows the selection, the faces and the settings
            _timer = new System.Windows.Forms.Timer { Interval = 250 };
            _timer.Tick += (s, e) =>
            {
                if (!Visible || _tool == null) return;
                var text = _tool.GetInfoText();
                if (_info.Text != text) _info.Text = text;
            };
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out ExtrudeTool _);
        }
    }
}
