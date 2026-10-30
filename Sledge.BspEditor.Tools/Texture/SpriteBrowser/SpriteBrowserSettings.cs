using System.Collections.Generic;
using System.ComponentModel.Composition;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>
	/// Remembers how the user arranged the sprite browser (window, splitters, grid / list, columns, sorting, last folder,
	/// preview options) and which of the two browsers - sprite files or the texture browser - was used last.
	/// Persisted by the shell's settings provider like every other settings container.
	/// </summary>
	[Export(typeof(ISettingsContainer))]
	public class SpriteBrowserSettings : ISettingsContainer
	{
		// Which browser opens first: false = sprite browser, true = texture browser
		[Setting] public static bool UseTextureBrowser { get; set; } = false;

		// Window (normal bounds, not the maximized ones)
		[Setting] public static int WindowLeft { get; set; } = 0;
		[Setting] public static int WindowTop { get; set; } = 0;
		[Setting] public static int WindowWidth { get; set; } = 0; // 0 = never saved
		[Setting] public static int WindowHeight { get; set; } = 0;
		[Setting] public static bool WindowMaximized { get; set; } = false;

		// Layout (pixels, 0 = use the default)
		[Setting] public static int TreeWidth { get; set; } = 0;
		[Setting] public static int PreviewWidth { get; set; } = 0;
		[Setting] public static int InfoHeight { get; set; } = 0;

		// List
		[Setting] public static bool GridView { get; set; } = true;
		[Setting] public static int ThumbSize { get; set; } = 96;
		[Setting] public static int ColumnNameWidth { get; set; } = 0;
		[Setting] public static int ColumnFolderWidth { get; set; } = 0;
		[Setting] public static int ColumnSizeWidth { get; set; } = 0;
		[Setting] public static int ColumnDimensionsWidth { get; set; } = 0;
		[Setting] public static int ColumnFramesWidth { get; set; } = 0;
		[Setting] public static int SortColumn { get; set; } = -1;
		[Setting] public static bool SortAscending { get; set; } = true;

		// Preview
		[Setting] public static int PreviewBackground { get; set; } = 0; // index into the background list
		[Setting] public static int PreviewZoom { get; set; } = 0;       // 0 = fit, otherwise whole-number zoom
		[Setting] public static int PreviewFps { get; set; } = 10;
		[Setting] public static bool PreviewAutoPlay { get; set; } = true;
		[Setting] public static bool PreviewOutline { get; set; } = false;

		/// <summary>The last browsed folder (relative to the sprites folder), used when the property has no value yet.</summary>
		[Setting] public static string LastFolder { get; set; } = "";

		public string Name => "Sledge.BspEditor.Tools.SpriteBrowserSettings";
		public bool ValuesLoaded { get; private set; }

		public IEnumerable<SettingKey> GetKeys()
		{
			// Nothing to edit in the settings form
			yield break;
		}

		public void LoadValues(ISettingsStore store)
		{
			store.LoadInstance(this);
			ValuesLoaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.StoreInstance(this);
		}
	}
}
