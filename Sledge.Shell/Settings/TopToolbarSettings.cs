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
				if (e?.Id == null || !all.Contains(e.Id) || result.Find(e.Id) != null) continue;
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
		public Image DefaultIcon { get; set; }
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
		public static bool Locked { get; set; } = false;
		public static int IconSize { get; set; } = 24;

		/// <summary>Where each toolbar strip (keyed by its menu section) was left by the user: x, y inside the toolbar panel</summary>
		public static Dictionary<string, int[]> StripPositions { get; set; } = new Dictionary<string, int[]>();

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
			Dock = store.Get("TopToolbarPosition", ToolbarDock.Top);
			Locked = store.Get("TopToolbarLocked", false);
			IconSize = Math.Max(16, Math.Min(64, store.Get("TopToolbarIconSize24", 24)));
			Layout = store.Get("TopToolbarButtons", new TopToolbarLayout()) ?? new TopToolbarLayout();
			StripPositions = store.Get("TopToolbarStripPositions", new Dictionary<string, int[]>()) ?? new Dictionary<string, int[]>();
			ResetOrder = store.Get("TopToolbarLayoutVersion", 0) < LayoutVersion;
			ValuesLoaded = true;
			Loaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("TopToolbarPosition", Dock);
			store.Set("TopToolbarLocked", Locked);
			store.Set("TopToolbarIconSize24", IconSize);
			store.Set("TopToolbarButtons", Layout);
			store.Set("TopToolbarStripPositions", StripPositions);
			store.Set("TopToolbarLayoutVersion", ResetOrder ? 0 : LayoutVersion);
		}
	}
}
