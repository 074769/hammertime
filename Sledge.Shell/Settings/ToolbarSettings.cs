using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings
{
	/// <summary>
	/// One tool's entry in the toolbar customisation.
	/// </summary>
	public class ToolbarEntry
	{
		/// <summary>The tool's name (ITool.Name)</summary>
		public string Name { get; set; }

		/// <summary>Whether the tool is shown in the toolbar. Hidden tools still work through their hotkeys.</summary>
		public bool Visible { get; set; } = true;

		/// <summary>Full path of a user-chosen icon file, or empty to use the tool's built-in icon.</summary>
		public string IconPath { get; set; }

		public ToolbarEntry Clone()
		{
			return new ToolbarEntry { Name = Name, Visible = Visible, IconPath = IconPath };
		}
	}

	/// <summary>
	/// The user's toolbar layout: the order of the entries is the order of the buttons.
	/// </summary>
	public class ToolbarLayout : List<ToolbarEntry>
	{
		public ToolbarLayout Clone()
		{
			var l = new ToolbarLayout();
			foreach (var e in this) l.Add(e.Clone());
			return l;
		}

		public ToolbarEntry Find(string name)
		{
			return this.FirstOrDefault(x => x.Name == name);
		}

		/// <summary>
		/// Builds the display order for the given tool names: saved entries first (in saved order),
		/// then any tools that have no saved entry yet (in the default order, visible).
		/// </summary>
		public ToolbarLayout Resolve(IEnumerable<string> toolNames)
		{
			var names = toolNames.ToList();
			var result = new ToolbarLayout();
			foreach (var e in this)
			{
				if (e?.Name == null || !names.Contains(e.Name) || result.Find(e.Name) != null) continue;
				result.Add(e.Clone());
			}
			foreach (var n in names)
			{
				if (result.Find(n) == null) result.Add(new ToolbarEntry { Name = n });
			}
			return result;
		}
	}

	/// <summary>
	/// Stores which tools are shown in the toolbar, their order and their custom icons.
	/// </summary>
	[Export(typeof(ISettingsContainer))]
	internal class ToolbarSettings : ISettingsContainer
	{
		// Static so the toolbar (ToolRegister) can read it directly
		public static ToolbarLayout Layout { get; private set; } = new ToolbarLayout();
		public static int IconSize { get; private set; } = 32;

		/// <summary>
		/// The unique name of the settings container.
		/// </summary>
		public const string ContainerName = "Sledge.Shell.Toolbar";

		public string Name => ContainerName;
		public bool ValuesLoaded { get; private set; }

		public IEnumerable<SettingKey> GetKeys()
		{
			yield return new SettingKey("Toolbar", "Tools", typeof(ToolbarLayout));
			yield return new SettingKey("Toolbar", "ToolsIconSize", typeof(int)) { EditorType = "Slider", EditorHint = "16,64,2,8,1" };
		}

		public void LoadValues(ISettingsStore store)
		{
			Layout = store.Get("Tools", new ToolbarLayout()) ?? new ToolbarLayout();
			IconSize = Math.Max(16, Math.Min(64, store.Get("ToolsIconSize", 32)));
			ValuesLoaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("Tools", Layout);
			store.Set("ToolsIconSize", IconSize);
		}
	}
}
