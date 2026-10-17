using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.Common.Logging;
using Sledge.FileSystem;
using Sledge.Providers.Model.Mdl10.Format;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser
{
	/// <summary>
	/// A browser for .mdl models: folder tree, search across all folders, a list or thumbnail grid, and an
	/// interactive preview (orbit / zoom / pan, shading modes, sequences, skins and bodygroups) with model information.
	/// </summary>
	public class ModelBrowserDialog : Form
	{
		private const int SearchLimit = 5000;
		private const int ThumbSize = 112;
		private const int MaxThumbs = 1500;
		private const string PlaceholderKey = "__placeholder";
		private static readonly Color ThumbBackground = Color.FromArgb(64, 66, 72);

		// Remembered between openings of the dialog
		private static bool s_grid;
		private static int s_shading;

		public IFile SelectedFile { get; private set; }

		private readonly IFile _root;
		private readonly IFile _modelsRoot;
		private readonly bool _hasModelsFolder;

		private readonly TextBox _search;
		private readonly Label _count;
		private readonly Button _gridToggle;
		private readonly SplitContainer _outer;
		private readonly SplitContainer _inner;
		private readonly TreeView _tree;
		private readonly ListView _list;
		private readonly ImageList _thumbs;
		private readonly ModelViewerControl _viewer;
		private readonly ComboBox _shading;
		private readonly NumericUpDown _skin;
		private readonly ComboBox _sequence;
		private readonly CheckBox _play;
		private readonly TrackBar _frame;
		private readonly Label _frameLabel;
		private readonly FlowLayoutPanel _bodyPanel;
		private readonly Button _expand;
		private readonly TextBox _info;
		private readonly TextBox _pathBox;
		private readonly Button _ok;
		private readonly Button _cancel;
		private readonly Timer _previewTimer;
		private readonly Timer _thumbTimer;
		private readonly WheelFilter _wheelFilter;

		private List<(IFile File, string Path)> _index;
		private bool _suppressSearchEvent;
		private bool _suppressTreeEvent;
		private bool _suppressControls;

		private int _loadVersion;
		private IFile _pendingPreview;

		private readonly HashSet<string> _thumbHave = new HashSet<string>();
		private readonly HashSet<string> _thumbFailed = new HashSet<string>();
		private readonly Bitmap _placeholder;
		private bool _thumbBusy;

		public ModelBrowserDialog(IFile root, string currentValue)
		{
			_root = root;
			var models = root?.TraversePath("models");
			_hasModelsFolder = models != null && models.IsContainer;
			_modelsRoot = _hasModelsFolder ? models : root;

			SuspendLayout();

			Text = "Model Browser";
			StartPosition = FormStartPosition.CenterParent;
			ShowInTaskbar = false;
			MinimizeBox = false;
			MaximizeBox = true;
			Size = new Size(1320, 800);
			MinimumSize = new Size(900, 540);

			// Bottom bar
			var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
			_cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Anchor = AnchorStyles.Right | AnchorStyles.Top };
			_ok = new Button { Text = "OK", Width = 90, Enabled = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
			_ok.Click += (s, e) => Confirm();
			_pathBox = new TextBox { ReadOnly = true, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
			bottom.Controls.Add(_pathBox);
			bottom.Controls.Add(_ok);
			bottom.Controls.Add(_cancel);
			bottom.Resize += (s, e) => LayoutBottom(bottom);
			AcceptButton = _ok;
			CancelButton = _cancel;

			// Top bar
			var top = new Panel { Dock = DockStyle.Top, Height = 34 };
			var searchLabel = new Label { Text = "Search:", AutoSize = true, Left = 8, Top = 9 };
			_search = new TextBox { Left = 62, Top = 6, Width = 300 };
			_search.TextChanged += (s, e) =>
			{
				if (!_suppressSearchEvent) RefreshList(null);
			};
			_gridToggle = new Button { Left = 372, Top = 5, Width = 86, Height = 24 };
			_gridToggle.Click += (s, e) => SetGrid(!s_grid);
			_count = new Label { AutoSize = true, Left = 470, Top = 9, Text = "" };
			top.Controls.Add(searchLabel);
			top.Controls.Add(_search);
			top.Controls.Add(_gridToggle);
			top.Controls.Add(_count);

			// Tree | (list | preview)
			_outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
			_inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
			_outer.Panel2.Controls.Add(_inner);

			_tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true, PathSeparator = "/" };
			_tree.BeforeExpand += TreeBeforeExpand;
			_tree.AfterSelect += TreeAfterSelect;
			_outer.Panel1.Controls.Add(_tree);

			// Thumbnails for the grid view
			_placeholder = MakePlaceholder();
			_thumbs = new ImageList { ImageSize = new Size(ThumbSize, ThumbSize), ColorDepth = ColorDepth.Depth32Bit };
			_thumbs.Images.Add(PlaceholderKey, _placeholder);

			_list = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				MultiSelect = false,
				HideSelection = false,
				GridLines = false,
				ShowItemToolTips = true,
				LargeImageList = _thumbs
			};
			_list.Columns.Add("Name", 190);
			_list.Columns.Add("Folder", 150);
			_list.Columns.Add("Size", 70, HorizontalAlignment.Right);
			_list.SelectedIndexChanged += ListSelectionChanged;
			_list.DoubleClick += (s, e) => { if (_list.SelectedItems.Count == 1) Confirm(); };
			_list.ColumnClick += ListColumnClick;
			_inner.Panel1.Controls.Add(_list);

			// Preview panel: controls on top, viewer in the middle, frame bar below it, details at the bottom
			var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
			var previewSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };

			_viewer = new ModelViewerControl { Dock = DockStyle.Fill };
			_viewer.FrameChanged += (s, f) => SetFrameControl(f);

			var frameBar = new Panel { Dock = DockStyle.Bottom, Height = 30 };
			_frame = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 0, Enabled = false, AutoSize = false };
			_frame.ValueChanged += (s, e) =>
			{
				if (_suppressControls) return;
				_suppressControls = true;
				_play.Checked = false;
				_suppressControls = false;
				_viewer.SetFrame(_frame.Value);
				UpdateFrameLabel();
			};
			_frameLabel = new Label { Dock = DockStyle.Right, Width = 90, TextAlign = ContentAlignment.MiddleRight, Text = "" };
			frameBar.Controls.Add(_frame);
			frameBar.Controls.Add(_frameLabel);

			_bodyPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Visible = false };

			var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
			_shading = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
			_shading.Items.AddRange(new object[] { "Smooth", "Flat", "Unlit (fullbright)", "Solid (no textures)", "Wireframe" });
			_shading.SelectedIndex = Math.Max(0, Math.Min(s_shading, _shading.Items.Count - 1));
			_viewer.Shading = (ModelShading) _shading.SelectedIndex;
			_shading.SelectedIndexChanged += (s, e) =>
			{
				s_shading = _shading.SelectedIndex;
				_viewer.Shading = (ModelShading) _shading.SelectedIndex;
			};
			_skin = new NumericUpDown { Width = 52, Minimum = 0, Maximum = 0, Enabled = false };
			_skin.ValueChanged += (s, e) =>
			{
				if (!_suppressControls) _viewer.Skin = (int) _skin.Value;
			};
			_sequence = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Enabled = false };
			_sequence.SelectedIndexChanged += (s, e) =>
			{
				if (_suppressControls || _sequence.SelectedIndex < 0) return;
				_viewer.SetSequence(_sequence.SelectedIndex);
				UpdateFrameControls();
			};
			_play = new CheckBox { Text = "Play", Appearance = Appearance.Button, AutoSize = true, Enabled = false, TextAlign = ContentAlignment.MiddleCenter };
			_play.CheckedChanged += (s, e) =>
			{
				_play.Text = _play.Checked ? "Pause" : "Play";
				if (!_suppressControls) _viewer.Playing = _play.Checked;
			};
			var reset = new Button { Text = "Reset view", AutoSize = true };
			reset.Click += (s, e) => _viewer.ResetView();
			_expand = new Button { Text = "Expand viewer", AutoSize = true };
			_expand.Click += (s, e) => ToggleExpand();

			bar.Controls.Add(MakeLabel("Shading:"));
			bar.Controls.Add(_shading);
			bar.Controls.Add(MakeLabel("Skin:"));
			bar.Controls.Add(_skin);
			bar.Controls.Add(MakeLabel("Sequence:"));
			bar.Controls.Add(_sequence);
			bar.Controls.Add(_play);
			bar.Controls.Add(reset);
			bar.Controls.Add(_expand);

			// Dock order: the last added control is laid out first
			previewSplit.Panel1.Controls.Add(_viewer);
			previewSplit.Panel1.Controls.Add(frameBar);
			previewSplit.Panel1.Controls.Add(_bodyPanel);
			previewSplit.Panel1.Controls.Add(bar);

			_info = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				WordWrap = false,
				Font = new Font(FontFamily.GenericMonospace, 8.25f),
				BackColor = SystemColors.Window
			};
			previewSplit.Panel2.Controls.Add(_info);

			preview.Controls.Add(previewSplit);
			_inner.Panel2.Controls.Add(preview);

			Controls.Add(_outer);
			Controls.Add(top);
			Controls.Add(bottom);
			LayoutBottom(bottom);

			_previewTimer = new Timer { Interval = 150 };
			_previewTimer.Tick += (s, e) =>
			{
				_previewTimer.Stop();
				var f = _pendingPreview;
				_pendingPreview = null;
				if (f != null) ShowModel(f);
			};

			_thumbTimer = new Timer { Interval = 80 };
			_thumbTimer.Tick += (s, e) => ThumbTick();
			_thumbTimer.Start();

			_wheelFilter = new WheelFilter(this);

			SetGrid(s_grid);
			ResumeLayout(true);

			Load += (s, e) =>
			{
				try
				{
					_outer.SplitterDistance = Math.Max(_outer.Panel1MinSize, 200);
					// The preview gets the larger share of the width
					_inner.SplitterDistance = Math.Max(_inner.Panel1MinSize, _inner.Width - (int) (_inner.Width * 0.62));
					previewSplit.Panel1MinSize = 220;
					previewSplit.Panel2MinSize = 60;
					previewSplit.SplitterDistance = Math.Max(previewSplit.Panel1MinSize, previewSplit.Height - 190);
				}
				catch (Exception ex)
				{
					Log.Error(nameof(ModelBrowserDialog), "Unable to set splitter distance", ex);
				}
				BuildTree(currentValue);
			};
		}

		private static Label MakeLabel(string text)
		{
			return new Label { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 7, 0, 0) };
		}

		private static Bitmap MakePlaceholder()
		{
			var bmp = new Bitmap(ThumbSize, ThumbSize);
			using (var g = Graphics.FromImage(bmp))
			using (var brush = new SolidBrush(Color.FromArgb(150, 150, 158)))
			using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
			using (var font = new Font(FontFamily.GenericSansSerif, 10f))
			{
				g.Clear(ThumbBackground);
				g.DrawString("MDL", font, brush, new RectangleF(0, 0, ThumbSize, ThumbSize), format);
			}
			return bmp;
		}

		private void LayoutBottom(Panel bottom)
		{
			var w = bottom.ClientSize.Width;
			_cancel.Left = w - _cancel.Width - 8;
			_cancel.Top = 7;
			_ok.Left = _cancel.Left - _ok.Width - 6;
			_ok.Top = 7;
			_pathBox.Left = 8;
			_pathBox.Top = 9;
			_pathBox.Width = Math.Max(50, _ok.Left - 16);
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			Application.AddMessageFilter(_wheelFilter);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			Application.RemoveMessageFilter(_wheelFilter);
			base.OnFormClosed(e);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Application.RemoveMessageFilter(_wheelFilter);
				_previewTimer?.Dispose();
				_thumbTimer?.Dispose();
				_thumbs?.Dispose();
				_placeholder?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <summary>
		/// Routes the mouse wheel to the viewer while the cursor is over it, whichever control has the keyboard focus.
		/// </summary>
		private class WheelFilter : IMessageFilter
		{
			private readonly ModelBrowserDialog _owner;

			public WheelFilter(ModelBrowserDialog owner)
			{
				_owner = owner;
			}

			public bool PreFilterMessage(ref Message m)
			{
				return _owner.HandleWheel(ref m);
			}
		}

		private bool HandleWheel(ref Message m)
		{
			const int WM_MOUSEWHEEL = 0x020A;
			if (m.Msg != WM_MOUSEWHEEL || IsDisposed || !_viewer.IsHandleCreated || !_viewer.Visible || Form.ActiveForm != this) return false;

			var lp = m.LParam.ToInt64();
			var x = (short) (lp & 0xFFFF);
			var y = (short) ((lp >> 16) & 0xFFFF);
			var delta = (short) ((m.WParam.ToInt64() >> 16) & 0xFFFF);

			var p = _viewer.PointToClient(new Point(x, y));
			if (!_viewer.ClientRectangle.Contains(p)) return false;

			_viewer.ApplyWheel(delta, p);
			return true;
		}

		private void ToggleExpand()
		{
			var collapse = !_outer.Panel1Collapsed;
			_outer.Panel1Collapsed = collapse;
			_inner.Panel1Collapsed = collapse;
			_expand.Text = collapse ? "Show list" : "Expand viewer";
		}

		#region Tree

		private void BuildTree(string currentValue)
		{
			_tree.BeginUpdate();
			try
			{
				_tree.Nodes.Clear();
				var rootNode = new TreeNode(_hasModelsFolder ? "models" : "(game root)") { Tag = _modelsRoot };
				_tree.Nodes.Add(rootNode);
				PopulateNode(rootNode);
				rootNode.Expand();
			}
			finally
			{
				_tree.EndUpdate();
			}

			// Walk to the folder of the current value, if any
			var segments = (currentValue ?? "").Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			var fileName = segments.Count > 0 ? segments[segments.Count - 1] : null;
			if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
			if (_hasModelsFolder && segments.Count > 0 && string.Equals(segments[0], "models", StringComparison.OrdinalIgnoreCase)) segments.RemoveAt(0);
			else if (_hasModelsFolder && segments.Count == 0 && fileName != null && string.Equals(fileName, "models", StringComparison.OrdinalIgnoreCase)) fileName = null;

			var node = _tree.Nodes[0];
			foreach (var seg in segments)
			{
				PopulateNode(node);
				var next = node.Nodes.Cast<TreeNode>().FirstOrDefault(n => string.Equals(n.Text, seg, StringComparison.OrdinalIgnoreCase));
				if (next == null) break;
				node = next;
				node.Parent?.Expand();
			}

			_suppressTreeEvent = true;
			_tree.SelectedNode = node;
			_suppressTreeEvent = false;
			node.EnsureVisible();

			RefreshList(string.IsNullOrEmpty(fileName) ? null : fileName);
		}

		private static void PopulateNode(TreeNode node)
		{
			if (node.Nodes.Count == 1 && node.Nodes[0].Tag == null)
			{
				node.Nodes.Clear();
			}
			else if (node.Nodes.Count > 0)
			{
				return; // already populated
			}

			var folder = node.Tag as IFile;
			if (folder == null) return;

			try
			{
				foreach (var child in folder.GetChildren().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
				{
					var n = new TreeNode(child.Name) { Tag = child };
					if (child.NumChildren > 0) n.Nodes.Add(new TreeNode("...")); // placeholder, Tag == null
					node.Nodes.Add(n);
				}
			}
			catch (Exception ex)
			{
				Log.Error(nameof(ModelBrowserDialog), "Unable to list folder " + folder.FullPathName, ex);
			}
		}

		private void TreeBeforeExpand(object sender, TreeViewCancelEventArgs e)
		{
			PopulateNode(e.Node);
		}

		private void TreeAfterSelect(object sender, TreeViewEventArgs e)
		{
			if (_suppressTreeEvent) return;
			if (_search.Text.Length > 0)
			{
				_suppressSearchEvent = true;
				_search.Text = "";
				_suppressSearchEvent = false;
			}
			RefreshList(null);
		}

		#endregion

		#region List

		private static bool IsMdl(IFile f)
		{
			return string.Equals(f.Extension, "mdl", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Texture ("xxxT.mdl") and sequence group ("xxx01.mdl") files are loaded together with the main model,
		/// so they are hidden from the list: "xxxT.mdl" when "xxx.mdl" exists next to it, and "xxxNN.mdl" when it
		/// really is a sequence group file (IDSQ header).
		/// </summary>
		private static bool IsCompanion(IFile f)
		{
			try
			{
				var dir = f.Parent;
				var name = f.NameWithoutExtension;
				if (dir == null || string.IsNullOrEmpty(name) || name.Length < 2) return false;

				if (name.EndsWith("t", StringComparison.OrdinalIgnoreCase))
				{
					var main = dir.GetFile(name.Substring(0, name.Length - 1) + ".mdl");
					if (main != null && main.Exists) return true;
				}

				if (name.Length > 2 && char.IsDigit(name[name.Length - 1]) && char.IsDigit(name[name.Length - 2]))
				{
					using (var s = f.Open())
					{
						var id = new byte[4];
						var read = s.Read(id, 0, 4);
						return read == 4 && id[0] == (byte) 'I' && id[1] == (byte) 'D' && id[2] == (byte) 'S' && id[3] == (byte) 'Q';
					}
				}

				return false;
			}
			catch
			{
				return false;
			}
		}

		private static string GetGamePath(IFile file)
		{
			var path = "";
			while (file != null && !(file is RootFile))
			{
				path = "/" + file.Name + path;
				file = file.Parent;
			}
			return path.TrimStart('/');
		}

		private List<(IFile File, string Path)> GetIndex()
		{
			if (_index != null) return _index;

			var old = Cursor;
			Cursor = Cursors.WaitCursor;
			try
			{
				_index = new List<(IFile, string)>();
				try
				{
					foreach (var f in _modelsRoot.GetFiles(true))
					{
						if (!IsMdl(f) || IsCompanion(f)) continue;
						_index.Add((f, GetGamePath(f)));
					}
				}
				catch (Exception ex)
				{
					Log.Error(nameof(ModelBrowserDialog), "Unable to index models", ex);
				}
				_index = _index.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
			}
			finally
			{
				Cursor = old;
			}
			return _index;
		}

		private void RefreshList(string selectName)
		{
			var query = (_search.Text ?? "").Trim();
			var items = new List<ListViewItem>();
			var truncated = false;

			if (query.Length == 0)
			{
				var folder = _tree.SelectedNode?.Tag as IFile ?? _modelsRoot;
				try
				{
					foreach (var f in folder.GetFiles().Where(IsMdl).Where(x => !IsCompanion(x)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
					{
						items.Add(MakeItem(f, ""));
					}
				}
				catch (Exception ex)
				{
					Log.Error(nameof(ModelBrowserDialog), "Unable to list models", ex);
				}
			}
			else
			{
				var tokens = query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var (file, path) in GetIndex())
				{
					var lower = path.ToLowerInvariant();
					if (!tokens.All(t => lower.Contains(t))) continue;
					if (items.Count >= SearchLimit)
					{
						truncated = true;
						break;
					}
					var slash = path.LastIndexOf('/');
					items.Add(MakeItem(file, slash > 0 ? path.Substring(0, slash) : ""));
				}
			}

			_list.BeginUpdate();
			try
			{
				_list.Items.Clear();
				_list.Items.AddRange(items.ToArray());
			}
			finally
			{
				_list.EndUpdate();
			}

			_count.Text = items.Count + (truncated ? "+" : "") + (items.Count == 1 ? " model" : " models") + (query.Length > 0 ? " (all folders)" : "");

			ListViewItem toSelect = null;
			if (selectName != null)
			{
				toSelect = items.FirstOrDefault(x => string.Equals(x.Text, selectName, StringComparison.OrdinalIgnoreCase));
			}
			if (toSelect != null)
			{
				toSelect.Selected = true;
				toSelect.Focused = true;
				toSelect.EnsureVisible();
			}
			else
			{
				ListSelectionChanged(null, EventArgs.Empty);
			}
		}

		private ListViewItem MakeItem(IFile file, string folder)
		{
			var item = new ListViewItem(file.Name) { Tag = file, ToolTipText = GetGamePath(file) };
			item.ImageKey = _thumbHave.Contains(file.FullPathName) ? file.FullPathName : PlaceholderKey;
			item.SubItems.Add(folder);
			item.SubItems.Add(FormatSize(file.Size));
			return item;
		}

		private static string FormatSize(long bytes)
		{
			if (bytes < 1024) return bytes + " B";
			if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
			return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
		}

		private int _sortColumn = -1;
		private bool _sortAscending = true;

		private void ListColumnClick(object sender, ColumnClickEventArgs e)
		{
			if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
			else
			{
				_sortColumn = e.Column;
				_sortAscending = true;
			}

			var items = _list.Items.Cast<ListViewItem>().ToList();
			Func<ListViewItem, object> key;
			if (e.Column == 2) key = x => ((IFile) x.Tag).Size;
			else key = x => x.SubItems[e.Column].Text.ToLowerInvariant();

			var sorted = (_sortAscending ? items.OrderBy(key) : items.OrderByDescending(key)).ToArray();
			_list.BeginUpdate();
			try
			{
				_list.Items.Clear();
				_list.Items.AddRange(sorted);
			}
			finally
			{
				_list.EndUpdate();
			}
		}

		private void ListSelectionChanged(object sender, EventArgs e)
		{
			if (_list.SelectedItems.Count != 1)
			{
				_ok.Enabled = false;
				_pathBox.Text = "";
				_pendingPreview = null;
				_previewTimer.Stop();
				if (_list.Items.Count == 0 || _list.SelectedItems.Count == 0) ClearPreview("Select a model to preview");
				return;
			}

			var file = (IFile) _list.SelectedItems[0].Tag;
			_ok.Enabled = true;
			_pathBox.Text = GetGamePath(file);

			// Debounce so holding an arrow key doesn't load every model it passes over
			_pendingPreview = file;
			_previewTimer.Stop();
			_previewTimer.Start();
		}

		private void Confirm()
		{
			if (_list.SelectedItems.Count != 1) return;
			SelectedFile = (IFile) _list.SelectedItems[0].Tag;
			DialogResult = DialogResult.OK;
			Close();
		}

		#endregion

		#region Grid view / thumbnails

		private void SetGrid(bool grid)
		{
			s_grid = grid;
			_list.BeginUpdate();
			try
			{
				_list.View = grid ? View.LargeIcon : View.Details;
			}
			finally
			{
				_list.EndUpdate();
			}
			_gridToggle.Text = grid ? "List view" : "Grid view";
			if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].EnsureVisible();
		}

		/// <summary>
		/// Renders one missing thumbnail per tick (visible items first), on a background task.
		/// </summary>
		private async void ThumbTick()
		{
			if (_thumbBusy || IsDisposed || _list.View != View.LargeIcon || _list.Items.Count == 0) return;

			ListViewItem target = null;
			var height = _list.ClientSize.Height;
			for (var i = FirstVisibleIndex(); i < _list.Items.Count; i++)
			{
				var item = _list.Items[i];
				if (item.Bounds.Top > height) break;

				var file = item.Tag as IFile;
				if (file == null) continue;
				var key = file.FullPathName;
				if (_thumbHave.Contains(key) || _thumbFailed.Contains(key)) continue;

				target = item;
				break;
			}
			if (target == null) return;

			var f = (IFile) target.Tag;
			var thumbKey = f.FullPathName;
			_thumbBusy = true;
			Bitmap bmp = null;
			try
			{
				await Task.Run(() =>
				{
					try
					{
						if (!MdlFile.CanRead(f)) return;
						var mdl = MdlFile.FromFile(f);
						bmp = ModelThumbnailRenderer.RenderThumbnail(mdl, ThumbSize, ThumbBackground);
					}
					catch
					{
						// Unreadable model: it just keeps the placeholder
					}
				});
			}
			finally
			{
				_thumbBusy = false;
			}

			if (IsDisposed)
			{
				if (bmp != null) bmp.Dispose();
				return;
			}

			if (bmp == null)
			{
				_thumbFailed.Add(thumbKey);
				return;
			}

			if (_thumbs.Images.Count >= MaxThumbs) ResetThumbs();
			_thumbs.Images.Add(thumbKey, bmp);
			_thumbHave.Add(thumbKey);
			bmp.Dispose();

			// The item may have been replaced by a list refresh while rendering: find it again
			if (target.ListView == _list) target.ImageKey = thumbKey;
			else
			{
				foreach (ListViewItem item in _list.Items)
				{
					var file = item.Tag as IFile;
					if (file != null && file.FullPathName == thumbKey)
					{
						item.ImageKey = thumbKey;
						break;
					}
				}
			}
		}

		/// <summary>
		/// ListView.TopItem throws in the icon views, so probe along the top edge of the list for the first visible item.
		/// </summary>
		private int FirstVisibleIndex()
		{
			var first = int.MaxValue;
			for (var x = 8; x < _list.ClientSize.Width; x += 24)
			{
				var item = _list.GetItemAt(x, 8);
				if (item != null && item.Index < first) first = item.Index;
			}
			return first == int.MaxValue ? 0 : first;
		}

		private void ResetThumbs()
		{
			_thumbs.Images.Clear();
			_thumbs.Images.Add(PlaceholderKey, _placeholder);
			_thumbHave.Clear();
			foreach (ListViewItem item in _list.Items) item.ImageKey = PlaceholderKey;
		}

		#endregion

		#region Preview

		private void ClearPreview(string message)
		{
			_loadVersion++;
			_viewer.SetMessage(message);
			_info.Text = "";
			SetControlsEnabled(false);
		}

		private void SetControlsEnabled(bool enabled)
		{
			_suppressControls = true;
			try
			{
				if (!enabled)
				{
					_skin.Enabled = false;
					_sequence.Items.Clear();
					_sequence.Enabled = false;
					_play.Enabled = false;
					_frame.Enabled = false;
					_frameLabel.Text = "";
					ClearBodyGroups();
				}
			}
			finally
			{
				_suppressControls = false;
			}
		}

		private async void ShowModel(IFile file)
		{
			var version = ++_loadVersion;
			_viewer.SetMessage("Loading...");
			_info.Text = "";
			SetControlsEnabled(false);

			MdlFile mdl = null;
			string info = null;
			string error = null;

			await Task.Run(() =>
			{
				try
				{
					if (!MdlFile.CanRead(file))
					{
						error = "Unsupported or unreadable model file";
						return;
					}
					mdl = MdlFile.FromFile(file);
					info = Describe(file, mdl);
				}
				catch (Exception ex)
				{
					error = "Unable to load model";
					Log.Error(nameof(ModelBrowserDialog), "Unable to load model " + file.FullPathName, ex);
				}
			});

			if (IsDisposed || version != _loadVersion) return;

			if (mdl == null)
			{
				_viewer.SetMessage(error ?? "Unable to load model");
				_info.Text = GetGamePath(file) + System.Environment.NewLine + FormatSize(file.Size);
				return;
			}

			_info.Text = info;
			_viewer.SetModel(mdl);
			PopulateControls(mdl);
			_viewer.Playing = _play.Checked && _viewer.FrameCount > 1;
		}

		private void PopulateControls(MdlFile mdl)
		{
			_suppressControls = true;
			try
			{
				// Skins
				var skins = mdl.Skins != null ? mdl.Skins.Count : 0;
				_skin.Value = 0;
				_skin.Maximum = Math.Max(0, skins - 1);
				_skin.Enabled = skins > 1;

				// Sequences
				_sequence.BeginUpdate();
				_sequence.Items.Clear();
				var seqCount = mdl.Sequences != null ? mdl.Sequences.Count : 0;
				for (var i = 0; i < seqCount; i++)
				{
					var h = mdl.Sequences[i].Header;
					_sequence.Items.Add(i + ": " + (h.Name ?? "").TrimEnd('\0') + " (" + h.NumFrames + " frames)");
				}
				_sequence.EndUpdate();
				_sequence.Enabled = seqCount > 0;
				if (seqCount > 0) _sequence.SelectedIndex = 0;

				// Bodygroups
				ClearBodyGroups();
				if (mdl.BodyParts != null)
				{
					for (var p = 0; p < mdl.BodyParts.Count; p++)
					{
						var part = mdl.BodyParts[p];
						if (part.Models == null || part.Models.Length < 2) continue;

						var partIndex = p;
						var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
						for (var m = 0; m < part.Models.Length; m++)
						{
							var name = (part.Models[m].Header.Name ?? "").TrimEnd('\0');
							combo.Items.Add(m + (name.Length > 0 ? ": " + name : ""));
						}
						combo.SelectedIndex = 0;
						combo.SelectedIndexChanged += (s, e) =>
						{
							if (!_suppressControls) _viewer.SetBodyModel(partIndex, combo.SelectedIndex);
						};

						var title = (part.Header.Name ?? "").TrimEnd('\0');
						_bodyPanel.Controls.Add(MakeLabel((title.Length > 0 ? title : "Bodypart " + p) + ":"));
						_bodyPanel.Controls.Add(combo);
					}
				}
				_bodyPanel.Visible = _bodyPanel.Controls.Count > 0;

				UpdateFrameControls();
			}
			finally
			{
				_suppressControls = false;
			}
		}

		private void ClearBodyGroups()
		{
			var old = _bodyPanel.Controls.Cast<Control>().ToList();
			_bodyPanel.Controls.Clear();
			foreach (var c in old) c.Dispose();
			_bodyPanel.Visible = false;
		}

		private void UpdateFrameControls()
		{
			var wasSuppressed = _suppressControls;
			_suppressControls = true;
			try
			{
				var count = _viewer.FrameCount;
				_frame.Maximum = Math.Max(0, count - 1);
				_frame.Value = Math.Max(0, Math.Min(_viewer.Frame, _frame.Maximum));
				_frame.Enabled = count > 1;
				_play.Enabled = count > 1;
				UpdateFrameLabel();
			}
			finally
			{
				_suppressControls = wasSuppressed;
			}
		}

		private void SetFrameControl(int frame)
		{
			var wasSuppressed = _suppressControls;
			_suppressControls = true;
			try
			{
				_frame.Value = Math.Max(0, Math.Min(frame, _frame.Maximum));
				UpdateFrameLabel();
			}
			finally
			{
				_suppressControls = wasSuppressed;
			}
		}

		private void UpdateFrameLabel()
		{
			var count = _viewer.FrameCount;
			_frameLabel.Text = count > 0 ? "Frame " + _frame.Value + " / " + (count - 1) : "";
		}

		private static string Describe(IFile file, MdlFile mdl)
		{
			var ci = CultureInfo.InvariantCulture;
			Func<float, string> f = x => x.ToString("0.#", ci);
			var sb = new StringBuilder();

			sb.AppendLine(GetGamePath(file));
			sb.AppendLine("File size: " + FormatSize(file.Size));
			if (!string.IsNullOrWhiteSpace(mdl.Header.Name)) sb.AppendLine("Internal name: " + mdl.Header.Name.TrimEnd('\0'));
			sb.AppendLine();

			var bounds = ModelThumbnailRenderer.GetBounds(mdl);
			if (bounds.HasValue)
			{
				var size = bounds.Value.max - bounds.Value.min;
				sb.AppendLine("Dimensions: " + f(size.X) + " x " + f(size.Y) + " x " + f(size.Z) + " units");
				sb.AppendLine("Triangles: " + bounds.Value.triangles.ToString(ci));
			}
			else
			{
				sb.AppendLine("Dimensions: (no geometry)");
			}

			var hullSize = mdl.Header.HullMax - mdl.Header.HullMin;
			sb.AppendLine("Hull: " + f(hullSize.X) + " x " + f(hullSize.Y) + " x " + f(hullSize.Z));
			sb.AppendLine();

			sb.AppendLine("Bodyparts: " + (mdl.BodyParts?.Count ?? 0));
			if (mdl.BodyParts != null)
			{
				foreach (var bp in mdl.BodyParts.Take(12))
				{
					sb.AppendLine("  " + (bp.Header.Name ?? "").TrimEnd('\0') + " (" + (bp.Models?.Length ?? 0) + " models)");
				}
				if (mdl.BodyParts.Count > 12) sb.AppendLine("  ...");
			}

			sb.AppendLine("Skins: " + (mdl.Skins?.Count ?? 0));
			sb.AppendLine("Bones: " + (mdl.Bones?.Count ?? 0));
			sb.AppendLine("Hitboxes: " + (mdl.Hitboxes?.Count ?? 0));
			sb.AppendLine("Attachments: " + (mdl.Attachments?.Count ?? 0));
			sb.AppendLine();

			sb.AppendLine("Textures: " + (mdl.Textures?.Count ?? 0));
			if (mdl.Textures != null)
			{
				foreach (var t in mdl.Textures.Take(16))
				{
					sb.AppendLine("  " + (t.Header.Name ?? "").TrimEnd('\0') + "  " + t.Header.Width + "x" + t.Header.Height);
				}
				if (mdl.Textures.Count > 16) sb.AppendLine("  ...");
			}
			sb.AppendLine();

			sb.AppendLine("Sequences: " + (mdl.Sequences?.Count ?? 0));
			if (mdl.Sequences != null)
			{
				for (var i = 0; i < mdl.Sequences.Count && i < 24; i++)
				{
					var h = mdl.Sequences[i].Header;
					sb.AppendLine("  " + i + ": " + (h.Name ?? "").TrimEnd('\0') + " (" + h.NumFrames + " frames)");
				}
				if (mdl.Sequences.Count > 24) sb.AppendLine("  ...");
			}

			return sb.ToString();
		}


		#endregion
	}
}
