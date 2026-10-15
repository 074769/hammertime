using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Spin
{
    /// <summary>
    /// Sidebar panel for the spin tool: set the start and end piece, then spin. Also the number of pieces,
    /// the angle for a manual spin, and whether UVs are mirrored.
    /// Shows what the spin will produce, or why it can't.
    /// Only shown while the spin tool is the active tool.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    public class SpinToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private SpinTool _tool;

        public string Title { get; set; } = "Spin";
        public object Control => this;

        private readonly Button _setStart;
        private readonly Button _setEnd;
        private readonly Button _clear;
        private readonly CheckBox _auto;
        private readonly NumericUpDown _steps;
        private readonly NumericUpDown _angle;
        private readonly CheckBox _mirror;
        private readonly CheckBox _unfold;
        private readonly CheckBox _drag;
        private readonly Label _info;
        private readonly Button _spin;
        private readonly System.Windows.Forms.Timer _timer;

        public SpinToolSidebarPanel()
        {
            // The sidebar host docks this control at the top and uses the control's own height, so the
            // control has to size itself to its content (the default height is only 150 pixels)
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(6)
            };

            // Two equal columns that follow the sidebar's width, whatever the text says
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            _setStart = new Button { Text = "Set start", Dock = DockStyle.Fill, AutoSize = false, Height = 26 };
            _setEnd = new Button { Text = "Set end", Dock = DockStyle.Fill, AutoSize = false, Height = 26 };
            _clear = new Button { Text = "Clear start/end", Dock = DockStyle.Fill, AutoSize = false, Height = 26 };
            _auto = new CheckBox { Text = "Auto steps (piece thickness)", Checked = true, AutoSize = true };
            _steps = new NumericUpDown { Minimum = 1, Maximum = SpinTool.MaxSteps, Value = 8, Width = 60 };
            _angle = new NumericUpDown { Minimum = -359, Maximum = 359, DecimalPlaces = 1, Increment = 5, Value = 90, Width = 60 };
            _mirror = new CheckBox { Text = "Flip every second piece's UVs", Checked = true, AutoSize = true };
            _unfold = new CheckBox { Text = "Unfold UVs of each piece", Checked = false, AutoSize = true };
            _drag = new CheckBox { Text = "Drag to rotate in 2D (manual)", Checked = false, AutoSize = true };
            _info = new Label { AutoSize = true, MaximumSize = new Size(210, 0), Padding = new Padding(0, 4, 0, 4) };
            _spin = new Button { Text = "Spin", Dock = DockStyle.Fill, AutoSize = false, Height = 30 };

            for (var i = 0; i < 10; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            layout.Controls.Add(_setStart, 0, 0);
            layout.Controls.Add(_setEnd, 1, 0);
            layout.Controls.Add(_clear, 0, 1);
            layout.SetColumnSpan(_clear, 2);
            layout.Controls.Add(_auto, 0, 2);
            layout.SetColumnSpan(_auto, 2);
            layout.Controls.Add(new Label { Text = "Steps:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            layout.Controls.Add(_steps, 1, 3);
            layout.Controls.Add(new Label { Text = "Angle (manual):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
            layout.Controls.Add(_angle, 1, 4);
            layout.Controls.Add(_mirror, 0, 5);
            layout.SetColumnSpan(_mirror, 2);
            layout.Controls.Add(_unfold, 0, 6);
            layout.SetColumnSpan(_unfold, 2);
            layout.Controls.Add(_drag, 0, 7);
            layout.SetColumnSpan(_drag, 2);
            layout.Controls.Add(_info, 0, 8);
            layout.SetColumnSpan(_info, 2);
            layout.Controls.Add(_spin, 0, 9);
            layout.SetColumnSpan(_spin, 2);

            Controls.Add(layout);

            // The info text wraps to the sidebar's width
            Resize += (s, e) => _info.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 20), 0);

            _setStart.Click += (s, e) => _tool?.SetStartFromSelection();
            _setEnd.Click += (s, e) => _tool?.SetEndFromSelection();
            _clear.Click += (s, e) => _tool?.ClearPieces();
            _auto.CheckedChanged += (s, e) => { if (_tool != null) _tool.AutoSteps = _auto.Checked; };
            _steps.ValueChanged += (s, e) => { if (_tool != null) _tool.Steps = (int) _steps.Value; };
            _angle.ValueChanged += (s, e) => { if (_tool != null) _tool.AngleDegrees = (float) _angle.Value; };
            _mirror.CheckedChanged += (s, e) => { if (_tool != null) _tool.MirrorUvs = _mirror.Checked; };
            _unfold.CheckedChanged += (s, e) => { if (_tool != null) _tool.UnfoldUvs = _unfold.Checked; };
            _drag.CheckedChanged += (s, e) => { if (_tool != null) _tool.DragRotate = _drag.Checked; };
            _spin.Click += (s, e) => _tool?.ApplySpin();

            // When a manual spin is performed by dragging in a viewport, the tool pushes
            // the angle it used so the panel stays in sync
            Oy.Subscribe<string>("SpinTool:SetAngle", v =>
            {
                if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return;
                this.InvokeLater(() =>
                {
                    var value = (decimal) f;
                    value = Math.Min(_angle.Maximum, Math.Max(_angle.Minimum, value));
                    if (_angle.Value != value) _angle.Value = value;
                });
            });

            // The info line follows the selection, the pieces and the settings
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
            return context.TryGet("ActiveTool", out SpinTool _);
        }
    }
}
