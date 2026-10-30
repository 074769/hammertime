using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser;
using Sledge.Common.Logging;
using Sledge.FileSystem;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>
	/// A browser for .spr sprites: folder tree, search across all folders, a list or thumbnail grid, and a preview
	/// (zoom, pan, background, frame stepping and playback) with sprite information. The "Texture browser" button hands
	/// over to the WAD-style texture browser (the dialog closes with <see cref="DialogResult.Retry"/> and
	/// <see cref="SwitchRequested"/> set).
	/// </summary>
	public class SpriteBrowserDialog : Form
	{
		private const string CacheName = "SpriteThumbnails";
		private const int SearchLimit = 5000;
		private const int MaxThumbs = 1500;
		private const string PlaceholderKey = "__placeholder";
		private static readonly Color ThumbBackground = Color.FromArgb(64, 66, 72);
		private static readonly int[] ThumbSizes = { 64, 96, 128, 160 };
		private static readonly int[] ZoomValues = { 0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

		/// <summary>The sprite that was chosen, or null.</summary>
		public IFile SelectedFile { get; private set; }

		/// <summary>The game path of the chosen sprite (e.g. "sprites/glow01.spr"), or null.</summary>
		public string SelectedPath => SelectedFile == null ? null : GetGamePath(SelectedFile);

		/// <summary>True if the user asked for the texture browser instead.</summary>
		public bool SwitchRequested { get; private set; }

		private readonly IFile _root;
		private readonly IFile _spritesRoot;
		private readonly bool _hasSpritesFolder;

		private readonly TextBox _search;
		private readonly Label _count;
		private readonly Button _gridToggle;
		private readonly Button _switchButton;
		private readonly ComboBox _thumbSize;
		private readonly Panel _top;
		private readonly Panel _bottom;
		private readonly SplitContainer _outer;
		private readonly SplitContainer _inner;
		private readonly SplitContainer _previewSplit;
		private readonly TreeView _tree;
		private readonly ListView _list;
		private readonly ImageList _thumbs;
		private readonly SpritePreviewControl _viewer;
		private readonly ComboBox _background;
		private readonly ComboBox _zoom;
		private readonly CheckBox _outline;
		private readonly CheckBox _play;
		private readonly NumericUpDown _fps;
		private readonly TrackBar _frame;
		private readonly Label _frameLabel;
		private readonly Button _expand;
		private readonly TextBox _info;
		private readonly TextBox _pathBox;
		private readonly Button _ok;
		private readonly Button _cancel;
		private readonly Timer _previewTimer;
		private readonly Timer _workTimer;
		private readonly WheelFilter _wheelFilter;

		private List<(IFile File, string Path)> _index;
		private bool _suppressSearchEvent;
		private bool _suppressTreeEvent;
		private bool _suppressControls;

		private int _loadVersion;
		private IFile _pendingPreview;

		private readonly HashSet<string> _thumbHave = new HashSet<string>();
		private readonly HashSet<string> _thumbFailed = new HashSet<string>();
		private readonly Dictionary<string, SprHeader> _headers = new Dictionary<string, SprHeader>(); // null value = unreadable
		private Bitmap _placeholder;
		private int _thumbPixels;
		private bool _workBusy;

		private int _sortColumn = -1;
		private bool _sortAscending = true;

		public SpriteBrowserDialog(IFile root, string currentValue)
		{
			_root = root;
			var sprites = root?.TraversePath("sprites");
			_hasSpritesFolder = sprites != null && sprites.IsContainer;
			_spritesRoot = _hasSpritesFolder ? sprites : root;

			SuspendLayout();

			Text = "Sprite Browser";
			StartPosition = FormStartPosition.CenterParent;
			ShowInTaskbar = false;
			MinimizeBox = false;
			MaximizeBox = true;
			Size = new Size(1180, 760);
			MinimumSize = new Size(860, 520);

			// Top bar: the path of the selected sprite, the texture browser switch, and the grid / list toggle
			_top = new Panel { Dock = DockStyle.Top, Height = 34 };
			_pathBox = new TextBox { ReadOnly = true, Left = 8, Top = 7 };
			_switchButton = new Button { Text = "Texture browser", Top = 5, Width = 112, Height = 24 };
			_switchButton.Click += (s, e) =>
			{
				SwitchRequested = true;
				DialogResult = DialogResult.Retry;
				Close();
			};
			_gridToggle = new Button { Top = 5, Width = 86, Height = 24 };
			_gridToggle.Click += (s, e) => SetGrid(!SpriteBrowserSettings.GridView);
			_top.Controls.Add(_pathBox);
			_top.Controls.Add(_switchButton);
			_top.Controls.Add(_gridToggle);
			_top.Resize += (s, e) => LayoutBars();

			// Bottom bar: search, count, thumbnail size, OK / Cancel
			_bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };
			_cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
			_ok = new Button { Text = "OK", Width = 90, Enabled = false };
			_ok.Click += (s, e) => Confirm();
			var searchLabel = new Label { Text = "Search:", AutoSize = true, Left = 8, Top = 14 };
			_search = new TextBox { Left = 62, Top = 11, Width = 260 };
			_search.TextChanged += (s, e) =>
			{
				if (!_suppressSearchEvent) RefreshList(null);
			};
			_count = new Label { AutoSize = true, Left = 334, Top = 14, Text = "" };
			var thumbLabel = new Label { Text = "Thumbnails:", AutoSize = true, Top = 14, Name = "thumbLabel" };
			_thumbSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, Top = 10, Name = "thumbSize" };
			foreach (var t in ThumbSizes) _thumbSize.Items.Add(t.ToString(CultureInfo.InvariantCulture));
			var savedIndex = Array.IndexOf(ThumbSizes, SpriteBrowserSettings.ThumbSize);
			_thumbSize.SelectedIndex = savedIndex >= 0 ? savedIndex : 1;
			_thumbSize.SelectedIndexChanged += (s, e) => ChangeThumbSize();
			_bottom.Controls.Add(searchLabel);
			_bottom.Controls.Add(_search);
			_bottom.Controls.Add(_count);
			_bottom.Controls.Add(thumbLabel);
			_bottom.Controls.Add(_thumbSize);
			_bottom.Controls.Add(_ok);
			_bottom.Controls.Add(_cancel);
			_bottom.Resize += (s, e) => LayoutBars();
			AcceptButton = _ok;
			CancelButton = _cancel;

			// Tree | (list | preview)
			_outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
			_inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
			_outer.Panel2.Controls.Add(_inner);

			_tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true, PathSeparator = "/" };
			_tree.BeforeExpand += (s, e) => PopulateNode(e.Node);
			_tree.AfterSelect += TreeAfterSelect;
			_outer.Panel1.Controls.Add(_tree);

			// Thumbnails for the grid view
			_thumbPixels = CurrentThumbSize();
			_placeholder = MakePlaceholder(_thumbPixels);
			_thumbs = new ImageList { ImageSize = new Size(_thumbPixels, _thumbPixels), ColorDepth = ColorDepth.Depth32Bit };
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
			_list.Columns.Add("Name", SpriteBrowserSettings.ColumnNameWidth > 20 ? SpriteBrowserSettings.ColumnNameWidth : 170);
			_list.Columns.Add("Folder", SpriteBrowserSettings.ColumnFolderWidth > 20 ? SpriteBrowserSettings.ColumnFolderWidth : 120);
			_list.Columns.Add("Size", SpriteBrowserSettings.ColumnSizeWidth > 20 ? SpriteBrowserSettings.ColumnSizeWidth : 65, HorizontalAlignment.Right);
			_list.Columns.Add("Dimensions", SpriteBrowserSettings.ColumnDimensionsWidth > 20 ? SpriteBrowserSettings.ColumnDimensionsWidth : 85, HorizontalAlignment.Right);
			_list.Columns.Add("Frames", SpriteBrowserSettings.ColumnFramesWidth > 20 ? SpriteBrowserSettings.ColumnFramesWidth : 55, HorizontalAlignment.Right);
			_sortColumn = SpriteBrowserSettings.SortColumn;
			_sortAscending = SpriteBrowserSettings.SortAscending;
			_list.SelectedIndexChanged += ListSelectionChanged;
			_list.DoubleClick += (s, e) => { if (_list.SelectedItems.Count == 1) Confirm(); };
			_list.ColumnClick += ListColumnClick;
			_inner.Panel1.Controls.Add(_list);

			// Preview panel: options on top, viewer in the middle, playback bar below it, details at the bottom
			var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
			_previewSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };

			_viewer = new SpritePreviewControl { Dock = DockStyle.Fill };
			_viewer.FrameChanged += (s, f) => SetFrameControl(f);
			_viewer.Background = SpriteBrowserSettings.PreviewBackground;
			_viewer.Zoom = SpriteBrowserSettings.PreviewZoom;
			_viewer.Fps = SpriteBrowserSettings.PreviewFps;
			_viewer.ShowOutline = SpriteBrowserSettings.PreviewOutline;

			var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
			_background = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 78 };
			_background.Items.AddRange(SpritePreviewControl.BackgroundNames.Cast<object>().ToArray());
			_background.SelectedIndex = Math.Max(0, Math.Min(SpriteBrowserSettings.PreviewBackground, _background.Items.Count - 1));
			_background.SelectedIndexChanged += (s, e) =>
			{
				if (!_suppressControls) _viewer.Background = _background.SelectedIndex;
			};
			_zoom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62 };
			foreach (var z in ZoomValues) _zoom.Items.Add(z == 0 ? "Fit" : z + "x");
			_zoom.SelectedIndex = Math.Max(0, Array.IndexOf(ZoomValues, SpriteBrowserSettings.PreviewZoom));
			_viewer.ZoomChanged += (s, z) => SetZoomControl(z); // after the zoom box exists
			_zoom.SelectedIndexChanged += (s, e) =>
			{
				if (!_suppressControls && _zoom.SelectedIndex >= 0) _viewer.Zoom = ZoomValues[_zoom.SelectedIndex];
			};
			_outline = new CheckBox { Text = "Outline", AutoSize = true, Checked = SpriteBrowserSettings.PreviewOutline, Margin = new Padding(8, 5, 0, 0) };
			_outline.CheckedChanged += (s, e) => _viewer.ShowOutline = _outline.Checked;
			var reset = new Button { Text = "Reset view", AutoSize = true };
			reset.Click += (s, e) => _viewer.ResetView();
			_expand = new Button { Text = "Expand viewer", AutoSize = true };
			_expand.Click += (s, e) => ToggleExpand();

			bar.Controls.Add(MakeLabel("Background:"));
			bar.Controls.Add(_background);
			bar.Controls.Add(MakeLabel("Zoom:"));
			bar.Controls.Add(_zoom);
			bar.Controls.Add(_outline);
			bar.Controls.Add(reset);
			bar.Controls.Add(_expand);

			// Playback bar: [Play] [---- frame slider ----] Frame 3 / 7 [FPS 10]
			var frameBar = new Panel { Dock = DockStyle.Bottom, Height = 32 };
			_frame = new TrackBar { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 0, Enabled = false, AutoSize = false };
			_frame.ValueChanged += (s, e) =>
			{
				if (_suppressControls) return;
				_suppressControls = true;
				_play.Checked = false;
				_suppressControls = false;
				_viewer.Playing = false;
				_viewer.SetFrame(_frame.Value);
				UpdateFrameLabel();
			};
			_frameLabel = new Label { Dock = DockStyle.Right, Width = 92, TextAlign = ContentAlignment.MiddleRight, Text = "" };
			var fpsPanel = new Panel { Dock = DockStyle.Right, Width = 92 };
			_fps = new NumericUpDown { Left = 38, Top = 5, Width = 50, Minimum = 1, Maximum = 60, Value = Math.Max(1, Math.Min(60, SpriteBrowserSettings.PreviewFps)) };
			_fps.ValueChanged += (s, e) => _viewer.Fps = (int) _fps.Value;
			fpsPanel.Controls.Add(new Label { Text = "FPS", AutoSize = true, Left = 4, Top = 9 });
			fpsPanel.Controls.Add(_fps);
			_play = new CheckBox { Text = "Play", Appearance = Appearance.Button, Dock = DockStyle.Left, Width = 64, Enabled = false, TextAlign = ContentAlignment.MiddleCenter, Checked = SpriteBrowserSettings.PreviewAutoPlay };
			_play.CheckedChanged += (s, e) =>
			{
				_play.Text = _play.Checked ? "Pause" : "Play";
				if (!_suppressControls) _viewer.Playing = _play.Checked;
			};
			_play.Text = _play.Checked ? "Pause" : "Play";
			frameBar.Controls.Add(_frame);
			frameBar.Controls.Add(_frameLabel);
			frameBar.Controls.Add(fpsPanel);
			frameBar.Controls.Add(_play);

			// Dock order: the last added control is laid out first
			_previewSplit.Panel1.Controls.Add(_viewer);
			_previewSplit.Panel1.Controls.Add(frameBar);
			_previewSplit.Panel1.Controls.Add(bar);

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
			_previewSplit.Panel2.Controls.Add(_info);

			preview.Controls.Add(_previewSplit);
			_inner.Panel2.Controls.Add(preview);

			Controls.Add(_outer);
			Controls.Add(_top);
			Controls.Add(_bottom);
			LayoutBars();

			_previewTimer = new Timer { Interval = 150 };
			_previewTimer.Tick += (s, e) =>
			{
				_previewTimer.Stop();
				var f = _pendingPreview;
				_pendingPreview = null;
				if (f != null) ShowSprite(f);
			};

			_workTimer = new Timer { Interval = 80 };
			_workTimer.Tick += (s, e) => WorkTick();
			_workTimer.Start();

			_wheelFilter = new WheelFilter(this);

			SetGrid(SpriteBrowserSettings.GridView);
			ApplySavedWindow();
			ResumeLayout(true);

			Load += (s, e) =>
			{
				try
				{
					var treeWidth = SpriteBrowserSettings.TreeWidth > 0 ? SpriteBrowserSettings.TreeWidth : 190;
					_outer.SplitterDistance = Math.Max(_outer.Panel1MinSize, Math.Min(treeWidth, Math.Max(_outer.Panel1MinSize, _outer.Width - 300)));

					var previewWidth = SpriteBrowserSettings.PreviewWidth > 0 ? SpriteBrowserSettings.PreviewWidth : (int) (_inner.Width * 0.5);
					previewWidth = Math.Max(200, Math.Min(previewWidth, _inner.Width - 200));
					_inner.SplitterDistance = Math.Max(_inner.Panel1MinSize, _inner.Width - previewWidth);

					_previewSplit.Panel1MinSize = 200;
					_previewSplit.Panel2MinSize = 60;
					var infoHeight = SpriteBrowserSettings.InfoHeight > 0 ? SpriteBrowserSettings.InfoHeight : 150;
					infoHeight = Math.Max(60, Math.Min(infoHeight, _previewSplit.Height - 200));
					_previewSplit.SplitterDistance = Math.Max(_previewSplit.Panel1MinSize, _previewSplit.Height - infoHeight);
				}
				catch (Exception ex)
				{
					Log.Error(nameof(SpriteBrowserDialog), "Unable to set splitter distance", ex);
				}
				BuildTree(currentValue);
			};
		}

		private static Label MakeLabel(string text)
		{
			return new Label { Text = text, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(6, 7, 0, 0) };
		}

		private static Bitmap MakePlaceholder(int size)
		{
			var bmp = new Bitmap(size, size);
			using (var g = Graphics.FromImage(bmp))
			using (var brush = new SolidBrush(Color.FromArgb(150, 150, 158)))
			using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
			using (var font = new Font(FontFamily.GenericSansSerif, 10f))
			{
				g.Clear(ThumbBackground);
				g.DrawString("SPR", font, brush, new RectangleF(0, 0, size, size), format);
			}
			return bmp;
		}

		private int CurrentThumbSize()
		{
			var i = _thumbSize.SelectedIndex;
			return i >= 0 && i < ThumbSizes.Length ? ThumbSizes[i] : 96;
		}

		private void LayoutBars()
		{
			if (_top == null || _bottom == null) return;

			// Top: [ path ............ ] [Texture browser] [Grid view]
			_gridToggle.Left = Math.Max(8, _top.ClientSize.Width - _gridToggle.Width - 8);
			_switchButton.Left = Math.Max(8, _gridToggle.Left - _switchButton.Width - 6);
			_pathBox.Width = Math.Max(50, _switchButton.Left - 16);

			// Bottom: Search: [....] 123 sprites ......... Thumbnails: [96] [OK] [Cancel]
			var w = _bottom.ClientSize.Width;
			_cancel.Left = w - _cancel.Width - 8;
			_cancel.Top = 7;
			_ok.Left = _cancel.Left - _ok.Width - 6;
			_ok.Top = 7;
			_thumbSize.Left = _ok.Left - _thumbSize.Width - 16;
			var thumbLabel = _bottom.Controls["thumbLabel"];
			if (thumbLabel != null) thumbLabel.Left = _thumbSize.Left - thumbLabel.Width - 4;
		}

		#region Window state

		private void ApplySavedWindow()
		{
			var w = SpriteBrowserSettings.WindowWidth;
			var h = SpriteBrowserSettings.WindowHeight;
			if (w < MinimumSize.Width || h < MinimumSize.Height) return; // never saved (or nonsense)

			var bounds = new Rectangle(SpriteBrowserSettings.WindowLeft, SpriteBrowserSettings.WindowTop, w, h);
			if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(bounds))) return; // monitor is gone

			StartPosition = FormStartPosition.Manual;
			Bounds = bounds;
			if (SpriteBrowserSettings.WindowMaximized) WindowState = FormWindowState.Maximized;
		}

		private void SaveState()
		{
			try
			{
				var maximized = WindowState == FormWindowState.Maximized;
				var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
				if (WindowState != FormWindowState.Minimized)
				{
					SpriteBrowserSettings.WindowLeft = normal.Left;
					SpriteBrowserSettings.WindowTop = normal.Top;
					SpriteBrowserSettings.WindowWidth = normal.Width;
					SpriteBrowserSettings.WindowHeight = normal.Height;
					SpriteBrowserSettings.WindowMaximized = maximized;
				}

				// Splitters can't be measured while the "expand viewer" mode has panels collapsed
				if (!_outer.Panel1Collapsed && !_inner.Panel1Collapsed)
				{
					SpriteBrowserSettings.TreeWidth = _outer.SplitterDistance;
					SpriteBrowserSettings.PreviewWidth = _inner.Width - _inner.SplitterDistance;
					SpriteBrowserSettings.InfoHeight = _previewSplit.Height - _previewSplit.SplitterDistance;
				}

				SpriteBrowserSettings.GridView = _list.View == View.LargeIcon;
				SpriteBrowserSettings.ThumbSize = CurrentThumbSize();
				SpriteBrowserSettings.ColumnNameWidth = _list.Columns[0].Width;
				SpriteBrowserSettings.ColumnFolderWidth = _list.Columns[1].Width;
				SpriteBrowserSettings.ColumnSizeWidth = _list.Columns[2].Width;
				SpriteBrowserSettings.ColumnDimensionsWidth = _list.Columns[3].Width;
				SpriteBrowserSettings.ColumnFramesWidth = _list.Columns[4].Width;
				SpriteBrowserSettings.SortColumn = _sortColumn;
				SpriteBrowserSettings.SortAscending = _sortAscending;

				SpriteBrowserSettings.PreviewBackground = _background.SelectedIndex < 0 ? 0 : _background.SelectedIndex;
				SpriteBrowserSettings.PreviewZoom = _viewer.Zoom;
				SpriteBrowserSettings.PreviewFps = (int) _fps.Value;
				SpriteBrowserSettings.PreviewAutoPlay = _play.Checked;
				SpriteBrowserSettings.PreviewOutline = _outline.Checked;

				var node = _tree.SelectedNode;
				if (node != null && _search.Text.Trim().Length == 0)
				{
					var parts = new List<string>();
					for (var n = node; n != null && n.Parent != null; n = n.Parent) parts.Insert(0, n.Text);
					SpriteBrowserSettings.LastFolder = string.Join("/", parts);
				}

				// Write it to disk now rather than waiting for the editor to shut down
				LogicAndTrick.Oy.Oy.Publish("Settings:Save");
			}
			catch (Exception ex)
			{
				Log.Error(nameof(SpriteBrowserDialog), "Unable to save sprite browser state", ex);
			}
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			SaveState();
			base.OnFormClosing(e);
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
				_workTimer?.Dispose();
				_thumbs?.Dispose();
				_placeholder?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <summary>Routes the mouse wheel to the preview while the cursor is over it, whichever control has the keyboard focus.</summary>
		private class WheelFilter : IMessageFilter
		{
			private readonly SpriteBrowserDialog _owner;

			public WheelFilter(SpriteBrowserDialog owner)
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

		#endregion

		#region Tree

		private void BuildTree(string currentValue)
		{
			_tree.BeginUpdate();
			try
			{
				_tree.Nodes.Clear();
				var rootNode = new TreeNode(_hasSpritesFolder ? "sprites" : "(game root)") { Tag = _spritesRoot };
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
			var useLastFolder = string.IsNullOrEmpty(currentValue) && !string.IsNullOrEmpty(SpriteBrowserSettings.LastFolder);
			if (_hasSpritesFolder && segments.Count > 0 && string.Equals(segments[0], "sprites", StringComparison.OrdinalIgnoreCase)) segments.RemoveAt(0);
			else if (_hasSpritesFolder && segments.Count == 0 && fileName != null && string.Equals(fileName, "sprites", StringComparison.OrdinalIgnoreCase)) fileName = null;

			if (useLastFolder)
			{
				// Nothing selected yet: reopen where the user left off (relative to the sprites folder)
				segments = SpriteBrowserSettings.LastFolder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
			}

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
				Log.Error(nameof(SpriteBrowserDialog), "Unable to list folder " + folder.FullPathName, ex);
			}
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

		private static bool IsSpr(IFile f)
		{
			return string.Equals(f.Extension, "spr", StringComparison.OrdinalIgnoreCase);
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
					foreach (var f in _spritesRoot.GetFiles(true))
					{
						if (!IsSpr(f)) continue;
						_index.Add((f, GetGamePath(f)));
					}
				}
				catch (Exception ex)
				{
					Log.Error(nameof(SpriteBrowserDialog), "Unable to index sprites", ex);
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
				var folder = _tree.SelectedNode?.Tag as IFile ?? _spritesRoot;
				try
				{
					foreach (var f in folder.GetFiles().Where(IsSpr).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
					{
						items.Add(MakeItem(f, ""));
					}
				}
				catch (Exception ex)
				{
					Log.Error(nameof(SpriteBrowserDialog), "Unable to list sprites", ex);
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

			SortList();

			_count.Text = items.Count + (truncated ? "+" : "") + (items.Count == 1 ? " sprite" : " sprites") + (query.Length > 0 ? " (all folders)" : "");

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
			item.SubItems.Add("");
			item.SubItems.Add("");
			if (_headers.TryGetValue(file.FullPathName, out var header)) FillHeaderColumns(item, header);
			return item;
		}

		private static void FillHeaderColumns(ListViewItem item, SprHeader header)
		{
			if (header == null)
			{
				item.SubItems[3].Text = "?";
				item.SubItems[4].Text = "?";
				return;
			}
			item.SubItems[3].Text = header.Width + " x " + header.Height;
			item.SubItems[4].Text = header.NumFrames.ToString(CultureInfo.InvariantCulture);
		}

		private static string FormatSize(long bytes)
		{
			if (bytes < 1024) return bytes + " B";
			if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
			return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
		}

		private void ListColumnClick(object sender, ColumnClickEventArgs e)
		{
			if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
			else
			{
				_sortColumn = e.Column;
				_sortAscending = true;
			}

			SortList();
		}

		private long HeaderKey(ListViewItem item, int column)
		{
			var file = (IFile) item.Tag;
			if (!_headers.TryGetValue(file.FullPathName, out var h) || h == null) return -1;
			return column == 3 ? (long) h.Width * h.Height : h.NumFrames;
		}

		private void SortList()
		{
			if (_sortColumn < 0 || _sortColumn >= _list.Columns.Count) return;

			var column = _sortColumn;
			var items = _list.Items.Cast<ListViewItem>().ToList();
			Func<ListViewItem, object> key;
			if (column == 2) key = x => ((IFile) x.Tag).Size;
			else if (column == 3 || column == 4) key = x => HeaderKey(x, column);
			else key = x => x.SubItems[column].Text.ToLowerInvariant();

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
				if (_list.Items.Count == 0 || _list.SelectedItems.Count == 0) ClearPreview("Select a sprite to preview");
				return;
			}

			var file = (IFile) _list.SelectedItems[0].Tag;
			_ok.Enabled = true;
			_pathBox.Text = GetGamePath(file);

			// Debounce so holding an arrow key doesn't load every sprite it passes over
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

		#region Grid view / thumbnails / headers

		private void SetGrid(bool grid)
		{
			SpriteBrowserSettings.GridView = grid;
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
			_thumbSize.Enabled = grid;
			if (_list.SelectedItems.Count > 0) _list.SelectedItems[0].EnsureVisible();
		}

		private void ChangeThumbSize()
		{
			var size = CurrentThumbSize();
			if (size == _thumbPixels) return;
			_thumbPixels = size;
			SpriteBrowserSettings.ThumbSize = size;

			_placeholder?.Dispose();
			_placeholder = MakePlaceholder(size);
			_thumbs.Images.Clear();
			_thumbs.ImageSize = new Size(size, size); // changing the size empties the list
			_thumbs.Images.Add(PlaceholderKey, _placeholder);
			_thumbHave.Clear();
			_thumbFailed.Clear();
			foreach (ListViewItem item in _list.Items) item.ImageKey = PlaceholderKey;
		}

		/// <summary>
		/// Fills in what is missing for the visible items (visible items first), one after another on a background task:
		/// the header (dimensions and frame count columns) and, in the grid, the thumbnail. Thumbnails come from the
		/// persistent disk cache when the sprite hasn't changed, otherwise they are rendered and cached.
		/// </summary>
		private async void WorkTick()
		{
			if (_workBusy) return;
			_workBusy = true;
			try
			{
				while (!IsDisposed)
				{
					var target = NextVisibleNeedingWork(out var needHeader, out var needThumb);
					if (target == null) break;

					var file = (IFile) target.Tag;
					var key = file.FullPathName;
					var size = _thumbPixels;
					SprHeader header = null;
					Bitmap bmp = null;

					await Task.Run(() =>
					{
						try
						{
							if (needHeader) header = SprFile.ReadHeader(file);
							if (needThumb)
							{
								bmp = ThumbnailCache.GetOrCreate(CacheName, file, size, () =>
								{
									var spr = SprFile.Load(file);
									if (spr == null) return null;
									using (spr) return spr.RenderThumbnail(size, ThumbBackground);
								});
							}
						}
						catch
						{
							// Unreadable sprite: it just keeps the placeholder
						}
					});

					if (IsDisposed)
					{
						bmp?.Dispose();
						return;
					}

					if (needHeader)
					{
						_headers[key] = header;
						foreach (ListViewItem item in _list.Items)
						{
							if (item.Tag is IFile f && f.FullPathName == key) FillHeaderColumns(item, header);
						}
					}

					if (needThumb)
					{
						if (bmp == null)
						{
							_thumbFailed.Add(key);
						}
						else if (size == _thumbPixels)
						{
							if (_thumbs.Images.Count >= MaxThumbs) ResetThumbs();
							_thumbs.Images.Add(key, bmp);
							_thumbHave.Add(key);
							foreach (ListViewItem item in _list.Items)
							{
								if (item.Tag is IFile f && f.FullPathName == key)
								{
									item.ImageKey = key;
									break;
								}
							}
						}
						bmp?.Dispose();
					}
				}
			}
			finally
			{
				_workBusy = false;
			}
		}

		private ListViewItem NextVisibleNeedingWork(out bool needHeader, out bool needThumb)
		{
			needHeader = false;
			needThumb = false;
			if (_list.Items.Count == 0) return null;

			var grid = _list.View == View.LargeIcon;
			var height = _list.ClientSize.Height;
			for (var i = FirstVisibleIndex(); i < _list.Items.Count; i++)
			{
				var item = _list.Items[i];
				if (item.Bounds.Top > height) break;

				var file = item.Tag as IFile;
				if (file == null) continue;
				var key = file.FullPathName;

				needHeader = !_headers.ContainsKey(key);
				needThumb = grid && !_thumbHave.Contains(key) && !_thumbFailed.Contains(key);
				if (needHeader || needThumb) return item;
			}
			return null;
		}

		/// <summary>
		/// ListView.TopItem throws in the icon views, so probe along the top edge of the list for the first visible item there.
		/// </summary>
		private int FirstVisibleIndex()
		{
			if (_list.View == View.Details)
			{
				try
				{
					return _list.TopItem?.Index ?? 0;
				}
				catch
				{
					return 0;
				}
			}

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
			if (enabled) return;
			_suppressControls = true;
			try
			{
				_play.Enabled = false;
				_frame.Enabled = false;
				_frameLabel.Text = "";
			}
			finally
			{
				_suppressControls = false;
			}
		}

		private async void ShowSprite(IFile file)
		{
			var version = ++_loadVersion;
			_viewer.SetMessage("Loading...");
			_info.Text = "";
			SetControlsEnabled(false);

			SprFile spr = null;
			string error = null;

			await Task.Run(() =>
			{
				try
				{
					spr = SprFile.Load(file);
					if (spr == null) error = "Unsupported or unreadable sprite file";
				}
				catch (Exception ex)
				{
					error = "Unable to load sprite";
					Log.Error(nameof(SpriteBrowserDialog), "Unable to load sprite " + file.FullPathName, ex);
				}
			});

			if (IsDisposed || version != _loadVersion)
			{
				spr?.Dispose();
				return;
			}

			if (spr == null)
			{
				_viewer.SetMessage(error ?? "Unable to load sprite");
				_info.Text = GetGamePath(file) + System.Environment.NewLine + FormatSize(file.Size);
				return;
			}

			_info.Text = Describe(file, spr);
			_viewer.SetSprite(spr);
			PopulateControls();
			_viewer.Playing = _play.Checked && _viewer.FrameCount > 1;
		}

		private void PopulateControls()
		{
			_suppressControls = true;
			try
			{
				var count = _viewer.FrameCount;
				_frame.Maximum = Math.Max(0, count - 1);
				_frame.Value = 0;
				_frame.Enabled = count > 1;
				_play.Enabled = count > 1;
				UpdateFrameLabel();
			}
			finally
			{
				_suppressControls = false;
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

		private void SetZoomControl(int zoom)
		{
			var wasSuppressed = _suppressControls;
			_suppressControls = true;
			try
			{
				var index = Array.IndexOf(ZoomValues, zoom);
				if (index >= 0) _zoom.SelectedIndex = index;
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

		private static string OrientationName(SpriteOrientation o)
		{
			switch (o)
			{
				case SpriteOrientation.ParallelUpright: return "Parallel upright (faces the camera, turns around Z only)";
				case SpriteOrientation.FacingUpright: return "Facing upright";
				case SpriteOrientation.Parallel: return "Parallel (faces the camera, roll works)";
				case SpriteOrientation.Oriented: return "Oriented (fixed, uses the entity angles)";
				case SpriteOrientation.ParallelOriented: return "Parallel oriented";
				default: return ((int) o).ToString(CultureInfo.InvariantCulture);
			}
		}

		private static string RenderModeName(SpriteRenderMode m)
		{
			switch (m)
			{
				case SpriteRenderMode.Normal: return "Normal (opaque)";
				case SpriteRenderMode.Additive: return "Additive (glow)";
				case SpriteRenderMode.IndexAlpha: return "Index alpha";
				case SpriteRenderMode.AlphaTest: return "Alpha test (index 255 is transparent)";
				default: return ((int) m).ToString(CultureInfo.InvariantCulture);
			}
		}

		private static string Describe(IFile file, SprFile spr)
		{
			var ci = CultureInfo.InvariantCulture;
			var h = spr.Header;
			var sb = new StringBuilder();

			sb.AppendLine(GetGamePath(file));
			sb.AppendLine("File size: " + FormatSize(file.Size));
			sb.AppendLine();
			sb.AppendLine("Dimensions: " + h.Width + " x " + h.Height);
			sb.AppendLine("Frames: " + spr.Frames.Count + (spr.GroupFrames > 0 ? " (" + spr.GroupFrames + " animation group" + (spr.GroupFrames == 1 ? "" : "s") + ")" : ""));
			sb.AppendLine("Render mode: " + RenderModeName(h.RenderMode));
			sb.AppendLine("Orientation: " + OrientationName(h.Orientation));
			sb.AppendLine("Bounding radius: " + h.BoundingRadius.ToString("0.##", ci));
			if (h.BeamLength > 0) sb.AppendLine("Beam length: " + h.BeamLength.ToString("0.##", ci));
			sb.AppendLine("Frame sync: " + (h.SyncType == 0 ? "synchronized" : "random"));
			sb.AppendLine("Palette colours: " + spr.PaletteSize);

			if (spr.Frames.Count > 1)
			{
				var sizes = spr.Frames.Select(f => f.Width + " x " + f.Height).Distinct().ToList();
				if (sizes.Count > 1) sb.AppendLine("Frame sizes: " + string.Join(", ", sizes.Take(6)) + (sizes.Count > 6 ? ", ..." : ""));
				if (spr.HasTiming) sb.AppendLine("Group timing: " + spr.Frames.Where(f => f.Duration > 0).Sum(f => f.Duration).ToString("0.###", ci) + " s");
			}

			return sb.ToString();
		}

		#endregion
	}
}
