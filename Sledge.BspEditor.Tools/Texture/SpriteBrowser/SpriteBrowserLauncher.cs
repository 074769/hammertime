using System.ComponentModel.Composition;
using Sledge.BspEditor.Editing.Components.Properties.SmartEdit;
using Sledge.FileSystem;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>Lets the model browser's property editor open the sprite browser (and get back here with the switch button).</summary>
	[Export(typeof(ISpriteBrowserLauncher))]
	public class SpriteBrowserLauncher : ISpriteBrowserLauncher
	{
		public SpriteBrowserResult Show(IFile root, string currentValue)
		{
			using (var sb = new SpriteBrowserDialog(root, currentValue))
			{
				sb.ShowDialog();
				return new SpriteBrowserResult { SwitchToModels = sb.SwitchRequested, Path = sb.SelectedPath };
			}
		}
	}
}
