using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Editing.CompileErrors
{
    /// <summary>
    /// Global show/hide choices for compile log message types. Stored as a comma separated list of
    /// filter keys; a key in the list means that type is hidden.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class CompileErrorsSettings : ISettingsContainer
    {
        /// <summary>Hull 1 to 3 ambiguous leafnode warnings are usually harmless, so they start hidden.</summary>
        public const string DefaultHidden = "AmbiguousLeafnode.Hull1Plus";

        [Setting] public static string HiddenTypes { get; set; } = DefaultHidden;

        private static readonly object Lock = new object();

        public static bool IsHidden(string key)
        {
            lock (Lock) return Split().Contains(key);
        }

        public static void SetHidden(string key, bool hidden)
        {
            lock (Lock)
            {
                var set = Split();
                if (hidden) set.Add(key);
                else set.Remove(key);
                HiddenTypes = string.Join(",", set.OrderBy(x => x));
            }
        }

        private static HashSet<string> Split()
        {
            return new HashSet<string>(
                (HiddenTypes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        }

        public string Name => "Sledge.BspEditor.Editing.CompileErrors";
        public bool ValuesLoaded { get; private set; }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield return new SettingKey("Compile errors", "HiddenTypes", typeof(string));
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
