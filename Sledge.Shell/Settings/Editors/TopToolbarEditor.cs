using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Settings;
using Sledge.Shell.Registers;

namespace Sledge.Shell.Settings.Editors
{
	/// <summary>
	/// Settings editor for the top toolbar. The buttons that can be added are listed on the left, grouped by
	/// menu, with a search box. The toolbar itself is the list on the right, in the order it is shown, and it can
	/// hold divider lines. A preview strip at the top shows the result. Built in code (no designer file).
	/// </summary>
	public class TopToolbarEditor : UserControl, ISettingEditor
	{
		public event EventHandler<SettingKey> OnValueChanged;

		string ISettingEditor.Label { get; set; }
		public object Control => this;
		public SettingKey Key { get; set; }

		private const int ListIconSize = 16;

		private readonly TextBox _search;
		private readonly TreeView _available;
		private readonly ListView _list;
		private readonly ImageList _images;
		private readonly ToolStrip _preview;
		private readonly Panel _previewPanel;
		private readonly Panel _previewHost;
		private readonly HScrollBar _previewScroll;
		private readonly TableLayoutPanel _root;

		private readonly Button _add;
		private readonly Button _remove;
		private readonly Button _up;
		private readonly Button _down;
		private readonly Button _divider;
		private readonly Button _chooseIcon;
		private readonly Button _resetIcon;
		private readonly Button _resetAll;

		private readonly Dictionary<string, int> _imageIndex = new Dictionary<string, int>();

		private TopToolbarLayout _layout = new TopToolbarLayout();
		private bool _updating;

		public object Value
		{
			get => _layout;
			set
			{
				var saved = (value as TopToolbarLayout) ?? new TopToolbarLayout();
				_layout = saved.Resolve(Items().Select(x => x.Id));
				Rebuild(null);
			}
		}

		public TopToolbarEditor()
		{
			Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
			Size = new Size(640, 520);
			MinimumSize = new Size(520, 380);

			_images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(ListIconSize, ListIconSize) };

			var hint = new Label
			{
				Dock = DockStyle.Fill,
				Text = "Pick buttons on the left and add them to the toolbar. Drag rows to reorder them, and add dividers to group them. " +
				       "Double-click a row to add or remove it. Press OK to apply."
			};

			var previewLabel = new Label { Dock = DockStyle.Fill, Text = "Toolbar preview", TextAlign = ContentAlignment.BottomLeft };

			// the preview shows every button: when they do not fit, the panel scrolls sideways
			// (scroll bar, or the mouse wheel after clicking the preview) instead of hiding buttons in a drop down
			_preview = new ToolStrip
			{
				Dock = DockStyle.None,
				AutoSize = true,
				Location = Point.Empty,
				GripStyle = ToolStripGripStyle.Hidden,
				CanOverflow = false,
				ShowItemToolTips = true,
				LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow
			};
			// (the panel's own AutoScroll only moved its contents once the scroll bar thumb was released)
			_previewHost = new Panel { Dock = DockStyle.Fill };
			_previewHost.Controls.Add(_preview);
			_previewScroll = new HScrollBar { Dock = DockStyle.Bottom, SmallChange = 24 };
			_previewScroll.ValueChanged += (s, e) => _preview.Left = -_previewScroll.Value;
			_previewHost.Resize += (s, e) => UpdatePreviewScroll();
			_previewPanel = new ScrollPanel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
			_previewPanel.Controls.Add(_previewHost);
			_previewPanel.Controls.Add(_previewScroll);
			_previewPanel.MouseDown += (s, e) => _previewPanel.Focus();
			_previewHost.MouseDown += (s, e) => _previewPanel.Focus();
			_preview.MouseDown += (s, e) => _previewPanel.Focus();
			_previewPanel.MouseWheel += (s, e) => ScrollPreviewBy(-e.Delta / 3);

			// available buttons (left)
			_search = new TextBox { Dock = DockStyle.Top };
			_search.TextChanged += (s, e) => FillAvailable();
			_available = new TreeView
			{
				Dock = DockStyle.Fill,
				HideSelection = false,
				ShowLines = false,
				ShowRootLines = true,
				ImageList = _images,
				BorderStyle = BorderStyle.FixedSingle,
				AllowDrop = true
			};
			_available.AfterSelect += (s, e) => UpdateButtons();
			_available.NodeMouseDoubleClick += (s, e) => AddSelected();
			_available.MouseDown += (s, e) =>
			{
				_dragStart = e.Location;
				_dragSource = e.Button == MouseButtons.Left ? _available.GetNodeAt(e.X, e.Y) : null;
			};
			_available.MouseMove += (s, e) =>
			{
				if (ShouldStartDrag(e)) StartDrag(_available);
			};
			_available.MouseUp += (s, e) => _dragSource = null;
			_available.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
			_available.DragOver += (s, e) => e.Effect = e.Data.GetDataPresent(typeof(ListViewItem)) ? DragDropEffects.Move : DragDropEffects.None;
			_available.DragDrop += (s, e) =>
			{
				// dragging a row from the toolbar back onto the available list removes it
				if (e.Data.GetData(typeof(ListViewItem)) is ListViewItem dragged && dragged.Tag is TopToolbarEntry entry) Remove(entry);
			};

			var leftLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "Available buttons", TextAlign = ContentAlignment.BottomLeft };
			var left = new Panel { Dock = DockStyle.Fill };
			left.Controls.Add(_available);
			left.Controls.Add(_search);
			left.Controls.Add(leftLabel);

			// the toolbar (right)
			_list = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				HideSelection = false,
				MultiSelect = false,
				HeaderStyle = ColumnHeaderStyle.Nonclickable,
				SmallImageList = _images,
				BorderStyle = BorderStyle.FixedSingle,
				AllowDrop = true
			};
			_list.Columns.Add("Button", 210);
			_list.Columns.Add("Menu", 90);
			_list.Columns.Add("Icon", 70);
			_list.SelectedIndexChanged += (s, e) =>
			{
				UpdateButtons();
				if (!_updating) SyncPreviewSelection();
			};
			_list.DoubleClick += (s, e) => RemoveSelected();
			_list.MouseDown += (s, e) =>
			{
				_dragStart = e.Location;
				_dragSource = e.Button == MouseButtons.Left ? _list.GetItemAt(e.X, e.Y) : null;
			};
			_list.MouseMove += (s, e) =>
			{
				if (ShouldStartDrag(e)) StartDrag(_list);
			};
			_list.MouseUp += (s, e) => _dragSource = null;
			_list.DragEnter += (s, e) => e.Effect = CanDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
			_list.DragOver += (s, e) => e.Effect = CanDrop(e) ? DragDropEffects.Move : DragDropEffects.None;
			_list.DragDrop += (s, e) => DropOnList(e);

			var rightLabel = new Label { Dock = DockStyle.Top, Height = 20, Text = "On the toolbar", TextAlign = ContentAlignment.BottomLeft };
			var right = new Panel { Dock = DockStyle.Fill };
			right.Controls.Add(_list);
			right.Controls.Add(rightLabel);

			// buttons (middle)
			_add = MakeButton("Add  >", (s, e) => AddSelected());
			_remove = MakeButton("<  Remove", (s, e) => RemoveSelected());
			_up = MakeButton("Move up", (s, e) => MoveSelected(-1));
			_down = MakeButton("Move down", (s, e) => MoveSelected(1));
			_divider = MakeButton("Add divider", (s, e) => AddDivider());
			_chooseIcon = MakeButton("Choose icon...", (s, e) => ChooseIcon());
			_resetIcon = MakeButton("Default icon", (s, e) => ResetIcon());
			_resetAll = MakeButton("Reset to defaults", (s, e) => ResetAll());

			var middle = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.TopDown,
				WrapContents = false,
				Padding = new Padding(4, 24, 4, 0)
			};
			middle.Controls.AddRange(new Control[] { _add, _remove, Spacer(), _up, _down, Spacer(), _divider, _chooseIcon, _resetIcon });

			var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
			main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
			main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));
			main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
			main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			main.Controls.Add(left, 0, 0);
			main.Controls.Add(middle, 1, 0);
			main.Controls.Add(right, 2, 0);

			var bottom = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				Padding = new Padding(0, 4, 0, 0)
			};
			bottom.Controls.Add(_resetAll);

			var root = _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
			root.Controls.Add(hint, 0, 0);
			root.Controls.Add(previewLabel, 0, 1);
			root.Controls.Add(_previewPanel, 0, 2);
			root.Controls.Add(main, 0, 3);
			root.Controls.Add(bottom, 0, 4);

			Controls.Add(root);

			UpdateButtons();
		}

		/// <summary>A panel that can take the focus, so that it receives the mouse wheel.</summary>
		private class ScrollPanel : Panel
		{
			public ScrollPanel()
			{
				SetStyle(ControlStyles.Selectable, true);
			}
		}

		// A drag only starts while the left button is really held down and the mouse has moved a good
		// distance from where it was pressed, so a plain click can never leave a drag running (a drag makes
		// the lists scroll by themselves when the mouse is near their edges).
		private Point _dragStart;
		private object _dragSource;

		private bool ShouldStartDrag(MouseEventArgs e)
		{
			if (_dragSource == null) return false;
			if (e.Button != MouseButtons.Left || (System.Windows.Forms.Control.MouseButtons & MouseButtons.Left) == 0)
			{
				_dragSource = null;
				return false;
			}
			var threshold = Math.Max(SystemInformation.DragSize.Width, SystemInformation.DragSize.Height) * 3;
			return Math.Abs(e.X - _dragStart.X) > threshold || Math.Abs(e.Y - _dragStart.Y) > threshold;
		}

		private void StartDrag(Control source)
		{
			var item = _dragSource;
			_dragSource = null;
			if (item != null) source.DoDragDrop(item, DragDropEffects.Move);
		}

		private void ScrollPreviewBy(int dx)
		{
			SetPreviewScroll(_previewScroll.Value + dx);
		}

		private void SetPreviewScroll(int x)
		{
			var max = Math.Max(0, _previewScroll.Maximum - _previewScroll.LargeChange + 1);
			_previewScroll.Value = Math.Max(0, Math.Min(max, x));
		}

		/// <summary>Sets the scroll bar to the width of the strip and the width of the room there is for it.</summary>
		private void UpdatePreviewScroll()
		{
			var content = _preview.GetPreferredSize(Size.Empty).Width + 4;
			var view = Math.Max(1, _previewHost.ClientSize.Width);

			_previewScroll.Minimum = 0;
			_previewScroll.LargeChange = view;
			_previewScroll.Maximum = Math.Max(content, view) - 1;
			_previewScroll.Enabled = content > view;

			// keeps the position inside the new range and moves the strip to it
			SetPreviewScroll(_previewScroll.Value);
			_preview.Left = -_previewScroll.Value;
		}

		private static Button MakeButton(string text, EventHandler click)
		{
			var b = new Button { Text = text, Width = 122, Height = 26, Margin = new Padding(0, 0, 0, 4) };
			b.Click += click;
			return b;
		}

		private static Control Spacer()
		{
			return new Panel { Width = 122, Height = 10, Margin = new Padding(0) };
		}

		private static IReadOnlyList<TopToolbarItemInfo> Items()
		{
			return MenuRegister.AllToolbarItems;
		}

		private static TopToolbarItemInfo Find(string id)
		{
			return Items().FirstOrDefault(x => x.Id == id);
		}

		/// <summary>Buttons on the toolbar and dividers; hidden buttons are the ones that can still be added.</summary>
		private static bool IsShown(TopToolbarEntry entry)
		{
			return entry.IsSeparator() || entry.Visible;
		}

		private TopToolbarEntry SelectedEntry()
		{
			return _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as TopToolbarEntry : null;
		}

		private static int PreviewIconSize()
		{
			return Math.Max(16, Math.Min(64, TopToolbarSettings.IconSize));
		}

		private Image IconFor(TopToolbarEntry entry, TopToolbarItemInfo info, int size)
		{
			Image icon = null;
			if (!string.IsNullOrWhiteSpace(entry.IconPath)) icon = IconLoader.LoadFromFile(entry.IconPath, size);
			return icon ?? info?.IconAtSize?.Invoke(size) ?? info?.DefaultIcon;
		}

		// ---- building the three views ----

		private void Rebuild(TopToolbarEntry select)
		{
			_updating = true;
			try
			{
				BuildImages();
				FillToolbarList(select);
				FillAvailable();
				FillPreview();
			}
			finally
			{
				_updating = false;
			}
			UpdateButtons();
			SyncPreviewSelection();
		}

		private void BuildImages()
		{
			_images.Images.Clear();
			_imageIndex.Clear();
			foreach (var entry in _layout)
			{
				if (entry.IsSeparator() || _imageIndex.ContainsKey(entry.Id)) continue;
				var icon = IconFor(entry, Find(entry.Id), ListIconSize);
				if (icon == null) continue;
				_images.Images.Add(icon);
				_imageIndex[entry.Id] = _images.Images.Count - 1;
			}
		}

		private int ImageIndexOf(TopToolbarEntry entry)
		{
			return _imageIndex.TryGetValue(entry.Id, out var index) ? index : -1;
		}

		private void FillToolbarList(TopToolbarEntry select)
		{
			_list.BeginUpdate();
			try
			{
				_list.Items.Clear();
				foreach (var entry in _layout)
				{
					if (!IsShown(entry)) continue;

					ListViewItem item;
					if (entry.IsSeparator())
					{
						item = new ListViewItem("------------  divider  ------------", -1) { Tag = entry, ForeColor = SystemColors.GrayText };
					}
					else
					{
						var info = Find(entry.Id);
						if (info == null) continue;
						item = new ListViewItem(info.Name, ImageIndexOf(entry)) { Tag = entry };
						item.SubItems.Add(info.Section ?? "");
						item.SubItems.Add(string.IsNullOrWhiteSpace(entry.IconPath) ? "" : "custom");
					}
					_list.Items.Add(item);
					if (ReferenceEquals(entry, select))
					{
						item.Selected = true;
						item.Focused = true;
						item.EnsureVisible();
					}
				}
			}
			finally
			{
				_list.EndUpdate();
			}
		}

		private bool Matches(TopToolbarItemInfo info, string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return true;
			text = text.Trim();
			return (info.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
			       || (info.Section ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void FillAvailable()
		{
			var text = _search.Text;
			_available.BeginUpdate();
			try
			{
				_available.Nodes.Clear();

				var hidden = _layout.Where(e => !e.IsSeparator() && !e.Visible).ToList();
				var any = false;
				foreach (var section in Items().Select(x => x.Section ?? "").Distinct())
				{
					var children = new List<TreeNode>();
					foreach (var info in Items().Where(x => (x.Section ?? "") == section))
					{
						var entry = hidden.FirstOrDefault(e => e.Id == info.Id);
						if (entry == null || !Matches(info, text)) continue;
						var index = ImageIndexOf(entry);
						children.Add(new TreeNode(info.Name) { Tag = entry, ImageIndex = index, SelectedImageIndex = index });
					}
					if (children.Count == 0) continue;

					any = true;
					var node = new TreeNode(section + "  (" + children.Count + ")", children.ToArray()) { ImageIndex = -1, SelectedImageIndex = -1 };
					_available.Nodes.Add(node);
					node.Expand();
				}

				if (!any)
				{
					var message = string.IsNullOrWhiteSpace(text) ? "(every button is already on the toolbar)" : "(no button matches)";
					_available.Nodes.Add(new TreeNode(message) { ForeColor = SystemColors.GrayText });
				}
			}
			finally
			{
				_available.EndUpdate();
			}
			UpdateButtons();
		}

		private void FillPreview()
		{
			var size = PreviewIconSize();
			// room for the buttons and for the scroll bar under them
			_root.RowStyles[2].Height = size + 48;

			var scrolledTo = _previewScroll.Value;

			_preview.SuspendLayout();
			try
			{
				_preview.Items.Clear();
				_preview.ImageScalingSize = new Size(size, size);

				var dividerWaiting = false;
				foreach (var entry in _layout)
				{
					if (entry.IsSeparator())
					{
						dividerWaiting = _preview.Items.Count > 0;
						continue;
					}
					if (!entry.Visible) continue;
					var info = Find(entry.Id);
					if (info == null) continue;

					if (dividerWaiting)
					{
						_preview.Items.Add(new ToolStripSeparator());
						dividerWaiting = false;
					}

					var captured = entry;
					var button = new ToolStripButton
					{
						DisplayStyle = ToolStripItemDisplayStyle.Image,
						ImageScaling = ToolStripItemImageScaling.None,
						ImageAlign = ContentAlignment.MiddleCenter,
						Image = IconFor(entry, info, size),
						ToolTipText = info.Name,
						AutoSize = false,
						Size = new Size(size + 4, size + 4),
						Tag = entry
					};
					// clicking a button in the preview selects its row
					button.Click += (s, e) => SelectEntry(captured);
					_preview.Items.Add(button);
				}
			}
			finally
			{
				_preview.ResumeLayout(true);
			}

			// the scrollable width is the full width of the strip
			UpdatePreviewScroll();
			SetPreviewScroll(scrolledTo);
		}

		/// <summary>Marks the selected row's button in the preview and scrolls the preview to show it.</summary>
		private void SyncPreviewSelection()
		{
			var selected = SelectedEntry();
			ToolStripButton shown = null;
			foreach (ToolStripItem item in _preview.Items)
			{
				var button = item as ToolStripButton;
				if (button == null) continue;
				var isSelected = selected != null && ReferenceEquals(button.Tag, selected);
				button.Checked = isSelected;
				if (isSelected) shown = button;
			}
			if (shown == null) return;

			var left = _previewScroll.Value;
			if (shown.Bounds.Left < left || shown.Bounds.Right > left + _previewHost.ClientSize.Width)
			{
				SetPreviewScroll(shown.Bounds.Left - 24);
			}
		}

		private void SelectEntry(TopToolbarEntry entry)
		{
			foreach (ListViewItem item in _list.Items)
			{
				if (!ReferenceEquals(item.Tag, entry)) continue;
				item.Selected = true;
				item.Focused = true;
				item.EnsureVisible();
				return;
			}
		}

		private void UpdateButtons()
		{
			if (_updating) return;

			var node = _available.SelectedNode;
			_add.Enabled = node != null && (node.Tag is TopToolbarEntry || node.Nodes.Count > 0);

			var entry = SelectedEntry();
			var shown = _layout.Where(IsShown).ToList();
			var index = entry == null ? -1 : shown.IndexOf(entry);
			_remove.Enabled = entry != null;
			_up.Enabled = index > 0;
			_down.Enabled = index >= 0 && index < shown.Count - 1;
			_chooseIcon.Enabled = entry != null && !entry.IsSeparator();
			_resetIcon.Enabled = entry != null && !entry.IsSeparator() && !string.IsNullOrWhiteSpace(entry.IconPath);
		}

		private void Changed()
		{
			OnValueChanged?.Invoke(this, Key);
		}

		// ---- changing the layout ----

		/// <summary>Moves an entry next to another one (or to the end when there is no target).</summary>
		private void Place(TopToolbarEntry entry, TopToolbarEntry target, bool after)
		{
			_layout.Remove(entry);
			var index = _layout.Count;
			if (target != null)
			{
				var at = _layout.IndexOf(target);
				if (at >= 0) index = at + (after ? 1 : 0);
			}
			_layout.Insert(index, entry);
		}

		private void AddEntries(IEnumerable<TopToolbarEntry> entries, TopToolbarEntry target, bool after)
		{
			TopToolbarEntry last = null;
			foreach (var entry in entries.ToList())
			{
				entry.Visible = true;
				if (last != null) Place(entry, last, true);
				else Place(entry, target, after);
				last = entry;
			}
			if (last == null) return;
			Rebuild(last);
			Changed();
		}

		private IEnumerable<TopToolbarEntry> EntriesOf(TreeNode node)
		{
			if (node == null) yield break;
			if (node.Tag is TopToolbarEntry entry) yield return entry;
			foreach (TreeNode child in node.Nodes)
			{
				if (child.Tag is TopToolbarEntry e) yield return e;
			}
		}

		private void AddSelected()
		{
			var entries = EntriesOf(_available.SelectedNode).ToList();
			if (entries.Count == 0) return;
			// goes right after the selected row, or at the end
			AddEntries(entries, SelectedEntry(), true);
		}

		private void Remove(TopToolbarEntry entry)
		{
			var shown = _layout.Where(IsShown).ToList();
			var at = shown.IndexOf(entry);
			TopToolbarEntry neighbour = null;
			if (at >= 0) neighbour = at + 1 < shown.Count ? shown[at + 1] : (at > 0 ? shown[at - 1] : null);

			if (entry.IsSeparator()) _layout.Remove(entry);
			else entry.Visible = false;

			Rebuild(neighbour);
			Changed();
		}

		private void RemoveSelected()
		{
			var entry = SelectedEntry();
			if (entry != null) Remove(entry);
		}

		private void MoveSelected(int delta)
		{
			var entry = SelectedEntry();
			if (entry == null) return;

			var from = _layout.IndexOf(entry);
			var to = from + delta;
			while (to >= 0 && to < _layout.Count && !IsShown(_layout[to])) to += delta;
			if (from < 0 || to < 0 || to >= _layout.Count) return;

			var other = _layout[to];
			_layout[to] = entry;
			_layout[from] = other;

			Rebuild(entry);
			Changed();
		}

		private void AddDivider()
		{
			var divider = TopToolbarEntry.NewSeparator();
			var target = SelectedEntry();
			if (target == null) _layout.Add(divider);
			else _layout.Insert(_layout.IndexOf(target) + 1, divider);

			Rebuild(divider);
			Changed();
		}

		private void ChooseIcon()
		{
			var entry = SelectedEntry();
			if (entry == null || entry.IsSeparator()) return;

			using (var ofd = new OpenFileDialog())
			{
				ofd.Title = "Choose an icon for " + (Find(entry.Id)?.Name ?? entry.Id);
				ofd.Filter = "Icon files (*.svg;*.png;*.ico;*.bmp;*.jpg;*.gif)|*.svg;*.png;*.ico;*.bmp;*.jpg;*.gif|All files (*.*)|*.*";
				if (!string.IsNullOrWhiteSpace(entry.IconPath)) ofd.FileName = entry.IconPath;
				if (ofd.ShowDialog(FindForm()) != DialogResult.OK) return;

				if (IconLoader.LoadFromFile(ofd.FileName, ListIconSize) == null)
				{
					MessageBox.Show(FindForm(), "That file couldn't be loaded as an icon.", "Choose icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				entry.IconPath = ofd.FileName;
			}

			Rebuild(entry);
			Changed();
		}

		private void ResetIcon()
		{
			var entry = SelectedEntry();
			if (entry == null) return;
			entry.IconPath = null;
			Rebuild(entry);
			Changed();
		}

		private void ResetAll()
		{
			var answer = MessageBox.Show(FindForm(), "Put the toolbar back to its default buttons, order and icons?", "Reset toolbar",
				MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
			if (answer != DialogResult.OK) return;

			_layout = new TopToolbarLayout().Resolve(Items().Select(x => x.Id)).WithDefaultSeparators(Items());
			Rebuild(null);
			Changed();
		}

		// ---- drag and drop on the toolbar list ----

		private static bool CanDrop(DragEventArgs e)
		{
			return e.Data.GetDataPresent(typeof(ListViewItem)) || e.Data.GetDataPresent(typeof(TreeNode));
		}

		private void DropOnList(DragEventArgs e)
		{
			var pt = _list.PointToClient(new Point(e.X, e.Y));
			var over = _list.GetItemAt(pt.X, pt.Y);

			TopToolbarEntry target;
			bool after;
			if (over != null)
			{
				target = over.Tag as TopToolbarEntry;
				after = pt.Y > over.Bounds.Top + over.Bounds.Height / 2;
			}
			else
			{
				// below the last row: to the end
				target = _list.Items.Count > 0 ? _list.Items[_list.Items.Count - 1].Tag as TopToolbarEntry : null;
				after = true;
			}

			if (e.Data.GetData(typeof(ListViewItem)) is ListViewItem dragged)
			{
				var moving = dragged.Tag as TopToolbarEntry;
				if (moving == null || ReferenceEquals(moving, target)) return;
				Place(moving, target, after);
				Rebuild(moving);
				Changed();
				return;
			}

			if (e.Data.GetData(typeof(TreeNode)) is TreeNode node)
			{
				AddEntries(EntriesOf(node), target, after);
			}
		}

		public void UseDarkTheme(bool dark)
		{
			DialogRegister.ColorControlsRecursively(this, dark);
		}
	}
}
