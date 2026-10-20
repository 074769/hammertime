using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Logging;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Shell.Theming;
using Sledge.Shell.Theming;

namespace Sledge.Shell.Registers
{
	/// <summary>
	/// The dialog register controls dialogs
	/// </summary>
	[Export(typeof(IStartupHook))]
	[Export(typeof(ISettingsContainer))]
	public class DialogRegister : IStartupHook, ISettingsContainer
	{
		[Import] private Forms.Shell _shell;
		[ImportMany] private IEnumerable<Lazy<IDialog>> _dialogs;
		private static DialogRegister _instance;
		private bool _useDarkMode = false;
		public async Task OnStartup()
		{
			// Register the exported dialogs
			foreach (var export in _dialogs)
			{
				Log.Debug(nameof(DialogRegister), "Loaded: " + export.Value.GetType().FullName);
				_components.Add(export.Value);
			}

			// Theming: every window registered here is themed when the dark palette is on, re-themed when it
			// changes, and anything added to it later is themed as it arrives. Windows nobody registers are
			// picked up by Themer's own message filter.
			_shell.InvokeSync(() =>
			{
				Themer.Install();
				Sledge.Shell.Settings.DialogPlacementSettings.Install();
				Themer.Track(_shell);
				foreach (var component in _components)
				{
					if (component is Control control) Themer.Track(control);
					if (component is Form form) Sledge.Shell.Settings.DialogPlacementSettings.Attach(form);
				}
			});

			// Subscribe to context changes
			Oy.Subscribe<IContext>("Context:Changed", ContextChanged);
		}
		private void UseDarkMode()
		{
			// Switching the palette re-themes every registered window (see Themer)
			Theme.Set(_useDarkMode);
			Oy.Publish<bool>("Theme:Changed", _useDarkMode);
		}

		/// <summary>Controls with this Tag (colour swatches) keep their own BackColor when the theme is applied.</summary>
		public const string NoThemeTag = Themer.NoThemeTag;

		/// <summary>Kept for existing callers: themes a control tree with the current palette.</summary>
		public static void ColorControlsRecursively(Control control, bool darkMode)
		{
			Themer.Apply(control);
		}

		private readonly List<IDialog> _components;

		public string Name => "DialogRegister";

		public bool ValuesLoaded { get; private set; } = false;

		public DialogRegister()
		{
			_instance = this;
			_components = new List<IDialog>();
		}
		public static bool IsAnyDialogOpened() => _instance._components.Where(x => x.Visible).Any();
		public static bool IsAnyDialogFocused() => _instance._components.Where(x => x.Visible).OfType<Form>().Where(f => f.ContainsFocus).Any();

		private Task ContextChanged(IContext context)
		{
			_shell.InvokeLater(() =>
			{
				foreach (var c in _components)
				{
					var vis = c.IsInContext(context);
					if (vis != c.Visible) c.SetVisible(context, vis);
				}
			});
			return Task.CompletedTask;
		}

		public IEnumerable<SettingKey> GetKeys()
		{
			yield return new SettingKey("Interface", "UseDarkMode", typeof(Boolean));
		}

		public void LoadValues(ISettingsStore store)
		{

			_useDarkMode = store.Get<Boolean>("UseDarkMode");
			UseDarkMode();
			ValuesLoaded = true;

		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("UseDarkMode", _useDarkMode);
		}
	}
}
