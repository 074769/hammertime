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
		/// Saved entries first (in saved order, dropping ones that no longer exist),
		/// then any buttons without a saved entry (default order, visible).
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
			foreach (var id in all)
			{
				if (result.Find(id) == null) result.Add(new TopToolbarEntry { Id = id });
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

		/// <summary>True once the saved values have been read (they may arrive after the menus are first built)</summary>
		public static bool Loaded { get; private set; }

		public string Name => "Sledge.Shell.TopToolbar";
		public bool ValuesLoaded { get; private set; }

		public IEnumerable<SettingKey> GetKeys()
		{
			yield return new SettingKey("Toolbar", "TopToolbarPosition", typeof(ToolbarDock));
			yield return new SettingKey("Toolbar", "TopToolbarLocked", typeof(bool));
			yield return new SettingKey("Toolbar", "TopToolbarButtons", typeof(TopToolbarLayout));
		}

		public void LoadValues(ISettingsStore store)
		{
			Dock = store.Get("TopToolbarPosition", ToolbarDock.Top);
			Locked = store.Get("TopToolbarLocked", false);
			Layout = store.Get("TopToolbarButtons", new TopToolbarLayout()) ?? new TopToolbarLayout();
			ValuesLoaded = true;
			Loaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("TopToolbarPosition", Dock);
			store.Set("TopToolbarLocked", Locked);
			store.Set("TopToolbarButtons", Layout);
		}
	}
}
