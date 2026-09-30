using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Environment.Controls;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Environment
{
    [Export]
    [Export(typeof(ISettingsContainer))]
    [Export(typeof(ISettingEditorFactory))]
    public class EnvironmentRegister : ISettingsContainer, ISettingEditorFactory
    {
        private readonly IEnumerable<Lazy<IEnvironmentFactory>> _factories;
        private EnvironmentCollection _environments = new EnvironmentCollection();
        public bool ValuesLoaded { get; private set; } = false;

        [ImportingConstructor]
        public EnvironmentRegister([ImportMany] IEnumerable<Lazy<IEnvironmentFactory>> factories)
        {
            _factories = factories;
        }

        public string OrderHint => "B";

        public IEnumerable<SerialisedEnvironment> GetSerialisedEnvironments()
        {
            return _environments;
        }

        // Remembers the saved configuration each live environment instance was built from,
        // so we can tell later whether the settings for it actually changed.
        private readonly ConditionalWeakTable<IEnvironment, string> _signatures = new ConditionalWeakTable<IEnvironment, string>();

        private static string GetSignature(SerialisedEnvironment env)
        {
            var props = string.Join("\n", env.Properties.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value));
            return env.ID + "|" + env.Name + "|" + env.Type + "|" + props;
        }

        public IEnvironment GetEnvironment(string id)
        {
            var env = _environments.FirstOrDefault(x => x.ID == id);
            if (env == null) return null;

            var fac = _factories.FirstOrDefault(x => x.Value.TypeName == env.Type);
            if (fac == null) return null;

            var result = fac.Value.Deserialise(env);
            if (result != null)
            {
                _signatures.Remove(result);
                _signatures.Add(result, GetSignature(env));
            }
            return result;
        }

        /// <summary>
        /// True if the given environment instance was built from the environment settings as
        /// they are right now, i.e. nothing about it needs to be rebuilt.
        /// </summary>
        public bool IsUpToDate(IEnvironment environment)
        {
            if (environment == null) return false;
            var current = _environments.FirstOrDefault(x => x.ID == environment.ID);
            if (current == null) return false;
            return _signatures.TryGetValue(environment, out var sig) && sig == GetSignature(current);
        }

        public bool Supports(SettingKey key)
        {
            return key.Type == typeof(EnvironmentCollection);
        }

        public ISettingEditor CreateEditorFor(SettingKey key)
        {
            if (key.Type == typeof(EnvironmentCollection))
            {
                return new EnvironmentCollectionEditor(_factories.Select(x => x.Value));
            }
            return null;
        }

        public string Name => "Sledge.BspEditor.Environment.EnvironmentRegister";

        public IEnumerable<SettingKey> GetKeys()
        {
            yield return new SettingKey("Environments", "Environments", typeof(EnvironmentCollection));
        }

        public void LoadValues(ISettingsStore store)
        {
            if (store.Contains("Environments"))
            {
                _environments = (EnvironmentCollection) store.Get(typeof(EnvironmentCollection), "Environments") ?? new EnvironmentCollection();
                ValuesLoaded = true;
            }
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("Environments", _environments);
        }
    }
}
