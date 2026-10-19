using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Windows.Forms;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Bevel
{
    /// <summary>
    /// Sidebar panel for the bevel tool: "Bevel by [n] units [Bevel]" (the same row the vertex tool has),
    /// the Clamp option, and a line that says what is selected, what will happen, or why it can't.
    /// Only shown while the bevel tool is the active tool.
    ///
    /// The panel sizes itself to its content and every column follows the sidebar's width, so nothing is clipped
    /// or overlaps when the sidebar is narrow: the number box shrinks first, the texts never do, and the info
    /// text wraps.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    public class BevelToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private BevelTool _tool;

        public string Title { get; set; } = "Bevel";
        public object Control => this;

        private readonly NumericUpDown _distance;
        private readonly CheckBox _clamp;
        private readonly Button _bevel;
        private readonly NumericUpDown _segments;
        private readonly Button _all;
        private readonly Button _clear;
        private readonly Label _info;
        private readonly System.Windows.Forms.Timer _timer;

        public BevelToolSidebarPanel()
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
                ColumnCount = 4,
                Padding = new Padding(6)
            };

            // label | number (takes the spare width) | units | button
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var byLabel = new Label { Text = "Bevel by", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
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
            _bevel = new Button { Text = "Bevel", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly, MinimumSize = new Size(58, 27), Anchor = AnchorStyles.Right, Margin = new Padding(3) };

            var segmentsLabel = new Label { Text = "Segments", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 6) };
            _segments = new NumericUpDown
            {
                Minimum = 1,
                Maximum = BevelGeometry.MaxSegments,
                Value = 1,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                MinimumSize = new Size(40, 0),
                Margin = new Padding(3, 6, 3, 6)
            };

            _clamp = new CheckBox { Text = "Clamp", Checked = true, AutoSize = true, Margin = new Padding(3, 3, 3, 3) };

            // Two buttons that share the width equally, so neither can be squeezed out of view
            var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _all = new Button { Text = "All edges", Dock = DockStyle.Fill, AutoSize = false, Height = 26, Margin = new Padding(3) };
            _clear = new Button { Text = "Clear", Dock = DockStyle.Fill, AutoSize = false, Height = 26, Margin = new Padding(3) };
            buttons.Controls.Add(_all, 0, 0);
            buttons.Controls.Add(_clear, 1, 0);
            _info = new Label { AutoSize = true, MaximumSize = new Size(210, 0), Padding = new Padding(0, 4, 0, 4), Margin = new Padding(3) };

            layout.Controls.Add(byLabel, 0, 0);
            layout.Controls.Add(_distance, 1, 0);
            layout.Controls.Add(unitsLabel, 2, 0);
            layout.Controls.Add(_bevel, 3, 0);

            layout.Controls.Add(segmentsLabel, 0, 1);
            layout.Controls.Add(_segments, 1, 1);

            layout.Controls.Add(_clamp, 0, 2);
            layout.SetColumnSpan(_clamp, 4);

            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 4);

            layout.Controls.Add(_info, 0, 4);
            layout.SetColumnSpan(_info, 4);

            Controls.Add(layout);

            var tips = new ToolTip();
            tips.SetToolTip(_segments, "How many flat faces each bevel is made of. 1 is a flat chamfer; more give a rounded edge (the profile follows an arc, like Blender's default).");
            tips.SetToolTip(_all, "Select every edge of the selected solids.");
            tips.SetToolTip(_clear, "Deselect all edges (Esc).");
            tips.SetToolTip(_clamp, "Shrinks the distance to the largest one that keeps the solid valid, instead of refusing.");

            // The info text wraps to the sidebar's width
            Resize += (s, e) => _info.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 28), 0);

            _distance.ValueChanged += (s, e) => { if (_tool != null) _tool.Distance = (float) _distance.Value; };
            _segments.ValueChanged += (s, e) => { if (_tool != null) _tool.Segments = (int) _segments.Value; };
            _all.Click += (s, e) => _tool?.SelectAllEdges();
            _clamp.CheckedChanged += (s, e) => { if (_tool != null) _tool.Clamp = _clamp.Checked; };
            _bevel.Click += async (s, e) => { if (_tool != null) await _tool.Apply(); };
            _clear.Click += (s, e) => _tool?.ClearSelectedEdges();

            // The info line follows the selection, the edges and the settings
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
            return context.TryGet("ActiveTool", out BevelTool _);
        }
    }
}
