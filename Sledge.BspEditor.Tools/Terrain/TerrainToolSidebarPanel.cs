using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sledge.BspEditor.Terrain;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Terrain
{
    /// <summary>
    /// Sidebar panel for the terrain tool: make and pick a terrain, the sculpt brush, smoothing and weld, and the
    /// options for turning the height grid into brushes (textures, merging, collision blocks).
    /// Only shown while the terrain tool is the active tool.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    public class TerrainToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private TerrainTool _tool;

        public string Title { get; set; } = "Terrain";
        public object Control => this;

        private readonly TableLayoutPanel _layout;

        private readonly ComboBox _terrains;
        private readonly TextBox _name;
        private readonly Button _fromSelection;
        private readonly Button _delete;
        private readonly NumericUpDown _cell;
        private readonly NumericUpDown _levelHeight;

        private readonly ComboBox _mode;
        private readonly NumericUpDown _radius;
        private readonly NumericUpDown _strength;
        private readonly ComboBox _falloff;
        private readonly NumericUpDown _setLevel;
        private readonly NumericUpDown _smoothPasses;
        private readonly Button _smooth;
        private readonly Button _weld;

        private readonly TextBox _surfaceTexture;
        private readonly TextBox _hiddenTexture;
        private readonly CheckBox _merge;
        private readonly CheckBox _collision;
        private readonly NumericUpDown _block;
        private readonly ComboBox _sampling;
        private readonly TextBox _clipTexture;
        private readonly Button _toggle;

        private readonly Label _info;
        private readonly System.Windows.Forms.Timer _timer;

        private bool _syncing;
        private string _listKey = "";

        public TerrainToolSidebarPanel()
        {
            // The sidebar host docks this control at the top and uses the control's own height, so it sizes itself to its content
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            _layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(6)
            };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            // Terrain
            Header("Terrain");
            _terrains = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            Full(_terrains);
            _name = new TextBox { Dock = DockStyle.Fill };
            Row("Name:", _name);
            _fromSelection = MakeButton("Terrain from selected brush");
            Full(_fromSelection);
            _delete = MakeButton("Delete terrain");
            Full(_delete);
            _cell = Number(1, 1024, 32);
            _levelHeight = Number(1, 256, 16);
            Row("Cell size (units):", _cell);
            Row("Level height:", _levelHeight);

            // Sculpt
            Header("Sculpt (drag on the terrain)");
            _mode = Combo("Raise", "Lower", "Flatten", "Smooth", "Set height");
            _radius = Number(8, 4096, 96);
            _strength = Number(0.1m, 32, 1, 1, 0.5m);
            _falloff = Combo("Smooth", "Linear", "Constant");
            _setLevel = Number(-4096, 4096, 0);
            Row("Mode (Ctrl swaps raise/lower):", _mode);
            Row("Radius (units):", _radius);
            Row("Strength (levels):", _strength);
            Row("Falloff:", _falloff);
            Row("Set height (level):", _setLevel);
            _smoothPasses = Number(1, 16, 1);
            _smooth = MakeButton("Smooth all");
            Row("Passes:", _smoothPasses);
            Full(_smooth);
            _weld = MakeButton("Weld to neighbours");
            Full(_weld);

            // Brushes
            Header("Brushes");
            _surfaceTexture = new TextBox { Dock = DockStyle.Fill };
            _hiddenTexture = new TextBox { Dock = DockStyle.Fill };
            Row("Surface texture:", _surfaceTexture);
            Row("Hidden faces:", _hiddenTexture);
            _merge = Check("Merge flat and planar areas", true);
            Full(_merge);
            _collision = Check("Generate collision blocks", false);
            Full(_collision);
            _block = Number(1, 64, 4);
            _sampling = Combo("Corner height", "Lowest (never above surface)", "Highest (never below surface)");
            _clipTexture = new TextBox { Dock = DockStyle.Fill, Text = "CLIP" };
            Row("Block size (cells):", _block);
            Row("Heights from:", _sampling);
            Row("Collision texture:", _clipTexture);
            _toggle = MakeButton("Bake to brushes");
            _toggle.Height = 32;
            Full(_toggle);

            _info = new Label { AutoSize = true, MaximumSize = new Size(210, 0), Padding = new Padding(0, 6, 0, 4) };
            Full(_info);

            Controls.Add(_layout);

            // The info text wraps to the sidebar's width
            Resize += (s, e) => _info.MaximumSize = new Size(Math.Max(80, ClientSize.Width - 20), 0);

            WireUp();

            _timer = new System.Windows.Forms.Timer { Interval = 250 };
            _timer.Tick += (s, e) => Refresh_();
            _timer.Start();
        }

        #region Layout helpers

        private void Header(string text)
        {
            var l = new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 2) };
            Full(l);
        }

        private void Full(Control c)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.Controls.Add(c, 0, _layout.RowCount);
            _layout.SetColumnSpan(c, 2);
            _layout.RowCount++;
        }

        private void Row(string label, Control c)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, _layout.RowCount);
            _layout.Controls.Add(c, 1, _layout.RowCount);
            _layout.RowCount++;
        }

        private static Button MakeButton(string text)
        {
            return new Button { Text = text, Dock = DockStyle.Fill, AutoSize = false, Height = 26 };
        }

        private static CheckBox Check(string text, bool value)
        {
            return new CheckBox { Text = text, Checked = value, AutoSize = true };
        }

        private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals = 0, decimal increment = 1)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Increment = increment, Width = 70 };
        }

        private static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            c.Items.AddRange(items.Cast<object>().ToArray());
            c.SelectedIndex = 0;
            return c;
        }

        #endregion

        #region Events

        private void WireUp()
        {
            _terrains.SelectedIndexChanged += (s, e) =>
            {
                if (_syncing || _tool == null) return;
                var list = _tool.Terrains().ToList();
                if (_terrains.SelectedIndex >= 0 && _terrains.SelectedIndex < list.Count) _tool.SetActive(list[_terrains.SelectedIndex].ID);
            };
            _name.Leave += (s, e) => _tool?.RenameActive(_name.Text);
            _fromSelection.Click += (s, e) =>
            {
                if (_tool == null) return;
                var text = _tool.FromSelection();
                _info.Text = text;
            };
            _delete.Click += (s, e) => _tool?.DeleteActive();

            _cell.ValueChanged += (s, e) => { if (_tool != null) _tool.NewCellSize = (float) _cell.Value; };
            _levelHeight.ValueChanged += (s, e) => { if (_tool != null) _tool.NewLevelHeight = (float) _levelHeight.Value; };

            _mode.SelectedIndexChanged += (s, e) => { if (_tool != null) _tool.Mode = (SculptMode) _mode.SelectedIndex; };
            _radius.ValueChanged += (s, e) => { if (_tool != null) _tool.Radius = (float) _radius.Value; };
            _strength.ValueChanged += (s, e) => { if (_tool != null) _tool.Strength = (float) _strength.Value; };
            _falloff.SelectedIndexChanged += (s, e) => { if (_tool != null) _tool.Falloff = (BrushFalloff) _falloff.SelectedIndex; };
            _setLevel.ValueChanged += (s, e) => { if (_tool != null) _tool.SetLevel = (int) _setLevel.Value; };
            _smooth.Click += (s, e) => _tool?.SmoothActive((int) _smoothPasses.Value);
            _weld.Click += (s, e) =>
            {
                if (_tool == null) return;
                var text = _tool.WeldActive();
                _info.Text = text;
            };

            // Options of the active terrain are written straight to it
            _surfaceTexture.TextChanged += (s, e) => Edit(t => t.SurfaceTexture = _surfaceTexture.Text.Trim());
            _hiddenTexture.TextChanged += (s, e) => Edit(t => t.HiddenTexture = _hiddenTexture.Text.Trim());
            _clipTexture.TextChanged += (s, e) => Edit(t => t.ClipTexture = _clipTexture.Text.Trim());
            _merge.CheckedChanged += (s, e) => Edit(t => t.MergeCoplanar = _merge.Checked);
            _collision.CheckedChanged += (s, e) => Edit(t => t.CollisionEnabled = _collision.Checked);
            _block.ValueChanged += (s, e) => Edit(t => t.CollisionBlock = (int) _block.Value);
            _sampling.SelectedIndexChanged += (s, e) => Edit(t => t.CollisionSampling = (CollisionSampling) Math.Max(0, _sampling.SelectedIndex));

            _toggle.Click += (s, e) => _tool?.ToggleState();
        }

        private void Edit(Action<TerrainData> change)
        {
            if (_syncing || _tool == null) return;
            var t = _tool.Active;
            if (t != null) change(t);
        }

        #endregion

        #region Sync

        private void Refresh_()
        {
            if (!Visible || _tool == null) return;

            _syncing = true;
            try
            {
                var list = _tool.Terrains().ToList();
                var active = _tool.Active;

                var key = string.Join("|", list.Select(x => x.ID + ":" + x.Name)) + "#" + (active?.ID ?? 0);
                if (key != _listKey)
                {
                    _listKey = key;
                    _terrains.Items.Clear();
                    foreach (var t in list) _terrains.Items.Add(t.Name);
                    var i = active == null ? -1 : list.FindIndex(x => x.ID == active.ID);
                    _terrains.SelectedIndex = i;
                    if (!_name.Focused) _name.Text = active?.Name ?? "";
                }

                var enabled = active != null;
                _delete.Enabled = enabled;
                _surfaceTexture.Enabled = _hiddenTexture.Enabled = _clipTexture.Enabled = enabled;
                _merge.Enabled = _collision.Enabled = _block.Enabled = _sampling.Enabled = _toggle.Enabled = enabled;
                _smooth.Enabled = _weld.Enabled = enabled && active.State == TerrainState.Heightfield;

                if (active != null)
                {
                    Set(_surfaceTexture, active.SurfaceTexture);
                    Set(_hiddenTexture, active.HiddenTexture);
                    Set(_clipTexture, active.ClipTexture);
                    if (!_merge.Focused) _merge.Checked = active.MergeCoplanar;
                    if (!_collision.Focused) _collision.Checked = active.CollisionEnabled;
                    if (!_block.Focused) _block.Value = Math.Max(_block.Minimum, Math.Min(_block.Maximum, active.CollisionBlock));
                    if (!_sampling.Focused) _sampling.SelectedIndex = (int) active.CollisionSampling;
                    _toggle.Text = active.State == TerrainState.Heightfield ? "Bake to brushes" : "Edit heightfield";
                }

                var text = _tool.GetInfoText();
                if (_info.Text != text) _info.Text = text;
            }
            finally
            {
                _syncing = false;
            }
        }

        private static void Set(TextBox box, string value)
        {
            value = value ?? "";
            if (!box.Focused && box.Text != value) box.Text = value;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out TerrainTool _);
        }
    }
}
