using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings
{
	/// <summary>
	/// Which edge of the window the top toolbar (File / Edit / ... button strips) is docked to.
	/// </summary>
	public enum ToolbarDock
	{
		Top,
		Bottom,
		Left,
		Right
	}

	/// <summary>
	/// One button of the top toolbar in the customisation.
	/// </summary>
	public class TopToolbarEntry
	{
		/// <summary>The menu item's ID (IMenuItem.ID)</summary>
		public string Id { get; set; }

		/// <summary>Whether the button is shown. Hidden buttons stay available in the menus and through hotkeys.</summary>
		public bool Visible { get; set; } = true;

		/// <summary>Full path of a user-chosen icon file, or empty to use the built-in icon.</summary>
		public string IconPath { get; set; }

		public TopToolbarEntry Clone()
		{
			return new TopToolbarEntry { Id = Id, Visible = Visible, IconPath = IconPath };
		}

		/// <summary>The ID of an entry that is a divider line between buttons instead of a button</summary>
		public const string SeparatorId = "-";

		public bool IsSeparator()
		{
			return Id == SeparatorId;
		}

		public static TopToolbarEntry NewSeparator()
		{
			return new TopToolbarEntry { Id = SeparatorId };
		}
	}

	/// <summary>
	/// The user's top toolbar layout. The order of the entries is the order of the buttons inside each strip.
	/// </summary>
	public class TopToolbarLayout : List<TopToolbarEntry>
	{
		public TopToolbarLayout Clone()
		{
			var l = new TopToolbarLayout();
			foreach (var e in this) l.Add(e.Clone());
			return l;
		}

		public TopToolbarEntry Find(string id)
		{
			return this.FirstOrDefault(x => x.Id == id);
		}

		/// <summary>
		/// A copy with a divider between neighbouring buttons that belong to different
		/// menu groups (hidden buttons are not counted). Existing dividers are kept.
		/// </summary>
		internal TopToolbarLayout WithDefaultSeparators(IEnumerable<TopToolbarItemInfo> items)
		{
			var infos = items.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
			var result = new TopToolbarLayout();
			string previous = null;
			foreach (var entry in this)
			{
				if (!entry.IsSeparator() && entry.Visible && infos.TryGetValue(entry.Id, out var info))
				{
					var key = info.Section + "/" + info.Group;
					if (previous != null && previous != key) result.Add(TopToolbarEntry.NewSeparator());
					previous = key;
				}
				result.Add(entry.Clone());
			}
			return result;
		}

		/// <summary>
		/// Saved entries first (in saved order, dropping ones that no longer exist).
		/// Buttons without a saved entry are visible and are placed right after
		/// the button that precedes them in the default order, so they stay next
		/// to their neighbours instead of piling up at the end of the list.
		/// <paramref name="ids"/> must be in the default order.
		/// </summary>
		public TopToolbarLayout Resolve(IEnumerable<string> ids)
		{
			var all = ids.ToList();
			var result = new TopToolbarLayout();
			foreach (var e in this)
			{
				if (e?.Id == null) continue;
				// dividers are not buttons: any number of them, wherever the user put them
				if (e.IsSeparator())
				{
					result.Add(e.Clone());
					continue;
				}
				if (!all.Contains(e.Id) || result.Find(e.Id) != null) continue;
				result.Add(e.Clone());
			}
			for (var i = 0; i < all.Count; i++)
			{
				var id = all[i];
				if (result.Find(id) != null) continue;

				var insertAt = 0;
				for (var j = i - 1; j >= 0; j--)
				{
					var prev = all[j];
					var index = result.FindIndex(x => x.Id == prev);
					if (index >= 0)
					{
						insertAt = index + 1;
						break;
					}
				}
				result.Insert(insertAt, new TopToolbarEntry { Id = id });
			}
			return result;
		}
	}

	/// <summary>
	/// Describes a button that can appear in the top toolbar (used by the settings editor).
	/// </summary>
	internal class TopToolbarItemInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Section { get; set; }
		/// <summary>The group inside the menu this button belongs to (buttons of one group sit together by default)</summary>
		public string Group { get; set; }
		public Image DefaultIcon { get; set; }
		/// <summary>Renders the built-in icon at a given pixel size, or null when the icon cannot do that</summary>
		public Func<int, Image> IconAtSize { get; set; }
	}

	/// <summary>
	/// Stores the top toolbar's position, lock state, button visibility, order and icons.
	/// </summary>
	[Export(typeof(ISettingsContainer))]
	internal class TopToolbarSettings : ISettingsContainer
	{
		// Static so the menu register can read it directly
		public static TopToolbarLayout Layout { get; set; } = new TopToolbarLayout();
		public static ToolbarDock Dock { get; set; } = ToolbarDock.Top;
		public static bool Locked { get; set; } = DefaultLocked;

		/// <summary>False until divider lines have been put into the saved layout (once, at the default places)</summary>
		public static bool SeparatorsMigrated { get; set; }
		public static int IconSize { get; set; } = 24;

		/// <summary>Where each toolbar strip (keyed by its menu section) was left by the user: x, y inside the toolbar panel</summary>
		public static Dictionary<string, int[]> StripPositions { get; set; } = DefaultStripPositions();

		/// <summary>The toolbar starts locked for a user who has never customised it</summary>
		private const bool DefaultLocked = true;

		/// <summary>
		/// Where the strips sit on a first run. The button order itself is the default order
		/// (an empty layout resolves to it); these are the strip positions that go with it.
		/// </summary>
		private static Dictionary<string, int[]> DefaultStripPositions()
		{
			return new Dictionary<string, int[]>
			{
				{ "File", new[] { 3, 0 } },
				{ "Edit", new[] { 245, 0 } },
				{ "View", new[] { 555, 0 } },
				{ "Map", new[] { 921, 0 } },
				{ "Tools", new[] { 1461, 0 } }
			};
		}

		/// <summary>True once the saved values have been read (they may arrive after the menus are first built)</summary>
		public static bool Loaded { get; private set; }

		/// <summary>Version of the saved layout format. Layouts older than this get their order reset to the default once.</summary>
		private const int LayoutVersion = 2;

		/// <summary>True when the loaded layout is from an older build and must be put in the default order</summary>
		public static bool ResetOrder { get; set; }

		public string Name => "Sledge.Shell.TopToolbar";
		public bool ValuesLoaded { get; private set; }

		public IEnumerable<SettingKey> GetKeys()
		{
			// The button list goes first: the settings page gives the first tall editor all the spare room
			yield return new SettingKey("Top Toolbar", "TopToolbarButtons", typeof(TopToolbarLayout));
			yield return new SettingKey("Top Toolbar", "TopToolbarIconSize24", typeof(int)) { EditorType = "Slider", EditorHint = "16,64,2,8,1" };
			yield return new SettingKey("Top Toolbar", "TopToolbarPosition", typeof(ToolbarDock));
			yield return new SettingKey("Top Toolbar", "TopToolbarLocked", typeof(bool));
		}

		public void LoadValues(ISettingsStore store)
		{
			// Nothing saved yet (first run): use the built-in defaults - default button order,
			// default strip positions, locked - and don't treat the missing version as an old layout.
			var firstRun = !store.Contains("TopToolbarButtons");

			Dock = store.Get("TopToolbarPosition", ToolbarDock.Top);
			Locked = store.Get("TopToolbarLocked", DefaultLocked);
			IconSize = Math.Max(16, Math.Min(64, store.Get("TopToolbarIconSize24", 24)));
			Layout = store.Get("TopToolbarButtons", new TopToolbarLayout()) ?? new TopToolbarLayout();
			SeparatorsMigrated = store.Get("TopToolbarSeparators", false);

			if (firstRun)
			{
				StripPositions = DefaultStripPositions();
				ResetOrder = false;
			}
			else
			{
				StripPositions = store.Get("TopToolbarStripPositions", new Dictionary<string, int[]>()) ?? new Dictionary<string, int[]>();
				ResetOrder = store.Get("TopToolbarLayoutVersion", 0) < LayoutVersion;
			}

			ValuesLoaded = true;
			Loaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("TopToolbarPosition", Dock);
			store.Set("TopToolbarLocked", Locked);
			store.Set("TopToolbarIconSize24", IconSize);
			store.Set("TopToolbarButtons", Layout);
			store.Set("TopToolbarSeparators", SeparatorsMigrated);
			store.Set("TopToolbarStripPositions", StripPositions);
			store.Set("TopToolbarLayoutVersion", ResetOrder ? 0 : LayoutVersion);
		}
	}
}
