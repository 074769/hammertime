using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Settings;
using Sledge.Shell.Registers;

namespace Sledge.Shell.Settings.Editors
{
	/// <summary>
	/// Settings editor for the top toolbar buttons: tick buttons to show them, reorder them, and pick an icon for each.
	/// Built in code (no designer file).
	/// </summary>
	public class TopToolbarEditor : UserControl, ISettingEditor
	{
		public event EventHandler<SettingKey> OnValueChanged;

		string ISettingEditor.Label { get; set; }
		public object Control => this;
		public SettingKey Key { get; set; }

		private const int IconSize = 16;

		private readonly ListView _list;
		private readonly ImageList _images;
		private readonly Button _up;
		private readonly Button _down;
		private readonly Button _chooseIcon;
		private readonly Button _resetIcon;
		private readonly Button _resetAll;
		private readonly Label _hint;

		private TopToolbarLayout _layout = new TopToolbarLayout();
		private bool _updating;

		public object Value
		{
			get => _layout;
			set
			{
				var saved = (value as TopToolbarLayout) ?? new TopToolbarLayout();
				_layout = saved.Resolve(Items().Select(x => x.Id));
				Rebuild(0);
			}
		}

		public TopToolbarEditor()
		{
			Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
			Size = new Size(560, 400);
			MinimumSize = new Size(420, 260);

			_hint = new Label
			{
				Dock = DockStyle.Top,
				Height = 34,
				Text = "Tick a button to show it in the top toolbar. Hidden buttons stay available in the menus. Press OK to apply."
			};

			_images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(IconSize, IconSize) };

			_list = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				CheckBoxes = true,
				HideSelection = false,
				MultiSelect = false,
				SmallImageList = _images,
				BorderStyle = BorderStyle.FixedSingle
			};
			_list.Columns.Add("Button", 170);
			_list.Columns.Add("Menu", 90);
			_list.Columns.Add("Custom icon", 260);
			_list.ItemChecked += ItemChecked;
			_list.SelectedIndexChanged += (s, e) => UpdateButtons();
			_list.DoubleClick += (s, e) => ChooseIcon();

			var buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Bottom,
				Height = 38,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Padding = new Padding(0, 6, 0, 0)
			};
			_up = MakeButton("Move up", (s, e) => Move(-1));
			_down = MakeButton("Move down", (s, e) => Move(1));
			_chooseIcon = MakeButton("Choose icon...", (s, e) => ChooseIcon());
			_resetIcon = MakeButton("Default icon", (s, e) => ResetIcon());
			_resetAll = MakeButton("Reset all", (s, e) => ResetAll());
			buttons.Controls.AddRange(new Control[] { _up, _down, _chooseIcon, _resetIcon, _resetAll });

			Controls.Add(_list);
			Controls.Add(buttons);
			Controls.Add(_hint);

			UpdateButtons();
		}

		private static Button MakeButton(string text, EventHandler click)
		{
			var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 26) };
			b.Click += click;
			return b;
		}

		private static System.Collections.Generic.IReadOnlyList<TopToolbarItemInfo> Items()
		{
			return MenuRegister.AllToolbarItems;
		}

		private static TopToolbarItemInfo Find(string id)
		{
			return Items().FirstOrDefault(x => x.Id == id);
		}

		private void Rebuild(int select)
		{
			_updating = true;
			_list.BeginUpdate();
			try
			{
				_list.Items.Clear();
				_images.Images.Clear();
				foreach (var entry in _layout)
				{
					var info = Find(entry.Id);
					Image icon = null;
					if (!string.IsNullOrWhiteSpace(entry.IconPath)) icon = IconLoader.LoadFromFile(entry.IconPath, IconSize);
					icon = icon ?? info?.DefaultIcon;
					var imageIndex = -1;
					if (icon != null)
					{
						_images.Images.Add(icon);
						imageIndex = _images.Images.Count - 1;
					}
					var item = new ListViewItem(info?.Name ?? entry.Id, imageIndex) { Checked = entry.Visible, Tag = entry };
					item.SubItems.Add(info?.Section ?? "");
					item.SubItems.Add(string.IsNullOrWhiteSpace(entry.IconPath) ? "(default)" : entry.IconPath);
					_list.Items.Add(item);
				}
				if (_list.Items.Count > 0)
				{
					var idx = Math.Max(0, Math.Min(select, _list.Items.Count - 1));
					_list.Items[idx].Selected = true;
					_list.Items[idx].Focused = true;
				}
			}
			finally
			{
				_list.EndUpdate();
				_updating = false;
			}
			UpdateButtons();
		}

		private void UpdateButtons()
		{
			var idx = _list.SelectedIndices.Count == 1 ? _list.SelectedIndices[0] : -1;
			_up.Enabled = idx > 0;
			_down.Enabled = idx >= 0 && idx < _list.Items.Count - 1;
			_chooseIcon.Enabled = idx >= 0;
			_resetIcon.Enabled = idx >= 0 && !string.IsNullOrWhiteSpace(_layout[idx].IconPath);
		}

		private void Changed()
		{
			OnValueChanged?.Invoke(this, Key);
		}

		private void ItemChecked(object sender, ItemCheckedEventArgs e)
		{
			if (_updating) return;
			if (e.Item.Tag is TopToolbarEntry entry)
			{
				entry.Visible = e.Item.Checked;
				Changed();
			}
		}

		private void Move(int delta)
		{
			if (_list.SelectedIndices.Count != 1) return;
			var idx = _list.SelectedIndices[0];
			var other = idx + delta;
			if (other < 0 || other >= _layout.Count) return;

			var tmp = _layout[idx];
			_layout[idx] = _layout[other];
			_layout[other] = tmp;

			Rebuild(other);
			Changed();
		}

		private void ChooseIcon()
		{
			if (_list.SelectedIndices.Count != 1) return;
			var idx = _list.SelectedIndices[0];
			var entry = _layout[idx];

			using (var ofd = new OpenFileDialog())
			{
				ofd.Title = "Choose an icon for " + (Find(entry.Id)?.Name ?? entry.Id);
				ofd.Filter = "Icon files (*.svg;*.png;*.ico;*.bmp;*.jpg;*.gif)|*.svg;*.png;*.ico;*.bmp;*.jpg;*.gif|All files (*.*)|*.*";
				if (!string.IsNullOrWhiteSpace(entry.IconPath)) ofd.FileName = entry.IconPath;
				if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;

				if (IconLoader.LoadFromFile(ofd.FileName, IconSize) == null)
				{
					MessageBox.Show(FindForm(), "That file couldn't be loaded as an icon.", "Choose icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				entry.IconPath = ofd.FileName;
			}

			Rebuild(idx);
			Changed();
		}

		private void ResetIcon()
		{
			if (_list.SelectedIndices.Count != 1) return;
			var idx = _list.SelectedIndices[0];
			_layout[idx].IconPath = null;
			Rebuild(idx);
			Changed();
		}

		private void ResetAll()
		{
			_layout = new TopToolbarLayout().Resolve(Items().Select(x => x.Id));
			Rebuild(0);
			Changed();
		}

		public void UseDarkTheme(bool dark)
		{
			DialogRegister.ColorControlsRecursively(this, dark);
		}
	}
}
