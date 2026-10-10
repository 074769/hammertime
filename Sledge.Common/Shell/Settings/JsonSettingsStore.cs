using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sledge.Common.Shell.Settings
{
    /// <summary>
    /// A setting store that saves to a JSON format
    /// </summary>
    public class JsonSettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, JProperty> _keys;

        public JsonSettingsStore()
        {
            _keys = new Dictionary<string, JProperty>();
        }

        public JsonSettingsStore(string json)
        {
            var root = JObject.Parse(json);
            _keys = root.Properties().ToDictionary(x => x.Name, x => x);
        }

        public IEnumerable<string> GetKeys()
        {
            return _keys.Keys;
        }

        public bool Contains(string key)
        {
            return _keys.ContainsKey(key);
        }

        public object Get(Type type, string key, object defaultValue = null)
        {
            if (!_keys.ContainsKey(key)) return defaultValue;
            var prop = _keys[key];
            try
            {
                return prop.Value.ToObject(type);
            }
            catch
            {
                return defaultValue;
            }
        }

        public T Get<T>(string key, T defaultValue = default(T))
        {
            return (T)Get(typeof(T), key, defaultValue);
        }

        public void Set<T>(string key, T value)
        {
            var obj = value == null ? null : JToken.FromObject(value);
            _keys[key] = new JProperty(key, obj);
        }

        public void Delete(string key)
        {
            if (_keys.ContainsKey(key)) _keys.Remove(key);
        }

        /// <summary>
        /// Compare this store with another one and return the keys whose values differ,
        /// including keys that only exist in one of the two stores.
        /// Used to work out what actually changed before applying settings.
        /// </summary>
        /// <param name="other">The store to compare against</param>
        /// <returns>The keys that differ between the two stores</returns>
        public IEnumerable<string> GetDifferentKeys(JsonSettingsStore other)
        {
            var keys = new HashSet<string>(_keys.Keys);
            keys.UnionWith(other._keys.Keys);
            foreach (var key in keys)
            {
                if (GetRawValue(key) != other.GetRawValue(key)) yield return key;
            }
        }

        private string GetRawValue(string key)
        {
            if (!_keys.TryGetValue(key, out var prop)) return null;
            var value = prop.Value;
            if (value == null || value.Type == JTokenType.Null) return null;
            return value.ToString(Formatting.None);
        }

        public string ToJson()
        {
            return new JObject(_keys.Values.OfType<object>().ToArray()).ToString();
        }
    }
}