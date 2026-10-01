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
	/// A browser for .mdl models: folder tree, search across all folders, a file list,
	/// and a static preview (software rendered thumbnail + model information).
	/// </summary>
	public class ModelBrowserDialog : Form
	{
		private const int PreviewSize = 320;
		private const int SearchLimit = 5000;

		public IFile SelectedFile { get; private set; }

		private readonly IFile _root;
		private readonly IFile _modelsRoot;
		private readonly bool _hasModelsFolder;

		private readonly TextBox _search;
		private readonly Label _count;
		private readonly TreeView _tree;
		private readonly ListView _list;
		private readonly PictureBox _picture;
		private readonly Label _pictureMessage;
		private readonly Button _rotateLeft;
		private readonly Button _rotateRight;
		private readonly NumericUpDown _skin;
		private readonly Label _skinLabel;
		private readonly TextBox _info;
		private readonly TextBox _pathBox;
		private readonly Button _ok;
		private readonly Button _cancel;
		private readonly Timer _previewTimer;

		private List<(IFile File, string Path)> _index;
		private bool _suppressSearchEvent;
		private bool _suppressTreeEvent;

		private MdlFile _mdl;
		private float _azimuth = 35f;
		private int _loadVersion;
		private int _renderVersion;
		private IFile _pendingPreview;

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
			Size = new Size(1040, 660);
			MinimumSize = new Size(820, 480);

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
			_count = new Label { AutoSize = true, Left = 376, Top = 9, Text = "" };
			top.Controls.Add(searchLabel);
			top.Controls.Add(_search);
			top.Controls.Add(_count);

			// Tree | (list | preview)
			var outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
			var inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
			outer.Panel2.Controls.Add(inner);

			_tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true, PathSeparator = "/" };
			_tree.BeforeExpand += TreeBeforeExpand;
			_tree.AfterSelect += TreeAfterSelect;
			outer.Panel1.Controls.Add(_tree);

			_list = new ListView
			{
				Dock = DockStyle.Fill,
				View = View.Details,
				FullRowSelect = true,
				MultiSelect = false,
				HideSelection = false,
				GridLines = false
			};
			_list.Columns.Add("Name", 190);
			_list.Columns.Add("Folder", 150);
			_list.Columns.Add("Size", 70, HorizontalAlignment.Right);
			_list.SelectedIndexChanged += ListSelectionChanged;
			_list.DoubleClick += (s, e) => { if (_list.SelectedItems.Count == 1) Confirm(); };
			_list.ColumnClick += ListColumnClick;
			inner.Panel1.Controls.Add(_list);

			// Preview panel
			var preview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
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
			var controls = new Panel { Dock = DockStyle.Top, Height = 32 };
			_rotateLeft = new Button { Text = "\u25C0", Width = 32, Left = 0, Top = 3, Enabled = false };
			_rotateRight = new Button { Text = "\u25B6", Width = 32, Left = 36, Top = 3, Enabled = false };
			_rotateLeft.Click += (s, e) => Rotate(-45f);
			_rotateRight.Click += (s, e) => Rotate(45f);
			_skinLabel = new Label { Text = "Skin:", AutoSize = true, Left = 90, Top = 9 };
			_skin = new NumericUpDown { Left = 126, Top = 5, Width = 56, Minimum = 0, Maximum = 0, Enabled = false };
			_skin.ValueChanged += (s, e) => RenderCurrent();
			controls.Controls.Add(_rotateLeft);
			controls.Controls.Add(_rotateRight);
			controls.Controls.Add(_skinLabel);
			controls.Controls.Add(_skin);

			var picturePanel = new Panel { Dock = DockStyle.Top, Height = PreviewSize + 2 };
			_picture = new PictureBox
			{
				Dock = DockStyle.Fill,
				SizeMode = PictureBoxSizeMode.Zoom,
				BackColor = Color.FromArgb(64, 66, 72),
				BorderStyle = BorderStyle.FixedSingle
			};
			_pictureMessage = new Label
			{
				Dock = DockStyle.Fill,
				TextAlign = ContentAlignment.MiddleCenter,
				ForeColor = Color.Gainsboro,
				BackColor = Color.Transparent,
				Text = "Select a model to preview"
			};
			picturePanel.Controls.Add(_picture);
			_picture.Controls.Add(_pictureMessage); // draw the message on top of the picture box

			// Dock order: the last added control is laid out first
			preview.Controls.Add(_info);
			preview.Controls.Add(controls);
			preview.Controls.Add(picturePanel);
			inner.Panel2.Controls.Add(preview);

			Controls.Add(outer);
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

			ResumeLayout(true);

			Load += (s, e) =>
			{
				try
				{
					outer.SplitterDistance = Math.Max(outer.Panel1MinSize, 200);
					inner.SplitterDistance = Math.Max(inner.Panel1MinSize, inner.Width - 380);
				}
				catch (Exception ex)
				{
					Log.Error(nameof(ModelBrowserDialog), "Unable to set splitter distance", ex);
				}
				BuildTree(currentValue);
			};
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

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_previewTimer?.Dispose();
				var img = _picture?.Image;
				if (_picture != null) _picture.Image = null;
				img?.Dispose();
			}
			base.Dispose(disposing);
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

		private static ListViewItem MakeItem(IFile file, string folder)
		{
			var item = new ListViewItem(file.Name) { Tag = file };
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

		#region Preview

		private void ClearPreview(string message)
		{
			_loadVersion++;
			_renderVersion++;
			_mdl = null;
			SetImage(null);
			_pictureMessage.Text = message;
			_pictureMessage.Visible = true;
			_info.Text = "";
			_rotateLeft.Enabled = false;
			_rotateRight.Enabled = false;
			_skin.Enabled = false;
		}

		private void SetImage(Image image)
		{
			var old = _picture.Image;
			_picture.Image = image;
			old?.Dispose();
		}

		private async void ShowModel(IFile file)
		{
			var version = ++_loadVersion;
			_renderVersion++;
			_mdl = null;
			SetImage(null);
			_pictureMessage.Text = "Loading...";
			_pictureMessage.Visible = true;
			_info.Text = "";
			_rotateLeft.Enabled = false;
			_rotateRight.Enabled = false;
			_skin.Enabled = false;

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
				_pictureMessage.Text = error ?? "Unable to load model";
				_info.Text = GetGamePath(file) + Environment.NewLine + FormatSize(file.Size);
				return;
			}

			_mdl = mdl;
			_info.Text = info;
			_rotateLeft.Enabled = true;
			_rotateRight.Enabled = true;

			var skins = mdl.Skins?.Count ?? 0;
			_skin.Value = 0;
			_skin.Maximum = Math.Max(0, skins - 1);
			_skin.Enabled = skins > 1;

			RenderCurrent();
		}

		private void Rotate(float degrees)
		{
			_azimuth = (_azimuth + degrees + 360f) % 360f;
			RenderCurrent();
		}

		private async void RenderCurrent()
		{
			var mdl = _mdl;
			if (mdl == null) return;

			var version = ++_renderVersion;
			var skin = (int) _skin.Value;
			var azimuth = _azimuth;
			var background = _picture.BackColor;

			Bitmap bmp = null;
			await Task.Run(() =>
			{
				try
				{
					bmp = ModelThumbnailRenderer.Render(mdl, PreviewSize, background, skin, azimuth);
				}
				catch (Exception ex)
				{
					Log.Error(nameof(ModelBrowserDialog), "Unable to render model preview", ex);
				}
			});

			if (IsDisposed || version != _renderVersion)
			{
				bmp?.Dispose();
				return;
			}

			SetImage(bmp);
			_pictureMessage.Text = "No preview available";
			_pictureMessage.Visible = bmp == null;
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
