using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Shell.Menu;
using Sledge.Shell.Components;
using Sledge.Shell.Settings;

namespace Sledge.Shell.Registers
{
	/// <summary>
	/// The menu register registers and handles menu and toolbar items
	/// </summary>
	[Export(typeof(IStartupHook))]
	[Export(typeof(IInitialiseHook))]
	internal class MenuRegister : IStartupHook, IInitialiseHook
	{
		// The menu register needs direct access to the shell
		[Import] private Forms.Shell _shell;

		// Store the context (the menu register is one of the few things that should need static access to the context)
		[Import] private IContext _context;

		[ImportMany] private IEnumerable<Lazy<IMenuItemProvider>> _itemProviders;
		[ImportMany] private IEnumerable<Lazy<IMenuMetadataProvider>> _metaDataProviders;

		public Task OnStartup()
		{
			// Register commands as menu items
			foreach (var export in _itemProviders)
			{
				export.Value.MenuItemsChanged += UpdateMenus;
				foreach (var menuItem in export.Value.GetMenuItems())
				{
					Add(menuItem);
				}
			}

			return Task.FromResult(0);
		}

		public async Task OnInitialise()
		{
			// Register metadata providers
			foreach (var md in _metaDataProviders)
			{
				_declaredGroups.AddRange(md.Value.GetMenuGroups());
				_declaredSections.AddRange(md.Value.GetMenuSections());
			}

			_shell.InvokeSync(() =>
			{
				_tree = new VirtualMenuTree(_context, _shell.MenuStrip, _shell.ToolbarContainer, _declaredSections, _declaredGroups);
				_tree.ResetItems(_menuItems.Values);
				_toolbarSettingsApplied = TopToolbarSettings.Loaded;
			});

			Oy.Subscribe<IContext>("Context:Changed", ContextChanged);
			Oy.Subscribe<object>("Menu:Update", UpdateMenu);
			Oy.Subscribe<bool>("Theme:Changed", (useDark) => _tree.UseDarkTheme = useDark);

			// Re-lay out the top toolbar when its settings change (settings form, or its right-click menu)
			Oy.Subscribe<object>("SettingsChanged", ToolbarSettingsChanged);
			Oy.Subscribe<object>("TopToolbar:Changed", ToolbarSettingsChanged);
		}

		private Task ContextChanged(IContext context)
		{
			// The settings may finish loading after the menus were first built; apply them once they are available
			if (!_toolbarSettingsApplied && TopToolbarSettings.Loaded)
			{
				_toolbarSettingsApplied = true;
				_shell.InvokeLater(() => _tree.ApplyToolbarSettings());
			}
			return UpdateMenu(context);
		}

		private bool _toolbarSettingsApplied;

		private Task ToolbarSettingsChanged(object obj)
		{
			_toolbarSettingsApplied = true;
			_shell.InvokeLater(() => _tree?.ApplyToolbarSettings());
			return Task.CompletedTask;
		}

		/// <summary>
		/// Every button that can appear in the top toolbar, in default order (used by the settings editor).
		/// </summary>
		internal static IReadOnlyList<TopToolbarItemInfo> AllToolbarItems { get; private set; } = new List<TopToolbarItemInfo>();

		private async Task UpdateMenu(object obj)
		{
			_shell.InvokeLater(_tree.Update);
		}

		// Clear all menus and repopulate them from the menu item providers
		private void UpdateMenus(object sender, EventArgs e)
		{
			_shell.InvokeLater(() =>
			{
				_menuItems.Clear();

				foreach (var export in _itemProviders)
				{
					foreach (var menuItem in export.Value.GetMenuItems())
					{
						Add(menuItem);
					}
				}

				_tree.ResetItems(_menuItems.Values);
			});
		}

		/// <summary>
		/// The list of all menu items by ID
		/// </summary>
		private readonly Dictionary<string, IMenuItem> _menuItems;

		/// <summary>
		/// Sections declared by menu metadata providers. A section is a top-level menu or toolbar.
		/// </summary>
		private readonly List<MenuSection> _declaredSections;

		/// <summary>
		/// Groups declared by menu metadata providers. Groups within a section are separated with lines.
		/// </summary>
		private readonly List<MenuGroup> _declaredGroups;

		private VirtualMenuTree _tree;

		public MenuRegister()
		{
			_menuItems = new Dictionary<string, IMenuItem>();
			_declaredSections = new List<MenuSection>();
			_declaredGroups = new List<MenuGroup>();
		}

		/// <summary>
		/// Add a menu item to the list
		/// </summary>
		/// <param name="menuItem">The menu item to add</param>
		private void Add(IMenuItem menuItem)
		{
			_menuItems[menuItem.ID] = menuItem;
		}

		/// <summary>
		/// A class that handles the tree of menu items
		/// and inserts them into the correct positions.
		/// </summary>
		private class VirtualMenuTree
		{
			private readonly IContext _context;
			private readonly List<MenuSection> _declaredSections;
			private readonly List<MenuGroup> _declaredGroups;
			private bool _useDarkTheme = false;
			public bool UseDarkTheme
			{
				get => _useDarkTheme; set
				{
					_useDarkTheme = value;
					if (value)
					{
						MenuStrip.Renderer = new CustomToolStripRenderer(SystemColors.ControlDarkDark, _backColor);
						return;
					}
					MenuStrip.Renderer = null;
				}
			}
			private Color _systemDarkBackColor = Color.FromArgb(50, 50, 50);
			private Color _backColor = Color.FromArgb(70, 70, 70);

			/// <summary>
			/// The container whose four edge panels can host the toolbars
			/// </summary>
			private ToolStripContainer ToolbarContainer { get; set; }

			/// <summary>
			/// The toolbar strips we have joined to a panel (they may have been dragged to another panel since)
			/// </summary>
			private readonly List<ToolStrip> _joinedStrips = new List<ToolStrip>();

			private ContextMenuStrip _toolbarMenu;

			/// <summary>
			/// The menu strip for the top level menus
			/// </summary>
			private MenuStrip MenuStrip { get; set; }

			/// <summary>
			/// The root nodes of the virtual tree
			/// </summary>
			private Dictionary<string, MenuTreeRoot> RootNodes { get; set; }

			public VirtualMenuTree(IContext context, MenuStrip menuStrip, ToolStripContainer toolbarContainer, List<MenuSection> declaredSections, List<MenuGroup> declaredGroups)
			{
				_context = context;
				_declaredSections = declaredSections;
				_declaredGroups = declaredGroups;
				MenuStrip = menuStrip;
				ToolbarContainer = toolbarContainer;
				RootNodes = new Dictionary<string, MenuTreeRoot>();
				Clear();

			}

			public void ResetItems(IEnumerable<IMenuItem> items)
			{
				Clear();

				foreach (var mi in items)
				{
					Add(mi);
				}

				Render();
			}

			private void Render()
			{
				MenuStrip.SuspendLayout();
				MenuStrip.Items.Clear();
				MenuStrip.Items.AddRange(RootNodes.Values.OrderBy(x => x.OrderHint).Select(x => x.MenuMenuItem).OfType<ToolStripItem>().ToArray());
				MenuStrip.ResumeLayout();
				RenderToolbars();
			}

			/// <summary>
			/// Re-applies the top toolbar settings (position, lock, button visibility, order and icons).
			/// </summary>
			public void ApplyToolbarSettings()
			{
				RenderToolbars();
			}

			private ToolStripPanel PanelFor(ToolbarDock dock)
			{
				switch (dock)
				{
					case ToolbarDock.Bottom: return ToolbarContainer.BottomToolStripPanel;
					case ToolbarDock.Left: return ToolbarContainer.LeftToolStripPanel;
					case ToolbarDock.Right: return ToolbarContainer.RightToolStripPanel;
					default: return ToolbarContainer.TopToolStripPanel;
				}
			}

			/// <summary>
			/// Removes the toolbar strips we joined from whichever panel they are in now.
			/// Only our own strips are removed, the left panel also hosts the tools toolbar.
			/// </summary>
			private void DetachToolbars()
			{
				foreach (var strip in _joinedStrips)
				{
					strip.Parent?.Controls.Remove(strip);
				}
				_joinedStrips.Clear();
			}

			private void RenderToolbars()
			{
				DetachToolbars();

				var dock = TopToolbarSettings.Dock;
				var vertical = dock == ToolbarDock.Left || dock == ToolbarDock.Right;
				var panel = PanelFor(dock);

				// Publish the list of available buttons for the settings editor and work out the user's layout
				AllToolbarItems = RootNodes.Values
					.OrderBy(x => x.OrderHint)
					.SelectMany(r => r.ToolbarNodes.Select(n => new TopToolbarItemInfo { Id = n.Id, Name = n.DisplayName, Section = r.SectionName, DefaultIcon = n.DefaultIcon }))
					.ToList();
				var layout = TopToolbarSettings.Layout.Resolve(AllToolbarItems.Select(x => x.Id));

				if (_toolbarMenu == null) _toolbarMenu = BuildToolbarMenu();

				var iconSize = TopToolbarSettings.IconSize;
				panel.BeginInit();
				foreach (var ts in RootNodes.Values.OrderByDescending(x => x.OrderHint))
				{
					ts.ApplyLayout(layout, iconSize);
					if (ts.ToolStrip.Items.Count == 0) continue;

					ts.ToolStrip.ImageScalingSize = new Size(iconSize, iconSize);
					WireDragDrop(ts.ToolStrip);

					ts.ToolStrip.LayoutStyle = vertical ? ToolStripLayoutStyle.VerticalStackWithOverflow : ToolStripLayoutStyle.Flow;
					ts.ToolStrip.GripStyle = TopToolbarSettings.Locked ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;
					ts.ToolStrip.ContextMenuStrip = _toolbarMenu;
					panel.Join(ts.ToolStrip);
					_joinedStrips.Add(ts.ToolStrip);
				}
				panel.EndInit();
				panel.ContextMenuStrip = _toolbarMenu;

				if (UseDarkTheme)
				{
					panel.BackColor = _systemDarkBackColor;
					panel.ForeColor = System.Drawing.Color.White;
					foreach (var strip in _joinedStrips)
					{
						strip.BackColor = _systemDarkBackColor;
						strip.ForeColor = System.Drawing.Color.White;
					}
				}
			}

			private readonly HashSet<ToolStrip> _wired = new HashSet<ToolStrip>();
			private Point _dragStart;
			private string _dragId;

			private static string NodeId(ToolStripItem item)
			{
				return (item?.Tag as BaseMenuTreeNode)?.Id;
			}

			private static string DroppedIconFile(DragEventArgs e)
			{
				if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
				var files = e.Data.GetData(DataFormats.FileDrop) as string[];
				if (files == null || files.Length != 1) return null;
				var ext = System.IO.Path.GetExtension(files[0])?.ToLowerInvariant();
				return new[] { ".svg", ".png", ".ico", ".bmp", ".jpg", ".gif" }.Contains(ext) ? files[0] : null;
			}

			/// <summary>
			/// Lets the user drag a toolbar button to a new place in its strip, or drop an image file on a button to give it that icon.
			/// </summary>
			private void WireDragDrop(ToolStrip strip)
			{
				if (!_wired.Add(strip)) return;
				strip.AllowDrop = true;

				strip.MouseDown += (s, e) =>
				{
					_dragId = null;
					if (e.Button != MouseButtons.Left || TopToolbarSettings.Locked) return;
					var item = strip.GetItemAt(e.Location);
					if (item is ToolStripButton) { _dragId = NodeId(item); _dragStart = e.Location; }
				};
				strip.MouseUp += (s, e) => _dragId = null;
				strip.MouseMove += (s, e) =>
				{
					if (_dragId == null || e.Button != MouseButtons.Left) return;
					var dx = Math.Abs(e.X - _dragStart.X);
					var dy = Math.Abs(e.Y - _dragStart.Y);
					if (dx < SystemInformation.DragSize.Width && dy < SystemInformation.DragSize.Height) return;
					var id = _dragId;
					_dragId = null;
					strip.DoDragDrop(new DataObject("Sledge.TopToolbarButton", id), DragDropEffects.Move);
				};

				DragEventHandler over = (s, e) =>
				{
					var pt = strip.PointToClient(new Point(e.X, e.Y));
					if (e.Data.GetDataPresent("Sledge.TopToolbarButton") && !TopToolbarSettings.Locked) e.Effect = DragDropEffects.Move;
					else if (DroppedIconFile(e) != null && strip.GetItemAt(pt) is ToolStripButton) e.Effect = DragDropEffects.Copy;
					else e.Effect = DragDropEffects.None;
				};
				strip.DragEnter += over;
				strip.DragOver += over;
				strip.DragDrop += (s, e) =>
				{
					var pt = strip.PointToClient(new Point(e.X, e.Y));
					var target = strip.GetItemAt(pt) as ToolStripButton;
					var layout = TopToolbarSettings.Layout.Resolve(AllToolbarItems.Select(x => x.Id));

					if (e.Data.GetDataPresent("Sledge.TopToolbarButton"))
					{
						var id = e.Data.GetData("Sledge.TopToolbarButton") as string;
						var targetId = NodeId(target);
						// Only reorder inside the same strip
						var sameStrip = strip.Items.OfType<ToolStripButton>().Any(x => NodeId(x) == id);
						if (id == null || targetId == null || id == targetId || !sameStrip) return;

						var moving = layout.Find(id);
						layout.Remove(moving);
						var idx = layout.FindIndex(x => x.Id == targetId);
						// Dropped on the far half of the target: go after it
						var after = strip.LayoutStyle == ToolStripLayoutStyle.VerticalStackWithOverflow
							? pt.Y > target.Bounds.Top + target.Bounds.Height / 2
							: pt.X > target.Bounds.Left + target.Bounds.Width / 2;
						layout.Insert(after ? idx + 1 : idx, moving);
						TopToolbarSettings.Layout = layout;
						ToolbarChanged();
					}
					else
					{
						var file = DroppedIconFile(e);
						var targetId = NodeId(target);
						if (file == null || targetId == null) return;
						if (IconLoader.LoadFromFile(file, 16) == null) return;
						layout.Find(targetId).IconPath = file;
						TopToolbarSettings.Layout = layout;
						ToolbarChanged();
					}
				};
			}

			/// <summary>
			/// The right-click menu of the toolbar: choose which edge it is docked to and lock it in place.
			/// </summary>
			private ContextMenuStrip BuildToolbarMenu()
			{
				var menu = new ContextMenuStrip();
				var dockItems = new List<ToolStripMenuItem>();
				foreach (ToolbarDock d in Enum.GetValues(typeof(ToolbarDock)))
				{
					var dock = d;
					var item = new ToolStripMenuItem("Dock " + dock.ToString().ToLowerInvariant()) { Tag = dock };
					item.Click += (s, e) =>
					{
						TopToolbarSettings.Dock = dock;
						ToolbarChanged();
					};
					dockItems.Add(item);
					menu.Items.Add(item);
				}
				menu.Items.Add(new ToolStripSeparator());
				var lockItem = new ToolStripMenuItem("Lock toolbar");
				lockItem.Click += (s, e) =>
				{
					TopToolbarSettings.Locked = !TopToolbarSettings.Locked;
					ToolbarChanged();
				};
				menu.Items.Add(lockItem);

				menu.Opening += (s, e) =>
				{
					foreach (var di in dockItems) di.Checked = (ToolbarDock) di.Tag == TopToolbarSettings.Dock;
					lockItem.Checked = TopToolbarSettings.Locked;
				};
				return menu;
			}

			private static void ToolbarChanged()
			{
				// Persist the new position, then tell the register to lay the toolbar out again
				Oy.Publish("Settings:Save");
				Oy.Publish("TopToolbar:Changed", new object());
			}

			/// <summary>
			/// Add a section to the tree. This will create a top-level menu as well as a toolbar.
			/// </summary>
			private void AddSection(MenuSection ds)
			{
				// Create the root
				var rtn = new MenuTreeRoot(_context, ds.Description, ds);

				// When the menu is closed, push an empty string to the status bar
				rtn.MenuMenuItem.DropDownClosed += (s, a) => { Oy.Publish("Status:Information", ""); };

				// When the menu is opened, update the state of all the menu items in this section
				rtn.MenuMenuItem.DropDownOpening += (s, a) => { rtn.Update(); };

				// Add the node, menu, and toolbar
				RootNodes.Add(ds.Name, rtn);
			}

			/// <summary>
			/// Add an item to the tree. This will add the menu item and the toolbar button if required.
			/// </summary>
			private void Add(IMenuItem item)
			{
				// If the section isn't known, add it to the end
				if (!RootNodes.ContainsKey(item.Section)) AddSection(new MenuSection(item.Section, item.Section, "Z"));

				var root = RootNodes[item.Section];

				root.AddDescendant(item, _declaredGroups);
			}

			public void Clear()
			{
				MenuStrip.Items.Clear();
				DetachToolbars();
				RootNodes.Clear();

				// Add known sections straight away
				foreach (var ds in _declaredSections.OrderBy(x => x.OrderHint)) AddSection(ds);
			}

			public void Update()
			{
				foreach (var node in RootNodes.Values)
				{
					node.Update();
				}
			}
		}
		public class CustomToolStripRenderer : ToolStripProfessionalRenderer
		{
			private readonly Color _pressedBackColor;
			private readonly Color _backColor;
			private readonly Brush _dropDownEnabledColor = new SolidBrush(Color.Black);
			private readonly Brush _dropDownDisabledColor = new SolidBrush(Color.Gray);

			public CustomToolStripRenderer(Color pressedBackColor, Color backColor)
			{
				_pressedBackColor = pressedBackColor;
				_backColor = backColor;
			}
			protected override void OnRenderItemBackground(ToolStripItemRenderEventArgs e)
			{
				//base.OnRenderItemBackground(e);
				using (var brush = new SolidBrush(Color.Black)) // Example color
				{
					e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
				}
			}
			protected override void OnRenderToolStripPanelBackground(ToolStripPanelRenderEventArgs e)
			{
				//using (var brush = new SolidBrush(Color.Black))
				//{
				//	e.Graphics.FillRectangle(brush, e.ToolStripPanel.ClientRectangle);
				//	e.Graphics.DrawRectangle(SystemPens.Highlight, e.ToolStripPanel.ClientRectangle);
				//}
				base.OnRenderToolStripPanelBackground(e);

			}
			protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
			{
				if (e.ToolStrip is ToolStripDropDown)
				{
					// Custom border for the drop-down menu
					using (var pen = new Pen(ControlPaint.Dark(_backColor), 1)) // Border color
					{
						e.Graphics.DrawRectangle(pen, new Rectangle(Point.Empty, e.ToolStrip.ClientSize - new Size(1, 1)));
					}
				}
				else
				{
					base.OnRenderToolStripBorder(e);
				}
			}
			protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
			{
				return;
				//using (var pen = new Pen(Color.Gold, 1)) // Separator color
				//{
				//	int y = e.ToolStrip.ClientRectangle.Top + e.ToolStrip.ClientRectangle.Height / 2;
				//	e.Graphics.DrawLine(pen, e.ToolStrip.ClientRectangle.Left, y, e.ToolStrip.ClientRectangle.Right, y);
				//}
				//base.OnRenderImageMargin(e);
			}
			protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
			{
				//using (var pen = new Pen(Color.Purple, 1)) // Separator color
				//{
				//	int y = e.ToolStrip.ClientRectangle.Top + e.ToolStrip.ClientRectangle.Height / 2;
				//	e.Graphics.DrawLine(pen, e.ToolStrip.ClientRectangle.Left, y, e.ToolStrip.ClientRectangle.Right, y);
				//}
				base.OnRenderItemImage(e);
			}
			protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
			{

				// Custom separator rendering
				using (var pen = new Pen(ControlPaint.Dark(_backColor), 1)) // Separator color
				{
					int y = e.Item.ContentRectangle.Top + e.Item.ContentRectangle.Height / 2;
					e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left, y, e.Item.ContentRectangle.Right, y);
				}
			}
			protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
			{
				// Custom background for the entire drop-down
				if (e.ToolStrip is ToolStripDropDown)
				{
					e.ToolStrip.BackColor = _backColor;
					using (var brush = new SolidBrush(_backColor))
					{
						e.Graphics.FillRectangle(brush, e.ToolStrip.DisplayRectangle);
						e.Graphics.DrawRectangle(SystemPens.ControlDarkDark, e.ToolStrip.ClientRectangle);
					}
				}
				//else if (e.ToolStrip is ToolStripDropDownItem)
				//{
				//	using (var brush = new SolidBrush(Color.Yellow))
				//	{
				//		e.Graphics.FillRectangle(brush, e.AffectedBounds);
				//	}
				//}
				else
				{
					base.OnRenderToolStripBackground(e);
				}
			}
			protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
			{
				if (e.Item.Owner is ToolStripDropDown) //Item is drop-down item
				{
					if (e.Item is ToolStripMenuItem menuItem)
					{
						if (!e.Item.Enabled)
						{

							e.Graphics.DrawString(menuItem.Text, e.TextFont, _dropDownDisabledColor, e.TextRectangle);
						}
						else
						{
							e.Graphics.DrawString(menuItem.Text, e.TextFont, _dropDownEnabledColor, e.TextRectangle);
						}
						if (!string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString)) //Item has shortcut
						{
							SizeF shortcutTextSize = e.Graphics.MeasureString(menuItem.ShortcutKeyDisplayString, e.TextFont);
							float shortcutX = e.Item.ContentRectangle.Right - shortcutTextSize.Width - 10; // 10px padding from the right

							RectangleF shortcutRect = new RectangleF(shortcutX, e.Item.ContentRectangle.Top, shortcutTextSize.Width, shortcutTextSize.Height);
							if (e.Item.Enabled)
							{

								e.Graphics.DrawString(menuItem.ShortcutKeyDisplayString, e.TextFont, _dropDownEnabledColor, shortcutRect);
								return;
							}

							e.Graphics.DrawString(menuItem.ShortcutKeyDisplayString, e.TextFont, _dropDownDisabledColor, shortcutRect);
							return;
						}
					}

				}
				else if (e.Item.Owner is MenuStrip)
				{
					using (Brush textBrush = new SolidBrush(Color.White))
					{
						e.Graphics.DrawString(e.Text, e.TextFont, textBrush, e.TextRectangle);
					}
				}
				else
				{

					base.OnRenderItemText(e);
				}

			}

			protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
			{
				// Check if the item is pressed or selected
				if (e.Item.Pressed)
				{
					// Draw custom background for pressed state
					using (var brush = new SolidBrush(_pressedBackColor))
					{
						e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
					}
				}
				else if (e.Item.Selected)
				{
					// Draw custom background for selected state
					using (var brush = new SolidBrush(Color.DimGray)) // Example color
					{
						e.Graphics.FillRectangle(brush, e.Item.ContentRectangle);
					}
				}
				else
				{
					// Draw default background
					base.OnRenderMenuItemBackground(e);
				}
			}
		}

		/// <summary>
		/// A root node of a menu tree
		/// </summary>
		private class MenuTreeRoot : BaseMenuTreeNode
		{
			private MenuSection Section { get; }
			public ToolStrip ToolStrip { get; }

			public override string OrderHint => Section.OrderHint;

			public string SectionName => Section.Description;

			private List<MenuTreeGroup> _toolbarGroups;

			/// <summary>
			/// The nodes that have a toolbar button, in default order.
			/// </summary>
			public IEnumerable<BaseMenuTreeNode> ToolbarNodes => _toolbarGroups.SelectMany(g => g.Nodes).Where(n => n.ToolbarButton != null);

			/// <summary>
			/// Rebuilds the toolbar strip from the user's layout: order, visibility and custom icons.
			/// </summary>
			public void ApplyLayout(TopToolbarLayout layout, int iconSize)
			{
				var nodes = ToolbarNodes
					.Select((n, i) => new { Node = n, Index = i, Pos = layout.FindIndex(e => e.Id == n.Id) })
					.OrderBy(x => x.Pos < 0 ? int.MaxValue : x.Pos)
					.ThenBy(x => x.Index)
					.Select(x => x.Node)
					.ToList();

				// RemoveAt rather than Clear so the buttons are kept alive
				while (ToolStrip.Items.Count > 0) ToolStrip.Items.RemoveAt(0);

				string lastGroup = null;
				var any = false;
				foreach (var node in nodes)
				{
					var entry = layout.Find(node.Id);
					if (entry != null && !entry.Visible) continue;

					// Icon: the user's own if set and loadable, otherwise the built-in one
					Image icon = null;
					if (entry != null && !string.IsNullOrWhiteSpace(entry.IconPath))
					{
						icon = IconLoader.LoadFromFile(entry.IconPath, iconSize);
					}
					// Same look as the left tool bar: image only, fixed square buttons sized from the icon size
					var button = node.ToolbarButton;
					button.DisplayStyle = ToolStripItemDisplayStyle.Image;
					button.ImageScaling = ToolStripItemImageScaling.None;
					button.ImageAlign = ContentAlignment.MiddleCenter;
					button.Image = icon ?? node.ScaledIcon(iconSize);
					button.AutoSize = false;
					button.Width = iconSize + 4;
					button.Height = iconSize + 4;

					var group = node.Group?.Name ?? "";
					if (any && group != lastGroup) ToolStrip.Items.Add(new ToolStripSeparator());
					ToolStrip.Items.Add(node.ToolbarButton);
					lastGroup = group;
					any = true;
				}
			}

			public MenuTreeRoot(IContext context, string text, MenuSection section)
			{
				Section = section;
				MenuMenuItem = new ToolStripMenuItem(text) { Tag = this };
				ToolStrip = new ToolStrip { Tag = this, LayoutStyle = ToolStripLayoutStyle.Flow };
				Context = context;
				_toolbarGroups = new List<MenuTreeGroup>();
			}

			/// <summary>
			/// Add a descendant to this root node. Searches down the path until we find the correct parent
			/// </summary>
			public void AddDescendant(IMenuItem item, List<MenuGroup> declaredGroups)
			{
				// Find the parent node for this item
				// Start at the section root node
				BaseMenuTreeNode node = this;

				// Traverse the path until we get to the target
				var path = (item.Path ?? "").Split('/').Where(x => x.Length > 0).ToList();
				var currentPath = new List<string>();
				foreach (var p in path)
				{
					currentPath.Add(p);

					// If the current node isn't found, add it in
					if (!node.Children.ContainsKey(p))
					{
						var gr = declaredGroups.FirstOrDefault(x => x.Name == p && x.Path == String.Join("/", currentPath) && x.Section == item.Section);
						node.AddChild(p, new MenuTreeTextNode(Context, gr?.Description ?? p, gr));
					}

					node = node.Children[p];
				}

				// Add the node to the parent node
				var group = declaredGroups.FirstOrDefault(x => x.Name == item.Group && x.Path == item.Path && x.Section == item.Section);
				var itemNode = new MenuTreeNode(Context, item, group);
				node.AddChild(item.ID, itemNode);

				// Add to the toolbar as well
				// Items with no icon are never allowed
				if (item.AllowedInToolbar && item.Icon != null)
				{
					AddToolbarItem(itemNode);
				}
			}

			private void AddToolbarItem(BaseMenuTreeNode menuTreeNode)
			{
				if (_toolbarGroups.All(x => x.Group.Name != menuTreeNode.Group.Name))
				{
					_toolbarGroups.Add(new MenuTreeGroup(menuTreeNode.Group));
					_toolbarGroups = _toolbarGroups.OrderBy(x => x.Group.OrderHint).ToList();
				}

				// Insert the item into the correct index
				var groupIndex = _toolbarGroups.FindIndex(x => x.Group.Name == menuTreeNode.Group.Name);

				// Skip to the start of the group
				var groupStart = 0;
				for (var i = 0; i < groupIndex; i++)
				{
					var g = _toolbarGroups[i];
					groupStart += g.Nodes.Count + (g.HasSplitter ? 1 : 0);
				}

				// Add the node to the list and sort
				var group = _toolbarGroups[groupIndex];
				group.Nodes = group.Nodes.Union(new[] { menuTreeNode }).OrderBy(x => x.OrderHint ?? "").ToList();

				// Skip to the start of the node and insert
				var idx = group.Nodes.IndexOf(menuTreeNode);
				ToolStrip.Items.Insert(groupStart + idx, menuTreeNode.ToolbarButton);

				// Check groups for splitters
				groupStart = 0;
				for (var i = 0; i < _toolbarGroups.Count - 1; i++)
				{
					var g = _toolbarGroups[i];
					groupStart += g.Nodes.Count;

					// Add a splitter to the group if needed
					if (!g.HasSplitter && g.Nodes.Count > 0)
					{
						ToolStrip.Items.Insert(groupStart, new ToolStripSeparator());
						g.HasSplitter = true;
					}

					groupStart++;
				}
			}
		}

		/// <summary>
		/// A dummy node of the virtual menu tree. This node is text only, always enabled, and does nothing.
		/// </summary>
		private class MenuTreeTextNode : BaseMenuTreeNode
		{
			public override string OrderHint => Group.OrderHint;

			public MenuTreeTextNode(IContext context, string text, MenuGroup group)
			{
				Context = context;
				Group = group ?? new MenuGroup("", "", "", "T");
				MenuMenuItem = new ToolStripMenuItem(text) { Tag = this };
			}
		}

		/// <summary>
		/// A normal node of the virtual tree. This node has text, an icon, and will do something when activated.
		/// </summary>
		private class MenuTreeNode : BaseMenuTreeNode
		{
			private IMenuItem MenuItem { get; set; }

			public override string OrderHint => MenuItem.OrderHint;
			public override string Id => MenuItem.ID;
			public override string DisplayName => MenuItem.Name;
			public override Image DefaultIcon => MenuItem.Icon;
			public override Image IconAtSize(int size) => (MenuItem as CommandMenuItem)?.IconAtSize?.Invoke(size);

			public MenuTreeNode(IContext context, IMenuItem menuItem, MenuGroup group)
			{
				var en = menuItem.IsInContext(context);

				Group = group ?? new MenuGroup("", "", "", "T");
				Context = context;

				MenuItem = menuItem;

				if (menuItem is CommandMenuItem cmi && cmi.HasOptions)
				{
					// Clicking the entry runs the command, clicking the handle on the right opens its options popup
					MenuMenuItem = new OptionsMenuItem(menuItem.Name, menuItem.Icon, p => cmi.ShowOptions(Context, p))
					{
						Tag = this,
						ShortcutKeyDisplayString = menuItem.ShortcutText,
						Enabled = en
					};
				}
				else
				{
					MenuMenuItem = new ToolStripMenuItem(menuItem.Name, menuItem.Icon)
					{
						Tag = this,
						ShortcutKeyDisplayString = menuItem.ShortcutText,
						Enabled = en
					};
				}
				MenuMenuItem.Click += Fire;
				MenuMenuItem.MouseEnter += (s, a) => { Oy.Publish("Status:Information", menuItem.Description); };
				MenuMenuItem.MouseLeave += (s, a) => { Oy.Publish("Status:Information", ""); };

				if (menuItem.AllowedInToolbar)
				{
					ToolbarButton = new ToolStripButton(menuItem.Name, menuItem.Icon)
					{
						Tag = this,
						DisplayStyle = ToolStripItemDisplayStyle.Image,
						Enabled = en
					};
					ToolbarButton.Click += Fire;
					ToolbarButton.MouseEnter += (s, a) => { Oy.Publish("Status:Information", menuItem.Description); };
					ToolbarButton.MouseLeave += (s, a) => { Oy.Publish("Status:Information", ""); };
				}

				if (menuItem.IsToggle)
				{
					MenuMenuItem.CheckState = menuItem.GetToggleState(context) ? CheckState.Checked : CheckState.Unchecked;
					if (ToolbarButton != null) ToolbarButton.CheckState = MenuMenuItem.CheckState;
				}
			}

			private void Fire(object sender, EventArgs e)
			{
				MenuItem?.Invoke(Context).ContinueWith(t => MenuMenuItem.GetCurrentParent()?.InvokeLater(Update));
			}

			public override void Update()
			{
				var en = MenuItem.IsInContext(Context);
				MenuMenuItem.Enabled = en;
				if (ToolbarButton != null) ToolbarButton.Enabled = en;
				if (MenuItem.IsToggle && en)
				{
					var ts = MenuItem.GetToggleState(Context);
					MenuMenuItem.CheckState = ts ? CheckState.Checked : CheckState.Unchecked;
					if (ToolbarButton != null) ToolbarButton.CheckState = MenuMenuItem.CheckState;
				}
				base.Update();
			}
		}

		/// <summary>
		/// A menu entry with an options handle on the right. Clicking the entry itself behaves like a normal
		/// menu item; clicking the handle calls the options callback with the screen position of the entry's right edge.
		/// </summary>
		private class OptionsMenuItem : ToolStripMenuItem
		{
			private const int HandleWidth = 28;

			private readonly Action<Point> _showOptions;
			private bool _overHandle;

			public OptionsMenuItem(string text, Image image, Action<Point> showOptions) : base(text, image)
			{
				_showOptions = showOptions;
			}

			private bool InHandle(Point itemPoint)
			{
				return itemPoint.X >= Bounds.Width - HandleWidth;
			}

			protected override void OnMouseMove(MouseEventArgs mea)
			{
				base.OnMouseMove(mea);
				var over = InHandle(mea.Location);
				if (over != _overHandle)
				{
					_overHandle = over;
					Invalidate();
				}
			}

			protected override void OnMouseLeave(EventArgs e)
			{
				base.OnMouseLeave(e);
				if (_overHandle)
				{
					_overHandle = false;
					Invalidate();
				}
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);

				var r = new Rectangle(Bounds.Width - HandleWidth, 0, HandleWidth, Bounds.Height);
				var col = Enabled ? Color.Black : Color.Gray;

				if (_overHandle && Enabled)
				{
					using (var hb = new SolidBrush(Color.FromArgb(70, Color.Gray)))
					{
						e.Graphics.FillRectangle(hb, r);
					}
				}

				using (var pen = new Pen(Color.FromArgb(120, Color.Gray)))
				{
					e.Graphics.DrawLine(pen, r.Left, r.Top + 3, r.Left, r.Bottom - 4);
				}

				// Right-pointing chevron
				var cx = r.Left + r.Width / 2;
				var cy = r.Top + r.Height / 2;
				using (var brush = new SolidBrush(col))
				{
					e.Graphics.FillPolygon(brush, new[]
					{
						new Point(cx - 2, cy - 4),
						new Point(cx + 3, cy),
						new Point(cx - 2, cy + 4)
					});
				}
			}

			protected override void OnClick(EventArgs e)
			{
				if (Enabled && Owner != null)
				{
					var local = Owner.PointToClient(Control.MousePosition);
					local.Offset(-Bounds.X, -Bounds.Y);
					if (InHandle(local))
					{
						var screen = Owner.PointToScreen(new Point(Bounds.Right, Bounds.Top));
						var sync = System.Threading.SynchronizationContext.Current;

						// Let the File menu finish closing first, otherwise it would take the popup down with it
						if (sync != null) sync.Post(_ => _showOptions(screen), null);
						else _showOptions(screen);
						return;
					}
				}
				base.OnClick(e);
			}
		}

		private abstract class BaseMenuTreeNode
		{
			public MenuGroup Group { get; set; }
			public IContext Context { get; set; }

			public ToolStripMenuItem MenuMenuItem { get; set; }
			public ToolStripButton ToolbarButton { get; set; }

			public List<MenuTreeGroup> Groups { get; protected set; }
			public Dictionary<string, BaseMenuTreeNode> Children { get; private set; }

			public abstract string OrderHint { get; }

			/// <summary>The ID of the underlying menu item, if there is one.</summary>
			public virtual string Id => null;
			public virtual string DisplayName => null;
			public virtual Image DefaultIcon => null;

			/// <summary>The icon rendered natively at the given size (SVG), or null if it can't be.</summary>
			public virtual Image IconAtSize(int size) => null;

			private readonly Dictionary<int, Image> _scaledIcons = new Dictionary<int, Image>();

			/// <summary>The built-in icon resized to a square of the given size.</summary>
			public Image ScaledIcon(int size)
			{
				var src = DefaultIcon;
				if (src == null || (src.Width == size && src.Height == size)) return src;
				if (_scaledIcons.TryGetValue(size, out var cached)) return cached;

				// SVG icons are drawn directly at the target size so they stay sharp
				var native = IconAtSize(size);
				if (native != null && native.Width == size && native.Height == size)
				{
					_scaledIcons[size] = native;
					return native;
				}

				var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
				using (var g = Graphics.FromImage(bmp))
				{
					g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
					g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
					g.DrawImage(src, new Rectangle(0, 0, size, size));
				}
				_scaledIcons[size] = bmp;
				return bmp;
			}

			protected BaseMenuTreeNode()
			{
				Groups = new List<MenuTreeGroup>();
				Children = new Dictionary<string, BaseMenuTreeNode>();
			}

			public void AddChild(string name, BaseMenuTreeNode menuTreeNode)
			{
				Children.Add(name, menuTreeNode);
				if (Groups.All(x => x.Group.Name != menuTreeNode.Group.Name))
				{
					Groups.Add(new MenuTreeGroup(menuTreeNode.Group));
					Groups = Groups.OrderBy(x => x.Group.OrderHint).ToList();
				}

				// Insert the item into the correct index
				var groupIndex = Groups.FindIndex(x => x.Group.Name == menuTreeNode.Group.Name);

				// Skip to the start of the group
				var groupStart = 0;
				for (var i = 0; i < groupIndex; i++)
				{
					var g = Groups[i];
					groupStart += g.Nodes.Count + (g.HasSplitter ? 1 : 0);
				}

				// Add the node to the list and sort
				var group = Groups[groupIndex];
				group.Nodes = group.Nodes.Union(new[] { menuTreeNode }).OrderBy(x => x.OrderHint ?? "").ToList();

				// Skip to the start of the node and insert
				var idx = group.Nodes.IndexOf(menuTreeNode);
				MenuMenuItem.DropDownItems.Insert(groupStart + idx, menuTreeNode.MenuMenuItem);

				// Check groups for splitters
				groupStart = 0;
				for (var i = 0; i < Groups.Count - 1; i++)
				{
					var g = Groups[i];
					groupStart += g.Nodes.Count;

					// Add a splitter to the group if needed
					if (!g.HasSplitter && g.Nodes.Count > 0)
					{
						MenuMenuItem.DropDownItems.Insert(groupStart, new ToolStripSeparator());
						g.HasSplitter = true;
					}

					groupStart++;
				}
			}

			public virtual void Update()
			{
				foreach (var c in Children)
				{
					c.Value.Update();
				}
			}
		}

		private class MenuTreeGroup
		{
			public MenuGroup Group { get; set; }
			public List<BaseMenuTreeNode> Nodes { get; set; }

			public bool HasSplitter { get; set; }

			public MenuTreeGroup(MenuGroup group)
			{
				Group = group;
				Nodes = new List<BaseMenuTreeNode>();
			}
		}
	}
}
