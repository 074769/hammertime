using System.Collections.Generic;
using System.ComponentModel.Composition;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Components
{
    /// <summary>
    /// Remembers the 3D viewport display toggles that should survive an editor restart.
    /// Currently: "3D Textured (No Shade)". Stored globally (not per map) in the normal
    /// settings file, and applied to each map when it is opened.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class ViewportRenderSettings : ISettingsContainer
    {
        private static ViewportRenderSettings _instance;

        public bool Unshaded3D { get; set; } = false;

        public string Name => "Sledge.BspEditor.Components.ViewportRenderSettings";

        public bool ValuesLoaded { get; set; } = false;

        public ViewportRenderSettings()
        {
            _instance = this;
        }

        public static ViewportRenderSettings GetInstance()
        {
            return _instance;
        }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            Unshaded3D = store.Get("Unshaded3D", false);
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("Unshaded3D", Unshaded3D);
        }
    }
}
