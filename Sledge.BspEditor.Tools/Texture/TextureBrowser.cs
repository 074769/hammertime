using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Environment;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Translations;
using Sledge.QuickForms;
using Sledge.Shell;
using Sledge.Shell.Commands;
using Face = Sledge.BspEditor.Primitives.MapObjectData.Face;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TextBox;

namespace Sledge.BspEditor.Tools.Texture
{
	public partial class TextureBrowser : Form, IManualTranslate
	{
		private readonly MapDocument _document;
		private TextureListPanel _textureList;
		private FavouriteTextureEnvironmentCollection _settingsManager;
		public TextureBrowser(MapDocument document)
		{
			_document = document;
			InitializeComponent();
			InitialiseTextureList();

			// Setup memory & other controls
			var sz = GetMemory("SizeMode", 1);
			var so = GetMemory("SortBy", 0);

			SortOrderCombo.Items.Clear();
			SortOrderCombo.Items.Add("Name");
			SortOrderCombo.Enabled = false;
			SortOrderCombo.SelectedIndex = 0;

			FilterTextbox.Text = GetMemory("Filter", "");
			UsedTexturesOnlyBox.Checked = GetMemory("UsedTexturesOnly", false);
			SizeCombo.SelectedIndex = sz;
			SortOrderCombo.SelectedIndex = so;
			SortDescendingCheckbox.Checked = GetMemory("SortDescending", false);

			PackageTree.NodeMouseClick += PackageTreeNodeMouseClick;
			_textureList.TextureSelected += TextureSelected;
			_textureList.HighlightedTexturesChanged += HighlightedTexturesChanged;
			SizeCombo.SelectedIndex = 1;
			_textures = new List<string>();
			SelectedTexture = null;

			HighlightedTexturesChanged(null, _textureList.GetHighlightedTextures());
			var envTex = SettingsManager.GetInstance().FavouriteTextureFolders.FirstOrDefault(x => x.EnvironmentId == document.Environment.ID);
			if (envTex != null)
			{
				_settingsManager = envTex;
				return;
			}
			_settingsManager = new FavouriteTextureEnvironmentCollection { EnvironmentId = document.Environment.ID };
			SettingsManager.GetInstance().FavouriteTextureFolders.Add(_settingsManager);
		}

		private void InitialiseTextureList()
		{
			_textureList = new TextureListPanel
			{
				AllowMultipleHighlighting = true,
				AllowHighlighting = true,
				AutoScroll = true,
				BackColor = Color.Black,
				Dock = DockStyle.Fill,
				EnableDrag = true,
				ImageSize = 128,
				Location = new Point(226, 0),
				Name = "_textureList",
				Size = new Size(714, 495),
				TabIndex = 0
			};
			TextureListPanel.Controls.Add(_textureList);
		}

		public void Translate(ITranslationStringProvider strings)
		{
			CreateHandle();
			var prefix = GetType().FullName;
			this.InvokeLater(() =>
			{
				Text = strings.GetString(prefix, "Title");
				FavouriteTexturesLabel.Text = strings.GetString(prefix, "FavouriteTextures");
				AddFavouriteFolderButton.Text = strings.GetString(prefix, "AddFolder");
				DeleteFavouriteFolderButton.Text = strings.GetString(prefix, "DeleteFolder");
				RemoveFavouriteItemButton.Text = strings.GetString(prefix, "RemoveSelected");

				FilterLabel.Text = strings.GetString(prefix, "Filter");
				SizeLabel.Text = strings.GetString(prefix, "Size");

				UsedTexturesOnlyBox.Text = strings.GetString(prefix, "UsedTexturesOnly");
				SelectButton.Text = strings.GetString(prefix, "Select");
				SortByLabel.Text = strings.GetString(prefix, "SortBy");
				SortDescendingCheckbox.Text = strings.GetString(prefix, "SortDescending");
			});
		}

		public async Task Initialise(ITranslationStringProvider translation)
		{
			// Always open on "All Packages" (don't restore the WAD or favourites folder selected last time)
			SetMemory("SelectedPackage", (string) null);
			SetMemory("SelectedFavourite", (string) null);

			_textureList.Collection = await _document.Environment.GetTextureCollection();

			_textures.Clear();
			_textures.AddRange(_textureList.Collection.GetBrowsableTextures());

			UpdatePackageList();
			UpdateTextureList();
			UpdateFavouritesList();
			_textureList.SortTextureList(x => x, GetMemory("SortDescending", false));

			translation.Translate(this);
		}

		private string _goToTexture;

		/// <summary>
		/// Jump to a texture: switch to a view that contains it, then highlight it and scroll it into view.
		/// </summary>
		public async Task GoToTexture(string name)
		{
			if (String.IsNullOrWhiteSpace(name)) return;

			var tex = _textures.FirstOrDefault(x => String.Equals(x, name, StringComparison.InvariantCultureIgnoreCase));
			if (tex == null) return;

			bool IsListed() => _textureList.GetTextureList().Any(x => String.Equals(x, tex, StringComparison.InvariantCultureIgnoreCase));

			if (!IsListed())
			{
				// Leave the favourites view and go to the package the texture lives in
				FavouritesTree.SelectedNode = null;
				var pkg = _textureList.Collection.Packages.FirstOrDefault(p => p.HasTexture(tex));
				var root = PackageTree.Nodes.Cast<TreeNode>().FirstOrDefault();
				var node = pkg == null || root == null ? null : root.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Name == pkg.ToString());
				PackageTree.SelectedNode = node ?? root;
				await UpdateTextureList();
			}

			if (!IsListed())
			{
				// A filter is hiding it - the selected texture wins
				FilterTextbox.Text = "";
				SetMemory("Filter", "");
				UsedTexturesOnlyBox.Checked = false;
				await UpdateTextureList();
			}

			_goToTexture = tex;
			_textureList.SetHighlightedTextures(new[] { tex });
			_textureList.ScrollToTexture(tex);
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);

			// The list has no size until the dialog is visible, so scroll again now that it has been laid out
			if (_goToTexture != null) _textureList.ScrollToTexture(_goToTexture);
		}

		protected override void OnLoad(EventArgs e)
		{
			FilterTextbox.SelectAll();
			base.OnLoad(e);
		}

		private void HighlightedTexturesChanged(object sender, IEnumerable<string> selection)
		{
			TextureNameLabel.Text = "";
			TextureSizeLabel.Text = "";

			var list = selection.ToList();

			if (list.Count == 1 && _document != null)
			{
				var t = list[0];
				TextureNameLabel.Text = t;
				_document.Environment.GetTextureCollection().ContinueWith(tc =>
				{
					tc.Result.GetTextureItem(t).ContinueWith(ti =>
					{
						this.InvokeLater(() =>
						{
							TextureNameLabel.Text = ti == null ? t : $"{ti.Result.WadName}/{ti.Result.Name}";
							TextureSizeLabel.Text = ti == null ? "" : $@"{ti.Result.Width} x {ti.Result.Height}";
						});
					});
				});
			}
			else if (list.Count > 1)
			{
				TextureNameLabel.Text = list.Count + " textures selected";
				TextureSizeLabel.Text = "";
			}
		}

		public string SelectedTexture { get; set; }
		private readonly List<string> _textures;

		private void TextureSelected(object sender, string item)
		{
			SelectedTexture = item;
			DialogResult = DialogResult.OK;
			Close();
		}

		public void SetTextureList(IEnumerable<string> items)
		{
			_textures.Clear();
			_textures.AddRange(items);

			UpdatePackageList();
			UpdateFavouritesList();
			UpdateTextureList();
		}

		public void SetSelectedTextures(IEnumerable<string> items)
		{
			_textureList.SetHighlightedTextures(items);
		}

		public void SetFilterText(string text)
		{
			if (text != null) FilterTextbox.Text = text;
		}

		private void FilterTextboxKeyUp(object sender, KeyEventArgs e)
		{
			SetMemory("Filter", FilterTextbox.Text);
			UpdateTextureList();
		}

		private long _packageSelectionChangedAt;

		// Ctrl+click on the package that is already selected = deselect it and go back to "All Packages"
		private void PackageTreeNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Button != MouseButtons.Left || (ModifierKeys & Keys.Control) == 0) return;
			if (e.Node == null || e.Node.Parent == null || e.Node != PackageTree.SelectedNode) return;
			if (PackageTree.HitTest(e.Location).Location != TreeViewHitTestLocations.Label) return;

			// This same click just selected the node: nothing to undo
			var sinceChangeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _packageSelectionChangedAt) * 1000 / System.Diagnostics.Stopwatch.Frequency;
			if (sinceChangeMs < 700) return;

			var root = PackageTree.Nodes.Cast<TreeNode>().FirstOrDefault();
			if (root != null) PackageTree.SelectedNode = root;
		}

		private void SelectedPackageChanged(object sender, TreeViewEventArgs e)
		{
			_packageSelectionChangedAt = System.Diagnostics.Stopwatch.GetTimestamp();
			FavouritesTree.SelectedNode = null;
			var package = PackageTree.SelectedNode;
			var key = package?.Name;
			if (String.IsNullOrWhiteSpace(key)) key = null;
			SetMemory("SelectedPackage", key);
			SetMemory("SelectedFavourite", (string)null);

			UpdateTextureList();
		}

		private void SelectedFavouriteChanged(object sender, TreeViewEventArgs e)
		{
			PackageTree.SelectedNode = null;
			var favourite = FavouritesTree.SelectedNode;
			var key = favourite?.Name;
			if (String.IsNullOrWhiteSpace(key)) key = null;
			SetMemory("SelectedFavourite", key);
			SetMemory("SelectedPackage", (string)null);

			UpdateTextureList();
		}

		private void UsedTexturesOnlyChanged(object sender, EventArgs e)
		{
			SetMemory("UsedTexturesOnly", UsedTexturesOnlyBox.Checked);
			UpdateTextureList();
		}

		// True while we're rebuilding PackageTree nodes ourselves, so the AfterCheck handler
		// below knows to ignore checkbox states it's setting programmatically and only react
		// to the user actually clicking a checkbox.
		private bool _updatingPackageList;

		private void UpdatePackageList()
		{
			var selected = PackageTree.SelectedNode;
			var selectedKey = selected == null ? GetMemory<string>("SelectedPackage") : selected.Name;

			var packageManager = _document?.Environment as ITexturePackageManager;
			var loadedCounts = _textureList.Collection.Packages
				.Where(p => _textures.Any(p.HasTexture))
				.GroupBy(p => p.ToString(), StringComparer.InvariantCultureIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Textures.Count, StringComparer.InvariantCultureIgnoreCase);

			var packageNames = packageManager != null
				? packageManager.GetAllTexturePackageNames()
				: loadedCounts.Keys.AsEnumerable();

			var disabled = packageManager != null
				? new HashSet<string>(packageManager.ManuallyDisabledTexturePackages, StringComparer.InvariantCultureIgnoreCase)
				: new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

			_updatingPackageList = true;
			try
			{
				PackageTree.CheckBoxes = packageManager != null;
				PackageTree.Nodes.Clear();
				var parent = PackageTree.Nodes.Add("", "All Packages");
				TreeNode reselect = null;
				foreach (var name in packageNames.OrderBy(x => x, StringComparer.InvariantCultureIgnoreCase))
				{
					var isLoaded = loadedCounts.TryGetValue(name, out var count);
					var label = isLoaded ? $"{name} ({count})" : $"{name} (unloaded)";
					var node = parent.Nodes.Add(name, label);
					node.Checked = !disabled.Contains(name);
					if (selectedKey == node.Name) reselect = node;
				}
				parent.Checked = parent.Nodes.Count > 0 && parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
				// No remembered package: show everything ("All Packages"), unless a favourites folder is being viewed
				var viewingFavourite = FavouritesTree.SelectedNode != null || GetMemory<string>("SelectedFavourite") != null;
				PackageTree.SelectedNode = reselect ?? (viewingFavourite ? null : parent);
				PackageTree.ExpandAll();
			}
			finally
			{
				_updatingPackageList = false;
			}
		}

		private IEnumerable<string> GetPackageTextures()
		{
			var package = PackageTree.SelectedNode;
			var key = package?.Name;
			if (String.IsNullOrWhiteSpace(key)) key = null;
			if (key == null) return new HashSet<string>(_textures);
			var p = _textureList.Collection.Packages.FirstOrDefault(x => x.ToString() == key);
			if (p == null) return new HashSet<string>(); // package is unloaded for this map
			var set = new HashSet<string>(_textures);
			set.IntersectWith(p.Textures);
			return set;
		}

		private void PackageTreeAfterCheck(object sender, TreeViewEventArgs e)
		{
			// Ignore checkbox changes we made ourselves (rebuilding the tree, syncing "All Packages").
			if (_updatingPackageList || e.Node == null) return;
			if (!(_document?.Environment is ITexturePackageManager)) return;

			// Ticking only changes what is shown here. Nothing is applied or saved until
			// "Reload Textures" is clicked.
			var root = e.Node.Parent == null ? e.Node : e.Node.Parent;

			_updatingPackageList = true;
			try
			{
				if (e.Node.Parent == null)
				{
					// "All Packages": check/uncheck every package
					foreach (TreeNode child in root.Nodes) child.Checked = root.Checked;
				}
				else
				{
					// A single package: "All Packages" is ticked only when every package is
					root.Checked = root.Nodes.Cast<TreeNode>().All(n => n.Checked);
				}
			}
			finally
			{
				_updatingPackageList = false;
			}
		}

		private async Task RefreshTexturesFromEnvironment()
		{
			_textureList.Collection = await _document.Environment.GetTextureCollection();
			_textures.Clear();
			_textures.AddRange(_textureList.Collection.GetBrowsableTextures());

			UpdatePackageList();
			await UpdateTextureList();
		}

		private async void ReloadTexturesButtonClick(object sender, EventArgs e)
		{
			ReloadTexturesButton.Enabled = false;
			try
			{
				if (_document != null)
				{
					// Apply and save the package selection now (and only now)
					if (_document.Environment is ITexturePackageManager packageManager && PackageTree.Nodes.Count > 0)
					{
						var disabled = new HashSet<string>(
							PackageTree.Nodes[0].Nodes.Cast<TreeNode>().Where(n => !n.Checked).Select(n => n.Name),
							StringComparer.InvariantCultureIgnoreCase);

						packageManager.SetPendingDisabledTexturePackages(disabled);

						if (!String.IsNullOrWhiteSpace(_document.FileName))
						{
							MapTexturePackageSettingsManager.GetInstance()?.SetDisabledPackages(_document.FileName, disabled);
							await Oy.Publish("Settings:Save");
						}
					}

					await Oy.Publish("Command:Run", new CommandMessage("BspEditor:Textures:Reload"));
					await RefreshTexturesFromEnvironment();
				}
			}
			finally
			{
				ReloadTexturesButton.Enabled = true;
			}
		}

		private void UpdateFavouritesList()
		{
			FavouritesTree.Nodes.Clear();
			var selected = FavouritesTree.SelectedNode;
			var selectedKey = selected == null ? GetMemory<string>("SelectedFavourite") : selected.Name;
			var favourites = _settingsManager.Folders;
			FavouritesTree.Nodes.Clear();
			var parent = FavouritesTree.Nodes.Add("", "All Favourites");
			TreeNode reselect;
			AddFavouriteTextureFolders(parent, favourites, selectedKey, out reselect);
			FavouritesTree.SelectedNode = reselect;
			FavouritesTree.ExpandAll();
		}

		private async Task UpdateTextureList()
		{
			var list = FavouritesTree.SelectedNode != null ? GetFavouriteFolderTextures() : GetPackageTextures();
			if (!String.IsNullOrEmpty(FilterTextbox.Text))
			{
				list = list.Where(x => x.ToLower().Contains(FilterTextbox.Text.ToLower()));
			}
			if (UsedTexturesOnlyBox.Checked && _document != null)
			{
				var textureNames = new HashSet<string>(_document.Map.Root.FindAll().SelectMany(x => x.Data.OfType<ITextured>()).Select(x => x.Texture.Name).Distinct());
				list = list.Where(x => textureNames.Contains(x, StringComparer.InvariantCultureIgnoreCase));
			}
			var l = list.ToList();
			await _textureList.SetTextureList(l);

			var sel = _document?.Map.Data.GetOne<ActiveTexture>()?.Name;
			if (sel != null)
			{
				_textureList.SetHighlightedTextures(new[] { sel });
				_textureList.ScrollToTexture(sel);
			}
		}



		private List<string> GetTexturesInFavourite(FavouriteTextureFolder fav)
		{
			return _textures.Where(x => InFavouriteList(fav.Items, x)).ToList();
		}

		private IEnumerable<string> GetFavouriteFolderTextures()
		{
			var folder = FavouritesTree.SelectedNode;
			var node = folder == null ? null : folder.Tag as FavouriteTextureFolder;
			var nodes = new List<FavouriteTextureFolder>();
			CollectNodes(nodes, node == null ? _settingsManager.Folders : node.Children);
			if (node != null) nodes.Add(node);
			var favs = nodes.SelectMany(x => x.Items).ToList();
			return _textures.Where(x => InFavouriteList(favs, x));
		}

		private bool InFavouriteList(IEnumerable<string> favs, string ti)
		{
			return favs.Contains(ti, StringComparer.InvariantCultureIgnoreCase);
		}

		private void CollectNodes(List<FavouriteTextureFolder> favs, IEnumerable<FavouriteTextureFolder> folders)
		{
			foreach (var f in folders)
			{
				favs.Add(f);
				CollectNodes(favs, f.Children);
			}
		}

		private void SizeValueChanged(object sender, EventArgs e)
		{
			SetMemory("SizeMode", SizeCombo.SelectedIndex);
			_textureList.ImageSize = Convert.ToInt32(SizeCombo.SelectedItem);
		}

		private static readonly char[] AllowedSpecialChars = "!@#$%^&*()-_=+<>,.?/'\"\\;:[]{}`~".ToCharArray();

		private void TextureBrowserKeyPress(object sender, KeyPressEventArgs e)
		{
			if (!_textureList.Focused) return;

			if (e.KeyChar == 8 && FilterTextbox.Text.Length > 0)
			{
				if (FilterTextbox.SelectionLength > 0)
				{
					FilterTextbox.Text = FilterTextbox.Text.Substring(0, FilterTextbox.SelectionStart) +
										 FilterTextbox.Text.Substring(FilterTextbox.SelectionStart + FilterTextbox.SelectionLength);
				}
				else
				{
					FilterTextbox.Text = FilterTextbox.Text.Substring(0, FilterTextbox.Text.Length - 1);
				}
				FilterTextboxKeyUp(null, null);
			}
			else if ((e.KeyChar >= 'a' && e.KeyChar <= 'z')
				|| (e.KeyChar >= '0' && e.KeyChar <= '9')
				|| AllowedSpecialChars.Contains(e.KeyChar))
			{
				if (FilterTextbox.SelectionLength > 0)
				{

					FilterTextbox.Text = FilterTextbox.Text.Substring(0, FilterTextbox.SelectionStart) +
										 e.KeyChar +
										 FilterTextbox.Text.Substring(FilterTextbox.SelectionStart + FilterTextbox.SelectionLength);
				}
				else
				{
					FilterTextbox.Text += e.KeyChar;
				}
				FilterTextboxKeyUp(null, null);
			}
		}

		private void TextureBrowserKeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Delete && FilterTextbox.SelectionLength > 0)
			{
				FilterTextbox.Text = FilterTextbox.Text.Substring(0, FilterTextbox.SelectionStart) +
										 FilterTextbox.Text.Substring(FilterTextbox.SelectionStart + FilterTextbox.SelectionLength);
				FilterTextboxKeyUp(null, null);
			}
		}

		private void SortOrderComboIndexChanged(object sender, EventArgs e)
		{
			// Nothing, for now
		}

		private void SortDescendingCheckboxChanged(object sender, EventArgs e)
		{
			SetMemory("SortDescending", SortDescendingCheckbox.Checked);
			_textureList.SortTextureList(x => x, SortDescendingCheckbox.Checked);
		}

		private void SelectButtonClicked(object sender, EventArgs e)
		{
			var textures = _textureList.GetHighlightedTextures().ToHashSet(StringComparer.InvariantCultureIgnoreCase);
			if (!textures.Any()) return;

			var sel = _document.Map.Root.Find(x => x.Data.OfType<ITextured>().Any(t => textures.Contains(t.Texture.Name))).ToList();
			var des = _document.Selection.Except(sel).ToList();

			var transaction = new Transaction(new Select(sel), new Deselect(des));
			MapDocumentOperation.Perform(_document, transaction);

			Close();
		}

		private void MarkButtonClicked(object sender, EventArgs e)
		{
			var textures = _textureList.GetHighlightedTextures().ToHashSet(StringComparer.InvariantCultureIgnoreCase);
			if (!textures.Any()) return;

			var fs = _document.Map.Data.GetOne<FaceSelection>();
			if (fs == null)
			{
				fs = new FaceSelection();
				_document.Map.Data.Add(fs);
			}

			// Mark (face-select) every face in the map that uses one of the highlighted textures
			fs.Clear();
			foreach (var obj in _document.Map.Root.Find(x => x.Data.OfType<Face>().Any(f => textures.Contains(f.Texture.Name))).ToList())
			{
				fs.Add(obj, obj.Data.OfType<Face>().Where(f => textures.Contains(f.Texture.Name)).ToArray());
			}

			Oy.Publish("TextureTool:SelectionChanged", fs);

			Close();
		}

		// Favourite list management

		private void AddFavouriteTextureFolders(TreeNode parent, IEnumerable<FavouriteTextureFolder> folders, string selectedKey, out TreeNode reselect)
		{
			reselect = null;
			foreach (var fav in folders)
			{
				var items = GetTexturesInFavourite(fav);
				var node = parent.Nodes.Add(parent.Tag + "/" + fav.Name, fav.Name + " (" + items.Count + ")");
				AddFavouriteTextureFolders(node, fav.Children, selectedKey, out reselect);
				if (selectedKey == node.Name) reselect = node;
				node.Tag = fav;
			}
		}

		private void DeleteFavouriteFolderButtonClicked(object sender, EventArgs e)
		{
			FavouriteTextureFolder parent = null;
			var selected = FavouritesTree.SelectedNode;
			if (selected != null && selected.Parent != null)
			{
				parent = selected.Parent.Tag as FavouriteTextureFolder;
				var siblings = parent != null ? parent.Children : _settingsManager.Folders;
				siblings.Remove(selected.Tag as FavouriteTextureFolder);
				UpdateFavouritesList();
				UpdateTextureList();
			}
		}
		private void RenameFolderButtonClicked(object sender, EventArgs e)
		{
			FavouriteTextureFolder item = null;
			var selected = FavouritesTree.SelectedNode;
			if (selected != null && selected.Parent != null)
			{
				item = selected.Tag as FavouriteTextureFolder;
				var siblings = item != null ? item.Children : _settingsManager.Folders;

				using (var qf = new QuickForm("Rename Folder") { UseShortcutKeys = true }.TextBox("Name", "Name").OkCancel("Ok", "Cancel"))
				{
					if (qf.ShowDialog() != DialogResult.OK) return;

					var name = qf.String("Name");
					var uniqName = name;
					if (String.IsNullOrWhiteSpace(name)) return;

					var counter = 1;
					while (siblings.Any(x => x.Name == uniqName))
					{
						uniqName = name + "_" + counter;
						counter++;
					}

					item.Name = uniqName;
					UpdateFavouritesList();
				}
			}
		}
		private void AddFavouriteFolderButtonClicked(object sender, EventArgs e)
		{
			FavouriteTextureFolder parent = null;
			var selected = FavouritesTree.SelectedNode;
			if (selected != null) parent = selected.Tag as FavouriteTextureFolder;
			var siblings = parent != null ? parent.Children : _settingsManager.Folders;
			using (var qf = new QuickForm("Enter Folder Name") { UseShortcutKeys = true }.TextBox("Name", "Name").OkCancel("Ok", "Cancel"))
			{
				if (qf.ShowDialog() != DialogResult.OK) return;

				var name = qf.String("Name");
				var uniqName = name;
				if (String.IsNullOrWhiteSpace(name)) return;

				var counter = 1;
				while (siblings.Any(x => x.Name == uniqName))
				{
					uniqName = name + "_" + counter;
					counter++;
				}

				siblings.Add(new FavouriteTextureFolder { Name = uniqName });
				UpdateFavouritesList();
			}
		}

		private TreeNode _highlightedNode;

		private void FavouritesTreeDragEnter(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(string)) && !e.Data.GetDataPresent(typeof(List<string>))) return;

			var pt = FavouritesTree.PointToClient(new Point(e.X, e.Y));
			var highlightedNode = FavouritesTree.GetNodeAt(pt);
			if (highlightedNode == null || !(highlightedNode.Tag is FavouriteTextureFolder)) return;

			_highlightedNode = highlightedNode;
			_highlightedNode.BackColor = Color.LightSkyBlue;
			e.Effect = DragDropEffects.Copy;
		}

		private void FavouritesTreeDragDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(typeof(string)) || e.Data.GetDataPresent(typeof(List<string>)))
			{
				var pt = FavouritesTree.PointToClient(new Point(e.X, e.Y));
				var dest = FavouritesTree.GetNodeAt(pt);
				if (dest != null && dest.Tag is FavouriteTextureFolder)
				{
					var data = new List<string>();
					data.AddRange(
						(e.Data.GetData(typeof(List<string>)) as List<string>)
						);
					var folder = (FavouriteTextureFolder)dest.Tag;
					foreach (var ti in data)
					{
						if (!folder.Items.Contains(ti, StringComparer.InvariantCultureIgnoreCase)) folder.Items.Add(ti);
					}
					UpdateFavouritesList();
				}
			}
			if (_highlightedNode != null) _highlightedNode.BackColor = Color.Transparent;
			_highlightedNode = null;
		}

		private void FavouritesTreeDragLeave(object sender, EventArgs e)
		{
			if (_highlightedNode != null) _highlightedNode.BackColor = Color.Transparent;
			_highlightedNode = null;
		}

		private void FavouritesTreeDragOver(object sender, DragEventArgs e)
		{
			if (!e.Data.GetDataPresent(typeof(string)) && !e.Data.GetDataPresent(typeof(List<string>))) return;

			var pt = FavouritesTree.PointToClient(new Point(e.X, e.Y));
			var highlightedNode = FavouritesTree.GetNodeAt(pt);
			if (highlightedNode == null || !(highlightedNode.Tag is FavouriteTextureFolder))
			{
				if (_highlightedNode != null) _highlightedNode.BackColor = Color.Transparent;
				_highlightedNode = null;
				e.Effect = DragDropEffects.None;
				return;
			}

			if (_highlightedNode != null) _highlightedNode.BackColor = Color.Transparent;
			_highlightedNode = highlightedNode;
			_highlightedNode.BackColor = Color.LightSkyBlue;
			e.Effect = DragDropEffects.Copy;
		}

		private void RemoveFavouriteItemButtonClicked(object sender, EventArgs e)
		{
			var selection = _textureList.GetHighlightedTextures().Select(x => x);

			var folder = FavouritesTree.SelectedNode;
			var node = folder == null ? null : folder.Tag as FavouriteTextureFolder;
			var nodes = new List<FavouriteTextureFolder>();
			CollectNodes(nodes, node == null ? _settingsManager.Folders : node.Children);
			if (node != null) nodes.Add(node);

			nodes.ForEach(x => x.Items.RemoveAll(selection.Contains));
			UpdateFavouritesList();
			UpdateTextureList();
		}

		// Memory (per environment; transient)

		private static readonly Dictionary<string, Memory> _memory = new Dictionary<string, Memory>();

		private class Memory
		{
			public Dictionary<string, object> Values { get; } = new Dictionary<string, object>();

			public void Set<T>(string name, T value)
			{
				Values[GetType().Name + '.' + name] = value;
			}

			public T Get<T>(string name, T def = default(T))
			{
				if (!Values.TryGetValue(GetType().Name + '.' + name, out var v)) return def;

				try
				{
					return (T)Convert.ChangeType(v, typeof(T));
				}
				catch
				{
					return def;
				}

			}
		}

		private void SetMemory<T>(string name, T value)
		{
			var id = _document?.Environment?.ID;
			if (id == null) return;
			if (!_memory.TryGetValue(id, out var m)) _memory[id] = m = new Memory();
			m.Set(name, value);
		}

		private T GetMemory<T>(string name, T def = default(T))
		{
			var id = _document?.Environment?.ID;
			if (id == null) return def;
			if (_memory.TryGetValue(id, out var m)) return m.Get(name, def);
			return def;
		}
		[Export(typeof(ISettingsContainer))]
		public class SettingsManager : ISettingsContainer
		{
			private static SettingsManager _instance;

			[Setting("FavouriteFolders")]
			public List<FavouriteTextureEnvironmentCollection> FavouriteTextureFolders { get; private set; } = new();

			public string Name => "Sledge.BspEditor.Tools.Texture.TextureBrowser.SettingsManager";

			public bool ValuesLoaded { get; set; } = false;
			public SettingsManager()
			{
				_instance = this;
			}

			public IEnumerable<SettingKey> GetKeys()
			{
				yield break;
			}

			public void LoadValues(ISettingsStore store)
			{
				FavouriteTextureFolders = store.Get<FavouriteTextureEnvironmentCollection[]>("FavouriteFolders")?.ToList() ?? new();
				ValuesLoaded = true;
			}

			public void StoreValues(ISettingsStore store)
			{
				store.Set("FavouriteFolders", FavouriteTextureFolders);
			}

			internal static SettingsManager GetInstance()
			{
				return _instance;
			}
		}
	}
}
