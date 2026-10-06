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
		[Import] private Forms.Shell _shell;
		[Import] private IContext _context;

		[ImportMany] private IEnumerable<Lazy<IMenuItemProvider>> _itemProviders;
		[ImportMany] private IEnumerable<Lazy<IMenuMetadataProvider>> _metaDataProviders;

		public Task OnStartup()
		{
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
			foreach (var md in _metaDataProviders)
			{
				_declaredGroups.AddRange(md.Value.GetMenuGroups());
				_declaredSections.AddRange(md.Value.GetMenuSections());
			}

			_shell.InvokeSync(() =>
			{
				_tree = new VirtualMenuTree(
					_context,
					_shell.MenuStrip,
					_shell.ToolbarContainer,
					_declaredSections,
					_declaredGroups);

				_tree.ResetItems(_menuItems.Values);

				_toolbarSettingsApplied =
					TopToolbarSettings.Loaded;
			});

			Oy.Subscribe<IContext>("Context:Changed", ContextChanged);
			Oy.Subscribe<object>("Menu:Update", UpdateMenu);
			Oy.Subscribe<bool>("Theme:Changed",
				(useDark) => _tree.UseDarkTheme = useDark);

			Oy.Subscribe<object>(
				"SettingsChanged",
				ToolbarSettingsChanged);

			Oy.Subscribe<object>(
				"TopToolbar:Changed",
				ToolbarChanged);

			Oy.Subscribe<object>(
				"Settings:Loaded",
				SettingsLoaded);
		}

		private Task ContextChanged(IContext context)
		{
			return UpdateMenu(context);
		}

		private Task SettingsLoaded(object obj)
		{
			if (!TopToolbarSettings.Loaded)
			{
				return Task.CompletedTask;
			}

			_toolbarSettingsApplied = true;

			_shell.InvokeLater(
				() => _tree?.ApplyToolbarSettings(true));

			return Task.CompletedTask;
		}

		private bool _toolbarSettingsApplied;

		/// <summary>
		/// Any setting in the editor raises SettingsChanged. Only rebuild the
		/// toolbar when one of the toolbar's own settings is different.
		/// </summary>
		private Task ToolbarSettingsChanged(object obj)
		{
			_toolbarSettingsApplied = true;

			_shell.InvokeLater(
				() => _tree?.ApplyToolbarSettings(false));

			return Task.CompletedTask;
		}

		/// <summary>
		/// Raised by the toolbar's own actions (lock, dock, hide button...).
		/// </summary>
		private Task ToolbarChanged(object obj)
		{
			_toolbarSettingsApplied = true;

			_shell.InvokeLater(
				() => _tree?.ApplyToolbarSettings(true));

			return Task.CompletedTask;
		}

		/// <summary>
		/// Every button that can appear in the top toolbar,
		/// in default order.
		/// </summary>
		internal static IReadOnlyList<TopToolbarItemInfo> AllToolbarItems
		{
			get;
			private set;
		} = new List<TopToolbarItemInfo>();

		private async Task UpdateMenu(object obj)
		{
			_shell.InvokeLater(_tree.Update);
		}

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

		private readonly Dictionary<string, IMenuItem> _menuItems;

		private readonly List<MenuSection> _declaredSections;

		private readonly List<MenuGroup> _declaredGroups;

		private VirtualMenuTree _tree;

		public MenuRegister()
		{
			_menuItems =
				new Dictionary<string, IMenuItem>();

			_declaredSections =
				new List<MenuSection>();

			_declaredGroups =
				new List<MenuGroup>();
		}

		private void Add(IMenuItem menuItem)
		{
			_menuItems[menuItem.ID] = menuItem;
		}

		/// <summary>
		/// Handles the virtual menu tree and toolbar.
		/// </summary>
		private class VirtualMenuTree
		{
			private readonly IContext _context;

			private readonly List<MenuSection> _declaredSections;

			private readonly List<MenuGroup> _declaredGroups;

			private bool _useDarkTheme = false;

			public bool UseDarkTheme
			{
				get => _useDarkTheme;

				set
				{
					_useDarkTheme = value;

					ApplyToolbarTheme();

					if (value)
					{
						MenuStrip.Renderer =
							new CustomToolStripRenderer(
								SystemColors.ControlDarkDark,
								_backColor);

						return;
					}

					MenuStrip.Renderer = null;
				}
			}

			private Color _systemDarkBackColor =
				Color.FromArgb(50, 50, 50);

			private Color _backColor =
				Color.FromArgb(70, 70, 70);

			private ToolStripContainer ToolbarContainer
			{
				get;
			}

			// the one strip that holds every button
			private ToolStrip _bar;

			private readonly List<ToolStrip> _joinedStrips =
				new List<ToolStrip>();

			private ContextMenuStrip _toolbarMenu;

			private MenuStrip MenuStrip
			{
				get;
			}

			private Dictionary<string, MenuTreeRoot> RootNodes
			{
				get;
			}

			public VirtualMenuTree(
				IContext context,
				MenuStrip menuStrip,
				ToolStripContainer toolbarContainer,
				List<MenuSection> declaredSections,
				List<MenuGroup> declaredGroups)
			{
				_context = context;
				_declaredSections = declaredSections;
				_declaredGroups = declaredGroups;

				MenuStrip = menuStrip;
				ToolbarContainer = toolbarContainer;

				RootNodes =
					new Dictionary<string, MenuTreeRoot>();

				Clear();
			}

			public void ResetItems(
				IEnumerable<IMenuItem> items)
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

				MenuStrip.Items.AddRange(
					RootNodes.Values
						.OrderBy(x => x.OrderHint)
						.Select(x => x.MenuMenuItem)
						.OfType<ToolStripItem>()
						.ToArray());

				MenuStrip.ResumeLayout();

				if (TopToolbarSettings.Loaded)
				{
					RenderToolbars();
				}
			}

			/// <summary>
			/// Rebuilds the toolbar. Unless forced, nothing happens when the
			/// toolbar settings are the same as the ones already applied.
			/// </summary>
			public void ApplyToolbarSettings(bool force)
			{
				if (!force &&
					_appliedSignature != null &&
					_appliedSignature == ComputeSignature())
				{
					return;
				}

				RenderToolbars();
			}

			private string _appliedSignature;

			private int _renderToken;

			private int _renderedIconSize;

			/// <summary>
			/// Everything the toolbar is built from, as one string.
			/// </summary>
			private static string ComputeSignature()
			{
				var sb = new System.Text.StringBuilder();

				sb.Append(TopToolbarSettings.Dock).Append('|');
				sb.Append(TopToolbarSettings.Locked).Append('|');
				sb.Append(TopToolbarSettings.IconSize).Append('|');

				if (TopToolbarSettings.Layout != null)
				{
					foreach (var e in TopToolbarSettings.Layout)
					{
						if (e == null)
						{
							continue;
						}

						sb.Append(e.Id).Append(',')
							.Append(e.Visible).Append(',')
							.Append(e.IconPath).Append(';');
					}
				}

				sb.Append('|');

				if (TopToolbarSettings.StripPositions != null)
				{
					foreach (var kv in
						TopToolbarSettings.StripPositions
							.OrderBy(
								k => k.Key,
								StringComparer.Ordinal))
					{
						sb.Append(kv.Key).Append('=')
							.Append(
								kv.Value == null
									? ""
									: string.Join(",", kv.Value))
							.Append(';');
					}
				}

				return sb.ToString();
			}

			private bool IsVerticalDock(ToolbarDock dock)
			{
				return dock == ToolbarDock.Left ||
					dock == ToolbarDock.Right;
			}

			/// <summary>
			/// Strip moves caused by a rebuild arrive as queued layout
			/// messages, so only start listening again after they ran.
			/// A newer rebuild cancels the pending release of an older one.
			/// </summary>
			private void EndRendering(int token)
			{
				if (ToolbarContainer.IsHandleCreated)
				{
					ToolbarContainer.BeginInvoke(
						new Action(
							() =>
							{
								if (token == _renderToken)
								{
									_rendering = false;
								}
							}));
				}
				else
				{
					_rendering = false;
				}
			}

			private ToolStripPanel PanelFor(
				ToolbarDock dock)
			{
				switch (dock)
				{
					case ToolbarDock.Bottom:
						return ToolbarContainer.BottomToolStripPanel;

					case ToolbarDock.Left:
						return ToolbarContainer.LeftToolStripPanel;

					case ToolbarDock.Right:
						return ToolbarContainer.RightToolStripPanel;

					default:
						return ToolbarContainer.TopToolStripPanel;
				}
			}

			private void DetachToolbars()
			{
				foreach (var strip in _joinedStrips)
				{
					strip.Parent?.Controls.Remove(strip);
				}

				_joinedStrips.Clear();
			}

			/// <summary>
			/// Gives a toolbar button the user's icon (or the built-in one)
			/// at the toolbar's icon size.
			/// </summary>
			private static void ConfigureButton(
				BaseMenuTreeNode node,
				TopToolbarEntry entry,
				int iconSize)
			{
				Image icon = null;

				if (!string.IsNullOrWhiteSpace(
					entry.IconPath))
				{
					icon =
						IconLoader.LoadFromFile(
							entry.IconPath,
							iconSize);
				}

				var button = node.ToolbarButton;

				button.DisplayStyle =
					ToolStripItemDisplayStyle.Image;

				button.ImageScaling =
					ToolStripItemImageScaling.None;

				button.ImageAlign =
					ContentAlignment.MiddleCenter;

				button.Image =
					icon ??
					node.ScaledIcon(iconSize);

				button.AutoSize = false;

				button.Width = iconSize + 4;

				button.Height = iconSize + 4;
			}

			/// <summary>
			/// Default left-to-right order of the toolbar strips. Sections that
			/// are not listed come after these, ordered by their order hint.
			/// </summary>
			private static readonly string[] DefaultSectionOrder =
			{
				"File",
				"Edit",
				"View",
				"Map",
				"Tools"
			};

			private static int SectionRank(MenuTreeRoot root)
			{
				var i =
					Array.IndexOf(
						DefaultSectionOrder,
						root.SectionId);

				return i < 0
					? DefaultSectionOrder.Length
					: i;
			}

			private void RenderToolbars()
			{
				_rendering = true;

				var renderToken = ++_renderToken;

				_captureTimer?.Stop();

				DetachToolbars();

				var dock = TopToolbarSettings.Dock;

				var vertical =
					dock == ToolbarDock.Left ||
					dock == ToolbarDock.Right;

				var panel = PanelFor(dock);

				AllToolbarItems =
					RootNodes.Values
						.OrderBy(x => SectionRank(x))
						.ThenBy(x => x.OrderHint)
						.SelectMany(
							r => r.ToolbarNodes.Select(
								n => new TopToolbarItemInfo
								{
									Id = n.Id,
									Name = n.DisplayName,
									Section = r.SectionName,
									Group = n.Group?.Name ?? "",
									DefaultIcon = n.DefaultIcon,
									IconAtSize = n.ScaledIcon
								}))
						.ToList();

				/*
				 * Layouts saved by older builds could have an arbitrary
				 * section order (new buttons were appended at the end).
				 * Once, put them in the default order and keep each
				 * button's visibility and custom icon.
				 */
				if (TopToolbarSettings.ResetOrder &&
					AllToolbarItems.Count > 0)
				{
					var fresh =
						new TopToolbarLayout().Resolve(
							AllToolbarItems.Select(x => x.Id));

					foreach (var entry in fresh)
					{
						var old =
							TopToolbarSettings.Layout.Find(entry.Id);

						if (old != null)
						{
							entry.Visible = old.Visible;
							entry.IconPath = old.IconPath;
						}
					}

					TopToolbarSettings.Layout = fresh;

					TopToolbarSettings.StripPositions =
						new Dictionary<string, int[]>();

					TopToolbarSettings.ResetOrder = false;

					TopToolbarSettings.SeparatorsMigrated = false;

					Oy.Publish("Settings:Save");
				}

				/*
				 * Resolve the saved layout against the currently
				 * available toolbar items.
				 *
				 * The returned layout is deliberately used for all
				 * ordering decisions below.
				 */
				var layout =
					TopToolbarSettings.Layout.Resolve(
						AllToolbarItems.Select(x => x.Id));

				// the toolbar used to put a divider between menu groups by itself;
				// now the dividers are entries of the layout. Once, put them where
				// those automatic ones used to be.
				if (!TopToolbarSettings.SeparatorsMigrated &&
					AllToolbarItems.Count > 0)
				{
					layout =
						layout.WithDefaultSeparators(
							AllToolbarItems);

					TopToolbarSettings.Layout = layout;

					TopToolbarSettings.SeparatorsMigrated = true;

					Oy.Publish("Settings:Save");
				}

				if (_toolbarMenu == null)
				{
					_toolbarMenu =
						BuildToolbarMenu();
				}

				var iconSize =
					TopToolbarSettings.IconSize;

				// the saved strip positions are pixels; they only fit the icon
				// size they were saved with, so a new size starts from a fresh row
				if (_renderedIconSize != 0 &&
					_renderedIconSize != iconSize &&
					TopToolbarSettings.StripPositions != null &&
					TopToolbarSettings.StripPositions.Count > 0)
				{
					TopToolbarSettings.StripPositions =
						new Dictionary<string, int[]>();

					Oy.Publish("Settings:Save");
				}

				_renderedIconSize = iconSize;

				panel.BeginInit();

				/*
				 * Every button lives on one strip. The strip always fills its
				 * row (Stretch), stays on one line, and puts the buttons that do
				 * not fit behind its overflow arrow. There are no rows and no
				 * saved positions for a window resize to break.
				 */
				if (_bar == null)
				{
					_bar = new BufferedToolStrip();
				}

				var bar = _bar;

				while (bar.Items.Count > 0)
				{
					bar.Items.RemoveAt(0);
				}

				// the saved layout is the toolbar, in order: buttons and dividers
				var nodes =
					RootNodes.Values
						.SelectMany(r => r.ToolbarNodes)
						.GroupBy(n => n.Id)
						.ToDictionary(
							g => g.Key,
							g => g.First());

				var dividerWaiting = false;

				foreach (var entry in layout)
				{
					if (entry.IsSeparator())
					{
						// only between two buttons: never first, last or doubled
						dividerWaiting = bar.Items.Count > 0;

						continue;
					}

					if (!entry.Visible ||
						!nodes.TryGetValue(
							entry.Id,
							out var node))
					{
						continue;
					}

					ConfigureButton(
						node,
						entry,
						iconSize);

					if (dividerWaiting)
					{
						bar.Items.Add(
							new ToolStripSeparator());

						dividerWaiting = false;
					}

					bar.Items.Add(node.ToolbarButton);
				}

				if (bar.Items.Count > 0)
				{
					bar.ImageScalingSize =
						new Size(iconSize, iconSize);

					WireDragDrop(bar);

					bar.LayoutStyle =
						vertical
							? ToolStripLayoutStyle
								.VerticalStackWithOverflow
							: ToolStripLayoutStyle
								.HorizontalStackWithOverflow;

					// the bar is not moved around; the buttons are (drag and drop)
					bar.GripStyle =
						ToolStripGripStyle.Hidden;

					bar.ContextMenuStrip =
						_toolbarMenu;

					bar.CanOverflow = true;

					bar.Stretch = true;

					bar.AutoSize = true;

					bar.PerformLayout();

					panel.Join(bar);

					_joinedStrips.Add(bar);
				}

				panel.EndInit();

				panel.ContextMenuStrip =
					_toolbarMenu;

				ApplyToolbarTheme();

				panel.PerformLayout();

				foreach (var strip in _joinedStrips)
				{
					strip.Invalidate();
				}

				panel.Invalidate(true);

				_renderedDock = dock;

				// the rows may have changed height, the rest of the window
				// (dock panels, viewports) has to follow
				ToolbarContainer.PerformLayout();

				_appliedSignature = ComputeSignature();

				EndRendering(renderToken);
			}

			private void ApplyToolbarTheme()
			{
				if (_joinedStrips == null)
				{
					return;
				}

				var dark = UseDarkTheme;

				var back =
					dark
						? _systemDarkBackColor
						: SystemColors.Control;

				var fore =
					dark
						? Color.White
						: SystemColors.ControlText;

				foreach (var strip in _joinedStrips)
				{
					strip.Renderer =
						new ToolbarRenderer(dark);

					strip.BackColor = back;
					strip.ForeColor = fore;

					strip.Invalidate();
				}
			}

			private readonly HashSet<ToolStrip> _wired =
				new HashSet<ToolStrip>();

			private Point _dragStart;

			private string _dragId;

			private static string NodeId(
				ToolStripItem item)
			{
				return (
					item?.Tag as BaseMenuTreeNode
				)?.Id;
			}

			private static string DroppedIconFile(
				DragEventArgs e)
			{
				if (!e.Data.GetDataPresent(
					DataFormats.FileDrop))
				{
					return null;
				}

				var files =
					e.Data.GetData(
						DataFormats.FileDrop)
					as string[];

				if (files == null ||
					files.Length != 1)
				{
					return null;
				}

				var ext =
					System.IO.Path
						.GetExtension(files[0])
						?.ToLowerInvariant();

				return new[]
				{
					".svg",
					".png",
					".ico",
					".bmp",
					".jpg",
					".gif"
				}.Contains(ext)
					? files[0]
					: null;
			}

			private bool _rendering;

			private ToolbarDock _renderedDock = ToolbarDock.Top;

			private System.Windows.Forms.Timer _captureTimer;

			/// <summary>
			/// Called whenever a strip moves. Waits until the movement has
			/// settled, then records where the strips ended up. Strip moves
			/// made by RenderToolbars itself are ignored.
			/// </summary>
			private void ScheduleCapture()
			{
				if (_rendering)
				{
					return;
				}

				if (_captureTimer == null)
				{
					_captureTimer =
						new System.Windows.Forms.Timer
						{
							Interval = 400
						};

					_captureTimer.Tick +=
						(s, e) =>
						{
							_captureTimer.Stop();
							CaptureStripLayout();
						};
				}

				_captureTimer.Stop();
				_captureTimer.Start();
			}

			/// <summary>
			/// Stores the strips' on-screen order (as part of the saved layout)
			/// and their positions, then saves. No rebuild is triggered.
			/// </summary>
			private void CaptureStripLayout()
			{
				if (_rendering || _joinedStrips.Count == 0)
				{
					return;
				}

				var vertical =
					_renderedDock == ToolbarDock.Left ||
					_renderedDock == ToolbarDock.Right;

				var strips =
					vertical
						? _joinedStrips
							.OrderBy(x => x.Location.X)
							.ThenBy(x => x.Location.Y)
							.ToList()
						: _joinedStrips
							.OrderBy(x => x.Location.Y)
							.ThenBy(x => x.Location.X)
							.ToList();

				var current =
					TopToolbarSettings.Layout.Resolve(
						AllToolbarItems.Select(x => x.Id));

				var result = new TopToolbarLayout();

				// strips in on-screen order, each strip keeps its own button order
				foreach (var strip in strips)
				{
					var root = strip.Tag as MenuTreeRoot;

					if (root == null)
					{
						continue;
					}

					var ids =
						new HashSet<string>(
							root.ToolbarNodes.Select(n => n.Id));

					foreach (var entry in current)
					{
						if (ids.Contains(entry.Id) &&
							result.Find(entry.Id) == null)
						{
							result.Add(entry.Clone());
						}
					}
				}

				// entries without a visible strip keep their place at the end
				foreach (var entry in current)
				{
					if (result.Find(entry.Id) == null)
					{
						result.Add(entry.Clone());
					}
				}

				var positions =
					new Dictionary<string, int[]>();

				if (!vertical)
				{
					foreach (var strip in strips)
					{
						var root = strip.Tag as MenuTreeRoot;

						if (root != null)
						{
							positions[root.SectionName] =
								new[]
								{
									strip.Location.X,
									strip.Location.Y
								};
						}
					}
				}

				var sameOrder =
					TopToolbarSettings.Layout
						.Select(x => x.Id)
						.SequenceEqual(
							result.Select(x => x.Id));

				var samePositions =
					vertical ||
					(TopToolbarSettings.StripPositions != null &&
					 TopToolbarSettings.StripPositions.Count ==
						positions.Count &&
					 positions.All(
						p =>
							TopToolbarSettings.StripPositions
								.TryGetValue(
									p.Key,
									out var old) &&
							old != null &&
							old.SequenceEqual(p.Value)));

				if (sameOrder && samePositions)
				{
					return;
				}

				TopToolbarSettings.Layout = result;

				if (!vertical)
				{
					TopToolbarSettings.StripPositions =
						positions;
				}

				_appliedSignature = ComputeSignature();

				Oy.Publish("Settings:Save");
			}

			/// <summary>
			/// Enables toolbar button dragging and custom icon drops.
			/// </summary>
			private void WireDragDrop(
				ToolStrip strip)
			{
				if (!_wired.Add(strip))
				{
					return;
				}

				strip.AllowDrop = true;


				strip.MouseDown += (s, e) =>
				{
					_dragId = null;

					if (e.Button != MouseButtons.Left ||
						TopToolbarSettings.Locked)
					{
						return;
					}

					var item =
						strip.GetItemAt(e.Location);

					if (item is ToolStripButton)
					{
						_dragId =
							NodeId(item);

						_dragStart =
							e.Location;
					}
				};

				strip.MouseUp +=
					(s, e) =>
					{
						_dragId = null;
					};

				strip.MouseMove +=
					(s, e) =>
					{
						if (_dragId == null ||
							e.Button != MouseButtons.Left)
						{
							return;
						}

						var dx =
							Math.Abs(
								e.X -
								_dragStart.X);

						var dy =
							Math.Abs(
								e.Y -
								_dragStart.Y);

						if (dx <
								SystemInformation
									.DragSize.Width &&
							dy <
								SystemInformation
									.DragSize.Height)
						{
							return;
						}

						var id = _dragId;

						_dragId = null;

						strip.DoDragDrop(
							new DataObject(
								"Sledge.TopToolbarButton",
								id),
							DragDropEffects.Move);
					};

				DragEventHandler over =
					(s, e) =>
					{
						var pt =
							strip.PointToClient(
								new Point(
									e.X,
									e.Y));

						if (e.Data.GetDataPresent(
								"Sledge.TopToolbarButton") &&
							!TopToolbarSettings.Locked)
						{
							e.Effect =
								DragDropEffects.Move;
						}
						else if (
							DroppedIconFile(e) != null &&
							strip.GetItemAt(pt)
								is ToolStripButton)
						{
							e.Effect =
								DragDropEffects.Copy;
						}
						else
						{
							e.Effect =
								DragDropEffects.None;
						}
					};

				strip.DragEnter += over;
				strip.DragOver += over;

				strip.DragDrop +=
					(s, e) =>
					{
						var pt =
							strip.PointToClient(
								new Point(
									e.X,
									e.Y));

						var target =
							strip.GetItemAt(pt)
								as ToolStripButton;

						var layout =
							TopToolbarSettings.Layout
								.Resolve(
									AllToolbarItems
										.Select(x => x.Id));

						if (e.Data.GetDataPresent(
							"Sledge.TopToolbarButton"))
						{
							var id =
								e.Data.GetData(
									"Sledge.TopToolbarButton")
								as string;

							var targetId =
								NodeId(target);

							if (id == null ||
								targetId == null ||
								id == targetId)
							{
								return;
							}

							var moving =
								layout.Find(id);

							if (moving == null)
							{
								return;
							}

							var targetEntry =
								layout.Find(targetId);

							if (targetEntry == null)
							{
								return;
							}

							/*
							 * Remove the moving item first.
							 * This makes the insertion index correct
							 * whether the item stays in its section or
							 * moves into another section.
							 */
							layout.Remove(moving);

							var idx =
								layout.FindIndex(
									x =>
										x.Id ==
										targetId);

							if (idx < 0)
							{
								return;
							}

							var after =
								strip.LayoutStyle ==
									ToolStripLayoutStyle
										.VerticalStackWithOverflow
										? pt.Y >
											target.Bounds.Top +
											target.Bounds.Height / 2
										: pt.X >
											target.Bounds.Left +
											target.Bounds.Width / 2;

							layout.Insert(
								after
									? idx + 1
									: idx,
								moving);

							/*
							 * This is the important persistence step.
							 * The complete layout is stored, not just
							 * the current strip's local order.
							 */
							TopToolbarSettings.Layout =
								layout;

							ToolbarChanged();

							return;
						}

						var file =
							DroppedIconFile(e);

						var targetIdForIcon =
							NodeId(target);

						if (file == null ||
							targetIdForIcon == null)
						{
							return;
						}

						if (IconLoader.LoadFromFile(
								file,
								16) == null)
						{
							return;
						}

						var entry =
							layout.Find(
								targetIdForIcon);

						if (entry == null)
						{
							return;
						}

						entry.IconPath = file;

						TopToolbarSettings.Layout =
							layout;

						ToolbarChanged();
					};
			}

			/// <summary>
			/// Builds the toolbar context menu.
			/// </summary>
			private ContextMenuStrip BuildToolbarMenu()
			{
				var menu =
					new ContextMenuStrip();

				var dockItems =
					new List<ToolStripMenuItem>();

				foreach (
					ToolbarDock d
					in Enum.GetValues(
						typeof(ToolbarDock)))
				{
					var dock = d;

					var item =
						new ToolStripMenuItem(
							"Dock " +
							dock.ToString()
								.ToLowerInvariant())
						{
							Tag = dock
						};

					item.Click +=
						(s, e) =>
						{
							TopToolbarSettings.Dock =
								dock;

							ToolbarChanged();
						};

					dockItems.Add(item);
					menu.Items.Add(item);
				}

				menu.Items.Add(
					new ToolStripSeparator());

				var lockItem =
					new ToolStripMenuItem(
						"Lock toolbar");

				lockItem.Click +=
					(s, e) =>
					{
						/*
						 * DO NOT call ToolbarChanged() here.
						 *
						 * ToolbarChanged() publishes TopToolbar:Changed,
						 * which causes the entire toolbar to be rebuilt.
						 *
						 * Locking should only change the grip state.
						 */
						TopToolbarSettings.Locked =
							!TopToolbarSettings.Locked;

						/*
						 * Save the setting without asking the toolbar
						 * to reconstruct itself.
						 */
						Oy.Publish("Settings:Save");

						foreach (var strip in _joinedStrips)
						{
							strip.GripStyle =
								TopToolbarSettings.Locked
									? ToolStripGripStyle.Hidden
									: ToolStripGripStyle.Visible;
						}
					};

				menu.Items.Add(lockItem);

				menu.Opening +=
					(s, e) =>
					{
						foreach (var di in dockItems)
						{
							di.Checked =
								(ToolbarDock)di.Tag ==
								TopToolbarSettings.Dock;
						}

						lockItem.Checked =
							TopToolbarSettings.Locked;
					};

				return menu;
			}

			private static void ToolbarChanged()
			{
				/*
				 * Dragging/docking/icon changes need a full toolbar
				 * rebuild, because the actual ToolStrip contents changed.
				 *
				 * The layout is already stored in TopToolbarSettings.Layout
				 * before this method is called.
				 */
				Oy.Publish("Settings:Save");
				Oy.Publish(
					"TopToolbar:Changed",
					new object());
			}

			private void AddSection(
				MenuSection ds)
			{
				var rtn =
					new MenuTreeRoot(
						_context,
						ds.Description,
						ds);

				rtn.MenuMenuItem.DropDownClosed +=
					(s, a) =>
					{
						Oy.Publish(
							"Status:Information",
							"");
					};

				rtn.MenuMenuItem.DropDownOpening +=
					(s, a) =>
					{
						rtn.Update();
					};

				RootNodes.Add(
					ds.Name,
					rtn);
			}

			private void Add(IMenuItem item)
			{
				if (!RootNodes.ContainsKey(
					item.Section))
				{
					AddSection(
						new MenuSection(
							item.Section,
							item.Section,
							"Z"));
				}

				var root =
					RootNodes[item.Section];

				root.AddDescendant(
					item,
					_declaredGroups);
			}

			public void Clear()
			{
				MenuStrip.Items.Clear();

				DetachToolbars();

				RootNodes.Clear();

				foreach (
					var ds in _declaredSections
						.OrderBy(x => x.OrderHint))
				{
					AddSection(ds);
				}
			}

			public void Update()
			{
				foreach (var node in RootNodes.Values)
				{
					node.Update();
				}

				foreach (var strip in _joinedStrips)
				{
					strip.PerformLayout();
					strip.Invalidate(true);
					strip.Update();
				}
			}
		}

		private class BufferedToolStrip : ToolStrip
		{
			public BufferedToolStrip()
			{
				SetStyle(
					ControlStyles.OptimizedDoubleBuffer |
					ControlStyles.AllPaintingInWmPaint |
					ControlStyles.UserPaint,
					true);

				DoubleBuffered = true;
			}
		}

		private class ToolbarRenderer :
			ToolStripProfessionalRenderer
		{
			private readonly Color _checked;
			private readonly Color _checkedBorder;
			private readonly Color _hover;
			private readonly Color _pressed;

			public ToolbarRenderer(bool dark)
			{
				RoundedEdges = false;

				if (dark)
				{
					_checked =
						Color.FromArgb(
							105,
							105,
							105);

					_checkedBorder =
						Color.FromArgb(
							140,
							140,
							140);

					_hover =
						Color.FromArgb(
							80,
							80,
							80);

					_pressed =
						Color.FromArgb(
							125,
							125,
							125);
				}
				else
				{
					_checked =
						Color.FromArgb(
							200,
							200,
							200);

					_checkedBorder =
						Color.FromArgb(
							140,
							140,
							140);

					_hover =
						Color.FromArgb(
							225,
							225,
							225);

					_pressed =
						Color.FromArgb(
							180,
							180,
							180);
				}
			}

			protected override void OnRenderToolStripBackground(
				ToolStripRenderEventArgs e)
			{
				using (var brush =
					new SolidBrush(
						e.ToolStrip.BackColor))
				{
					e.Graphics.FillRectangle(
						brush,
						e.AffectedBounds);
				}
			}

			protected override void OnRenderToolStripBorder(
				ToolStripRenderEventArgs e)
			{
			}

			protected override void OnRenderButtonBackground(
				ToolStripItemRenderEventArgs e)
			{
				var item =
					e.Item as ToolStripButton;

				if (item == null ||
					!item.Enabled)
				{
					if (item == null ||
						!item.Checked)
					{
						return;
					}
				}

				var rect =
					new Rectangle(
						0,
						0,
						e.Item.Width - 1,
						e.Item.Height - 1);

				Color? fill = null;

				if (item.Pressed)
				{
					fill = _pressed;
				}
				else if (item.Checked)
				{
					fill =
						item.Selected
							? _pressed
							: _checked;
				}
				else if (item.Selected)
				{
					fill = _hover;
				}

				if (fill == null)
				{
					return;
				}

				using (var brush =
					new SolidBrush(fill.Value))
				{
					e.Graphics.FillRectangle(
						brush,
						rect);
				}

				if (item.Checked)
				{
					using (var pen =
						new Pen(_checkedBorder))
					{
						e.Graphics.DrawRectangle(
							pen,
							rect);
					}
				}
			}
		}

		public class CustomToolStripRenderer :
			ToolStripProfessionalRenderer
		{
			private readonly Color _pressedBackColor;
			private readonly Color _backColor;

			private readonly Brush _dropDownEnabledColor =
				new SolidBrush(Color.Black);

			private readonly Brush _dropDownDisabledColor =
				new SolidBrush(Color.Gray);

			public CustomToolStripRenderer(
				Color pressedBackColor,
				Color backColor)
			{
				_pressedBackColor =
					pressedBackColor;

				_backColor =
					backColor;
			}

			protected override void OnRenderItemBackground(
				ToolStripItemRenderEventArgs e)
			{
				using (var brush =
					new SolidBrush(Color.Black))
				{
					e.Graphics.FillRectangle(
						brush,
						e.Item.ContentRectangle);
				}
			}

			protected override void OnRenderToolStripPanelBackground(
				ToolStripPanelRenderEventArgs e)
			{
				base.OnRenderToolStripPanelBackground(e);
			}

			protected override void OnRenderToolStripBorder(
				ToolStripRenderEventArgs e)
			{
				if (e.ToolStrip is ToolStripDropDown)
				{
					using (var pen =
						new Pen(
							ControlPaint.Dark(
								_backColor),
							1))
					{
						e.Graphics.DrawRectangle(
							pen,
							new Rectangle(
								Point.Empty,
								e.ToolStrip.ClientSize -
								new Size(1, 1)));
					}
				}
				else
				{
					base.OnRenderToolStripBorder(e);
				}
			}

			protected override void OnRenderImageMargin(
				ToolStripRenderEventArgs e)
			{
				return;
			}

			protected override void OnRenderItemImage(
				ToolStripItemImageRenderEventArgs e)
			{
				base.OnRenderItemImage(e);
			}

			protected override void OnRenderSeparator(
				ToolStripSeparatorRenderEventArgs e)
			{
				using (var pen =
					new Pen(
						ControlPaint.Dark(
							_backColor),
						1))
				{
					int y =
						e.Item.ContentRectangle.Top +
						e.Item.ContentRectangle.Height / 2;

					e.Graphics.DrawLine(
						pen,
						e.Item.ContentRectangle.Left,
						y,
						e.Item.ContentRectangle.Right,
						y);
				}
			}

			protected override void OnRenderToolStripBackground(
				ToolStripRenderEventArgs e)
			{
				if (e.ToolStrip is ToolStripDropDown)
				{
					e.ToolStrip.BackColor =
						_backColor;

					using (var brush =
						new SolidBrush(_backColor))
					{
						e.Graphics.FillRectangle(
							brush,
							e.ToolStrip.DisplayRectangle);

						e.Graphics.DrawRectangle(
							SystemPens.ControlDarkDark,
							e.ToolStrip.ClientRectangle);
					}
				}
				else
				{
					base.OnRenderToolStripBackground(e);
				}
			}

			protected override void OnRenderItemText(
				ToolStripItemTextRenderEventArgs e)
			{
				if (e.Item.Owner is ToolStripDropDown)
				{
					if (e.Item is ToolStripMenuItem menuItem)
					{
						if (!e.Item.Enabled)
						{
							e.Graphics.DrawString(
								menuItem.Text,
								e.TextFont,
								_dropDownDisabledColor,
								e.TextRectangle);
						}
						else
						{
							e.Graphics.DrawString(
								menuItem.Text,
								e.TextFont,
								_dropDownEnabledColor,
								e.TextRectangle);
						}

						if (!string.IsNullOrEmpty(
							menuItem.ShortcutKeyDisplayString))
						{
							SizeF shortcutTextSize =
								e.Graphics.MeasureString(
									menuItem.ShortcutKeyDisplayString,
									e.TextFont);

							float shortcutX =
								e.Item.ContentRectangle.Right -
								shortcutTextSize.Width -
								10;

							RectangleF shortcutRect =
								new RectangleF(
									shortcutX,
									e.Item.ContentRectangle.Top,
									shortcutTextSize.Width,
									shortcutTextSize.Height);

							if (e.Item.Enabled)
							{
								e.Graphics.DrawString(
									menuItem.ShortcutKeyDisplayString,
									e.TextFont,
									_dropDownEnabledColor,
									shortcutRect);

								return;
							}

							e.Graphics.DrawString(
								menuItem.ShortcutKeyDisplayString,
								e.TextFont,
								_dropDownDisabledColor,
								shortcutRect);

							return;
						}
					}
				}
				else if (e.Item.Owner is MenuStrip)
				{
					using (Brush textBrush =
						new SolidBrush(Color.White))
					{
						e.Graphics.DrawString(
							e.Text,
							e.TextFont,
							textBrush,
							e.TextRectangle);
					}
				}
				else
				{
					base.OnRenderItemText(e);
				}
			}

			protected override void OnRenderMenuItemBackground(
				ToolStripItemRenderEventArgs e)
			{
				if (e.Item.Pressed)
				{
					using (var brush =
						new SolidBrush(
							_pressedBackColor))
					{
						e.Graphics.FillRectangle(
							brush,
							e.Item.ContentRectangle);
					}
				}
				else if (e.Item.Selected)
				{
					using (var brush =
						new SolidBrush(
							Color.DimGray))
					{
						e.Graphics.FillRectangle(
							brush,
							e.Item.ContentRectangle);
					}
				}
				else
				{
					base.OnRenderMenuItemBackground(e);
				}
			}
		}

		private class MenuTreeRoot :
			BaseMenuTreeNode
		{
			private MenuSection Section
			{
				get;
			}

			public ToolStrip ToolStrip
			{
				get;
			}

			public override string OrderHint =>
				Section.OrderHint;

			public string SectionId => Section.Name;

			public string SectionName =>
				Section.Description;

			private List<MenuTreeGroup> _toolbarGroups;

			public IEnumerable<BaseMenuTreeNode> ToolbarNodes =>
				_toolbarGroups
					.SelectMany(g => g.Nodes)
					.Where(
						n => n.ToolbarButton != null);

			public MenuTreeRoot(
				IContext context,
				string text,
				MenuSection section)
			{
				Section = section;

				MenuMenuItem =
					new ToolStripMenuItem(text)
					{
						Tag = this
					};

				ToolStrip =
					new BufferedToolStrip
					{
						Tag = this,
						LayoutStyle =
							ToolStripLayoutStyle
								.HorizontalStackWithOverflow
					};

				Context = context;

				_toolbarGroups =
					new List<MenuTreeGroup>();
			}

			public void AddDescendant(
				IMenuItem item,
				List<MenuGroup> declaredGroups)
			{
				BaseMenuTreeNode node = this;

				var path =
					(item.Path ?? "")
						.Split('/')
						.Where(x => x.Length > 0)
						.ToList();

				var currentPath =
					new List<string>();

				foreach (var p in path)
				{
					currentPath.Add(p);

					if (!node.Children.ContainsKey(p))
					{
						var gr =
							declaredGroups.FirstOrDefault(
								x =>
									x.Name == p &&
									x.Path ==
										String.Join(
											"/",
											currentPath) &&
									x.Section ==
										item.Section);

						node.AddChild(
							p,
							new MenuTreeTextNode(
								Context,
								gr?.Description ?? p,
								gr));
					}

					node =
						node.Children[p];
				}

				var group =
					declaredGroups.FirstOrDefault(
						x =>
							x.Name == item.Group &&
							x.Path == item.Path &&
							x.Section == item.Section);

				var itemNode =
					new MenuTreeNode(
						Context,
						item,
						group);

				node.AddChild(
					item.ID,
					itemNode);

				if (item.AllowedInToolbar &&
					item.Icon != null)
				{
					AddToolbarItem(itemNode);
				}
			}

			private void AddToolbarItem(
				BaseMenuTreeNode menuTreeNode)
			{
				if (_toolbarGroups.All(
					x =>
						x.Group.Name !=
						menuTreeNode.Group.Name))
				{
					_toolbarGroups.Add(
						new MenuTreeGroup(
							menuTreeNode.Group));

					_toolbarGroups =
						_toolbarGroups
							.OrderBy(
								x => x.Group.OrderHint)
							.ToList();
				}

				var groupIndex =
					_toolbarGroups.FindIndex(
						x =>
							x.Group.Name ==
							menuTreeNode.Group.Name);

				var groupStart = 0;

				for (var i = 0;
					i < groupIndex;
					i++)
				{
					var g =
						_toolbarGroups[i];

					groupStart +=
						g.Nodes.Count +
						(g.HasSplitter
							? 1
							: 0);
				}

				var group =
					_toolbarGroups[groupIndex];

				group.Nodes =
					group.Nodes
						.Union(
							new[]
							{
								menuTreeNode
							})
						.OrderBy(
							x =>
								x.OrderHint ?? "")
						.ToList();

				var idx =
					group.Nodes.IndexOf(
						menuTreeNode);

				ToolStrip.Items.Insert(
					groupStart + idx,
					menuTreeNode.ToolbarButton);

				groupStart = 0;

				for (var i = 0;
					i < _toolbarGroups.Count - 1;
					i++)
				{
					var g =
						_toolbarGroups[i];

					groupStart +=
						g.Nodes.Count;

					if (!g.HasSplitter &&
						g.Nodes.Count > 0)
					{
						ToolStrip.Items.Insert(
							groupStart,
							new ToolStripSeparator());

						g.HasSplitter = true;
					}

					groupStart++;
				}
			}
		}

		private class MenuTreeTextNode :
			BaseMenuTreeNode
		{
			public override string OrderHint =>
				Group.OrderHint;

			public MenuTreeTextNode(
				IContext context,
				string text,
				MenuGroup group)
			{
				Context = context;

				Group =
					group ??
					new MenuGroup(
						"",
						"",
						"",
						"T");

				MenuMenuItem =
					new ToolStripMenuItem(text)
					{
						Tag = this
					};
			}
		}

		private class MenuTreeNode :
			BaseMenuTreeNode
		{
			private IMenuItem MenuItem
			{
				get;
			}

			public override string OrderHint =>
				MenuItem.OrderHint;

			public override string Id =>
				MenuItem.ID;

			public override string DisplayName =>
				MenuItem.Name;

			public override Image DefaultIcon =>
				MenuItem.Icon;

			public override Image IconAtSize(
				int size) =>
				(MenuItem as CommandMenuItem)
					?.IconAtSize
					?.Invoke(size);

			public MenuTreeNode(
				IContext context,
				IMenuItem menuItem,
				MenuGroup group)
			{
				var en =
					menuItem.IsInContext(
						context);

				Group =
					group ??
					new MenuGroup(
						"",
						"",
						"",
						"T");

				Context = context;

				MenuItem = menuItem;

				if (menuItem is CommandMenuItem cmi &&
					cmi.HasOptions)
				{
					MenuMenuItem =
						new OptionsMenuItem(
							menuItem.Name,
							menuItem.Icon,
							p =>
								cmi.ShowOptions(
									Context,
									p))
						{
							Tag = this,
							ShortcutKeyDisplayString =
								menuItem.ShortcutText,
							Enabled = en
						};
				}
				else
				{
					MenuMenuItem =
						new ToolStripMenuItem(
							menuItem.Name,
							menuItem.Icon)
						{
							Tag = this,
							ShortcutKeyDisplayString =
								menuItem.ShortcutText,
							Enabled = en
						};
				}

				MenuMenuItem.Click += Fire;

				MenuMenuItem.MouseEnter +=
					(s, a) =>
					{
						Oy.Publish(
							"Status:Information",
							menuItem.Description);
					};

				MenuMenuItem.MouseLeave +=
					(s, a) =>
					{
						Oy.Publish(
							"Status:Information",
							"");
					};

				if (menuItem.AllowedInToolbar)
				{
					ToolbarButton =
						new ToolStripButton(
							menuItem.Name,
							menuItem.Icon)
						{
							Tag = this,
							DisplayStyle =
								ToolStripItemDisplayStyle
									.Image,
							Enabled = en
						};

					ToolbarButton.Click += Fire;

					ToolbarButton.MouseEnter +=
						(s, a) =>
						{
							Oy.Publish(
								"Status:Information",
								menuItem.Description);
						};

					ToolbarButton.MouseLeave +=
						(s, a) =>
						{
							Oy.Publish(
								"Status:Information",
								"");
						};
				}

				if (menuItem.IsToggle)
				{
					MenuMenuItem.CheckState =
						menuItem.GetToggleState(
							context)
							? CheckState.Checked
							: CheckState.Unchecked;

					if (ToolbarButton != null)
					{
						ToolbarButton.CheckState =
							MenuMenuItem.CheckState;
					}
				}
			}

			private void Fire(
				object sender,
				EventArgs e)
			{
				var owner =
					(sender as ToolStripItem)
						?.GetCurrentParent()
					?? ToolbarButton
						?.GetCurrentParent()
					?? MenuMenuItem
						.GetCurrentParent();

				MenuItem?.Invoke(Context)
					.ContinueWith(
						t =>
							owner?.InvokeLater(
								Update));
			}

			public override void Update()
			{
				var en =
					MenuItem.IsInContext(
						Context);

				MenuMenuItem.Enabled = en;

				if (ToolbarButton != null)
				{
					ToolbarButton.Enabled = en;
				}

				if (MenuItem.IsToggle && en)
				{
					var ts =
						MenuItem.GetToggleState(
							Context);

					MenuMenuItem.CheckState =
						ts
							? CheckState.Checked
							: CheckState.Unchecked;

					if (ToolbarButton != null &&
						ToolbarButton.CheckState !=
							MenuMenuItem.CheckState)
					{
						ToolbarButton.CheckState =
							MenuMenuItem.CheckState;

						ToolbarButton.Invalidate();

						ToolbarButton
							.GetCurrentParent()
							?.Invalidate();
					}
				}

				base.Update();
			}
		}

		private class OptionsMenuItem :
			ToolStripMenuItem
		{
			private const int HandleWidth = 28;

			private readonly Action<Point> _showOptions;

			private bool _overHandle;

			public OptionsMenuItem(
				string text,
				Image image,
				Action<Point> showOptions)
				: base(text, image)
			{
				_showOptions = showOptions;
			}

			private bool InHandle(
				Point itemPoint)
			{
				return itemPoint.X >=
					Bounds.Width -
					HandleWidth;
			}

			protected override void OnMouseMove(
				MouseEventArgs mea)
			{
				base.OnMouseMove(mea);

				var over =
					InHandle(mea.Location);

				if (over != _overHandle)
				{
					_overHandle = over;
					Invalidate();
				}
			}

			protected override void OnMouseLeave(
				EventArgs e)
			{
				base.OnMouseLeave(e);

				if (_overHandle)
				{
					_overHandle = false;
					Invalidate();
				}
			}

			protected override void OnPaint(
				PaintEventArgs e)
			{
				base.OnPaint(e);

				var r =
					new Rectangle(
						Bounds.Width -
							HandleWidth,
						0,
						HandleWidth,
						Bounds.Height);

				var col =
					Enabled
						? Color.Black
						: Color.Gray;

				if (_overHandle && Enabled)
				{
					using (var hb =
						new SolidBrush(
							Color.FromArgb(
								70,
								Color.Gray)))
					{
						e.Graphics.FillRectangle(
							hb,
							r);
					}
				}

				using (var pen =
					new Pen(
						Color.FromArgb(
							120,
							Color.Gray)))
				{
					e.Graphics.DrawLine(
						pen,
						r.Left,
						r.Top + 3,
						r.Left,
						r.Bottom - 4);
				}

				var cx =
					r.Left +
					r.Width / 2;

				var cy =
					r.Top +
					r.Height / 2;

				using (var brush =
					new SolidBrush(col))
				{
					e.Graphics.FillPolygon(
						brush,
						new[]
						{
							new Point(
								cx - 2,
								cy - 4),

							new Point(
								cx + 3,
								cy),

							new Point(
								cx - 2,
								cy + 4)
						});
				}
			}

			protected override void OnClick(
				EventArgs e)
			{
				if (Enabled &&
					Owner != null)
				{
					var local =
						Owner.PointToClient(
							Control.MousePosition);

					local.Offset(
						-Bounds.X,
						-Bounds.Y);

					if (InHandle(local))
					{
						var screen =
							Owner.PointToScreen(
								new Point(
									Bounds.Right,
									Bounds.Top));

						var sync =
							System.Threading
								.SynchronizationContext
								.Current;

						if (sync != null)
						{
							sync.Post(
								_ =>
									_showOptions(screen),
								null);
						}
						else
						{
							_showOptions(screen);
						}

						return;
					}
				}

				base.OnClick(e);
			}
		}

		private abstract class BaseMenuTreeNode
		{
			public MenuGroup Group
			{
				get;
				set;
			}

			public IContext Context
			{
				get;
				set;
			}

			public ToolStripMenuItem MenuMenuItem
			{
				get;
				set;
			}

			public ToolStripButton ToolbarButton
			{
				get;
				set;
			}

			public List<MenuTreeGroup> Groups
			{
				get;
				protected set;
			}

			public Dictionary<string, BaseMenuTreeNode>
				Children
			{
				get;
				private set;
			}

			public abstract string OrderHint
			{
				get;
			}

			public virtual string Id => null;

			public virtual string DisplayName => null;

			public virtual Image DefaultIcon => null;

			public virtual Image IconAtSize(
				int size) =>
				null;

			private readonly Dictionary<int, Image>
				_scaledIcons =
					new Dictionary<int, Image>();

			public Image ScaledIcon(int size)
			{
				var src = DefaultIcon;

				if (src == null ||
					(src.Width == size &&
					 src.Height == size))
				{
					return src;
				}

				if (_scaledIcons.TryGetValue(
					size,
					out var cached))
				{
					return cached;
				}

				var native =
					IconAtSize(size);

				if (native != null &&
					native.Width == size &&
					native.Height == size)
				{
					_scaledIcons[size] = native;
					return native;
				}

				var bmp =
					new Bitmap(
						size,
						size,
						System.Drawing.Imaging
							.PixelFormat
							.Format32bppArgb);

				using (var g =
					Graphics.FromImage(bmp))
				{
					g.InterpolationMode =
						System.Drawing.Drawing2D
							.InterpolationMode
							.HighQualityBicubic;

					g.PixelOffsetMode =
						System.Drawing.Drawing2D
							.PixelOffsetMode
							.HighQuality;

					g.SmoothingMode =
						System.Drawing.Drawing2D
							.SmoothingMode
							.HighQuality;

					g.DrawImage(
						src,
						new Rectangle(
							0,
							0,
							size,
							size));
				}

				_scaledIcons[size] = bmp;

				return bmp;
			}

			protected BaseMenuTreeNode()
			{
				Groups =
					new List<MenuTreeGroup>();

				Children =
					new Dictionary<
						string,
						BaseMenuTreeNode>();
			}

			public void AddChild(
				string name,
				BaseMenuTreeNode menuTreeNode)
			{
				Children.Add(
					name,
					menuTreeNode);

				if (Groups.All(
					x =>
						x.Group.Name !=
						menuTreeNode.Group.Name))
				{
					Groups.Add(
						new MenuTreeGroup(
							menuTreeNode.Group));

					Groups =
						Groups
							.OrderBy(
								x => x.Group.OrderHint)
							.ToList();
				}

				var groupIndex =
					Groups.FindIndex(
						x =>
							x.Group.Name ==
							menuTreeNode.Group.Name);

				var groupStart = 0;

				for (var i = 0;
					i < groupIndex;
					i++)
				{
					var g = Groups[i];

					groupStart +=
						g.Nodes.Count +
						(g.HasSplitter
							? 1
							: 0);
				}

				var group =
					Groups[groupIndex];

				group.Nodes =
					group.Nodes
						.Union(
							new[]
							{
								menuTreeNode
							})
						.OrderBy(
							x => x.OrderHint ?? "")
						.ToList();

				var idx =
					group.Nodes.IndexOf(
						menuTreeNode);

				MenuMenuItem.DropDownItems.Insert(
					groupStart + idx,
					menuTreeNode.MenuMenuItem);

				groupStart = 0;

				for (var i = 0;
					i < Groups.Count - 1;
					i++)
				{
					var g = Groups[i];

					groupStart +=
						g.Nodes.Count;

					if (!g.HasSplitter &&
						g.Nodes.Count > 0)
					{
						MenuMenuItem.DropDownItems.Insert(
							groupStart,
							new ToolStripSeparator());

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
			public MenuGroup Group
			{
				get;
				set;
			}

			public List<BaseMenuTreeNode> Nodes
			{
				get;
				set;
			}

			public bool HasSplitter
			{
				get;
				set;
			}

			public MenuTreeGroup(
				MenuGroup group)
			{
				Group = group;

				Nodes =
					new List<BaseMenuTreeNode>();
			}
		}
	}
}