using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings
{
	/// <summary>
	/// Remembers the column widths of the hotkey list in the settings window.
	/// The last column (Hotkey) always fills the remaining space, so it isn't stored.
	/// </summary>
	[Export(typeof(ISettingsContainer))]
	internal class HotkeyColumnSettings : ISettingsContainer
	{
		public const int DefaultActionWidth = 220;
		public const int DefaultDescriptionWidth = 340;
		public const int MinWidth = 40;
		public const int MaxWidth = 2000;

		// Static so the (non-MEF) editor control can read and write them directly
		public static int ActionWidth { get; set; } = DefaultActionWidth;
		public static int DescriptionWidth { get; set; } = DefaultDescriptionWidth;

		public string Name => "Sledge.Shell.HotkeyColumns";
		public bool ValuesLoaded { get; private set; }

		public IEnumerable<SettingKey> GetKeys()
		{
			// Not shown in the settings window
			yield break;
		}

		public void LoadValues(ISettingsStore store)
		{
			// The settings window re-applies the values it captured when it was opened when OK is clicked,
			// which would undo any column resizing done while it was open. Only load from disk once.
			if (ValuesLoaded) return;

			ActionWidth = Clamp(store.Get("ActionWidth", DefaultActionWidth));
			DescriptionWidth = Clamp(store.Get("DescriptionWidth", DefaultDescriptionWidth));
			ValuesLoaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("ActionWidth", ActionWidth);
			store.Set("DescriptionWidth", DescriptionWidth);
		}

		private static int Clamp(int width)
		{
			return Math.Max(MinWidth, Math.Min(MaxWidth, width));
		}
	}
}
