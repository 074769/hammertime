using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties;
using Sledge.BspEditor.Editing.Components.Properties.SmartEdit;
using Sledge.Common.Translations;
using Sledge.DataStructures.GameData;
using Sledge.FileSystem;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>
	/// The editor for sprite (.spr) keyvalues. Browse opens the sprite browser, which can hand over to the texture
	/// browser (and back) with a button; the last one used is the one that opens next time. Both give the same
	/// value ("sprites/name.spr").
	/// </summary>
	[Export(typeof(IObjectPropertyEditor))]
	public class SmartEditSpriteBrowser : SmartEditControl
	{
		private WeakReference<MapDocument> _document;
		[Import] private Lazy<ITranslationStringProvider> _translation;

		private readonly TextBox _textBox;
		private readonly Button _browseButton;

		public SmartEditSpriteBrowser()
		{
			CreateHandle();

			_textBox = new TextBox { Width = 180 };
			_textBox.TextChanged += (sender, e) => OnValueChanged();
			Controls.Add(_textBox);

			_browseButton = new Button { Text = "Browse...", Margin = new Padding(1), UseVisualStyleBackColor = true };
			_browseButton.Click += OpenBrowser;
			Controls.Add(_browseButton);

			_document = new WeakReference<MapDocument>(null);
		}

		public override string PriorityHint => "H";

		public override bool SupportsType(VariableType type)
		{
			return type == VariableType.Sprite;
		}

		private async void OpenBrowser(object sender, EventArgs e)
		{
			try
			{
				await OpenBrowser();
			}
			catch (Exception ex)
			{
				Sledge.Common.Logging.Log.Error(nameof(SmartEditSpriteBrowser), "Unable to open the sprite browser", ex);
			}
		}

		private async Task OpenBrowser()
		{
			if (!_document.TryGetTarget(out var doc)) return;

			var useTextures = SpriteBrowserSettings.UseTextureBrowser;
			while (true)
			{
				if (!useTextures)
				{
					using (var sb = new SpriteBrowserDialog(doc.Environment.Root, _textBox.Text))
					{
						sb.ShowDialog();
						if (sb.SwitchRequested)
						{
							useTextures = true;
							SpriteBrowserSettings.UseTextureBrowser = true;
							continue;
						}

						SpriteBrowserSettings.UseTextureBrowser = false;
						if (sb.SelectedPath != null) _textBox.Text = sb.SelectedPath;
					}
				}
				else
				{
					using (var tb = new TextureBrowser(doc))
					{
						await tb.Initialise(_translation.Value);
						var tc = await doc.Environment.GetTextureCollection();
						await tb.SetTextureList(tc.GetSpriteTextures());
						tb.SetSelectedTextures(new[] { _textBox.Text });
						tb.EnableSpriteBrowserSwitch();
						tb.ShowDialog();
						if (tb.SwitchToSpriteBrowser)
						{
							useTextures = false;
							SpriteBrowserSettings.UseTextureBrowser = false;
							continue;
						}

						SpriteBrowserSettings.UseTextureBrowser = true;
						if (tb.SelectedTexture != null) _textBox.Text = tb.SelectedTexture;
					}
				}

				// Remember which browser was used last
				LogicAndTrick.Oy.Oy.Publish("Settings:Save");
				return;
			}
		}

		protected override string GetName()
		{
			return OriginalName;
		}

		protected override string GetValue()
		{
			return _textBox.Text;
		}

		protected override void OnSetProperty(MapDocument document)
		{
			_document = new WeakReference<MapDocument>(document);
			_textBox.Text = PropertyValue;
		}
	}
}
