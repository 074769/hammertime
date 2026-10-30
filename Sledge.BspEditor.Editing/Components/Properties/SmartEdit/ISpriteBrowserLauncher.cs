using Sledge.FileSystem;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
	/// <summary>What happened in the sprite browser.</summary>
	public class SpriteBrowserResult
	{
		/// <summary>The user pressed the "Model browser" button: nothing was chosen, show the model browser again.</summary>
		public bool SwitchToModels { get; set; }

		/// <summary>The game path of the chosen sprite (e.g. "sprites/glow01.spr"), or null if the browser was cancelled.</summary>
		public string Path { get; set; }
	}

	/// <summary>
	/// Opens the sprite browser. The browser lives in a project that depends on this one, so the model browser's
	/// property editor reaches it through this interface (exported by the project that has the browser).
	/// </summary>
	public interface ISpriteBrowserLauncher
	{
		SpriteBrowserResult Show(IFile root, string currentValue);
	}
}
