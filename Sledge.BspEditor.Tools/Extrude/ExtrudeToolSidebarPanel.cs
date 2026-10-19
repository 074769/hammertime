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
    /// Sidebar panel for the extrude tool: which step it is in (red select / blue extrude, the tool's hotkey switches), "Extrude by [1] units",
    /// and a line that says what is picked or what the drag is doing. The amount is what a drag snaps to, and what Enter extrudes
    /// the picked faces by. Only shown while the extrude tool is active.
    /// Sizes itself to its content and follows the sidebar's width, the same way the bevel panel does.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    public class ExtrudeToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private ExtrudeTool _tool;

        public string Title { get; set; } = "Extrude";
        public object Control => this;

        private readonly Label _mode;
        private readonly NumericUpDown _amount;
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
                ColumnCount = 3,
                Padding = new Padding(6)
            };

            // label | number (takes the spare width) | units
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // The current step, in the same colour the picked faces have in the 3D view
            _mode = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 3, 3, 3) };
            var hotkey = new Label { Text = "Press this tool's hotkey again to switch between selecting faces (red) and extruding them (blue). Set it in Settings > Hotkeys (default Shift+F).", AutoSize = true, MaximumSize = new Size(210, 0), Margin = new Padding(3, 0, 3, 6) };

            var byLabel = new Label { Text = "Extrude by", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            _amount = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 1,
                Minimum = 0.25m,
                Maximum = 1000,
                Value = 1,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                MinimumSize = new Size(40, 0),
                Margin = new Padding(3, 6, 3, 6)
            };
            var unitsLabel = new Label { Text = "units", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            _info = new Label { AutoSize = true, MaximumSize = new Size(210, 0), Padding = new Padding(0, 4, 0, 4), Margin = new Padding(3) };

            layout.Controls.Add(_mode, 0, 0);
            layout.SetColumnSpan(_mode, 3);
            layout.Controls.Add(hotkey, 0, 1);
            layout.SetColumnSpan(hotkey, 3);

            layout.Controls.Add(byLabel, 0, 2);
            layout.Controls.Add(_amount, 1, 2);
            layout.Controls.Add(unitsLabel, 2, 2);
            layout.Controls.Add(_info, 0, 3);
            layout.SetColumnSpan(_info, 3);

            Controls.Add(layout);

            var tips = new ToolTip();
            tips.SetToolTip(_amount, "Dragging a face snaps to multiples of this. Enter extrudes the picked faces by exactly this much.");

            // The info text wraps to the sidebar's width
            Resize += (s, e) =>
            {
                _info.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 28), 0);
                hotkey.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 28), 0);
            };

            _amount.ValueChanged += (s, e) => { if (_tool != null) _tool.Amount = (float) _amount.Value; };

            // The info line follows the picked faces and the drag
            _timer = new System.Windows.Forms.Timer { Interval = 100 };
            _timer.Tick += (s, e) =>
            {
                if (!Visible || _tool == null) return;

                var extrude = _tool.IsExtrudeMode;
                var mode = extrude ? "EXTRUDE" : "SELECT FACES";
                if (_mode.Text != mode)
                {
                    _mode.Text = mode;
                    _mode.ForeColor = extrude ? Color.DodgerBlue : Color.Red;
                }

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
