using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using Svg;

namespace Sledge.Common.Shell
{
    /// <summary>
    /// Loads an icon from an embedded SVG if one exists, otherwise falls back to the
    /// PNG resource with the same name on the given resources class.
    /// </summary>
    public static class IconLoader
    {
        private const int DefaultSize = 16;
        private static readonly ConcurrentDictionary<string, Image> Cache = new ConcurrentDictionary<string, Image>();

        /// <param name="resourceType">The generated Resources class (e.g. typeof(Resources)). Its assembly is searched for the SVG.</param>
        /// <param name="resourceName">The PNG resource property name, e.g. "Menu_UVLock". The SVG must be named the same.</param>
        /// <param name="size">Optional pixel size for the SVG. Defaults to the size of the PNG fallback, or 16.</param>
        public static Image Load(Type resourceType, string resourceName, int? size = null)
        {
            if (resourceType == null || string.IsNullOrEmpty(resourceName)) return null;

            var key = resourceType.Assembly.FullName + "|" + resourceName + "|" + size;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var png = GetPng(resourceType, resourceName);
            var svg = TryLoadSvg(resourceType.Assembly, resourceName, size ?? png?.Width ?? DefaultSize, size ?? png?.Height ?? DefaultSize);

            var result = svg ?? png;
            if (result != null) Cache[key] = result;
            return result;
        }

        private static Image GetPng(Type resourceType, string resourceName)
        {
            var prop = resourceType.GetProperty(resourceName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (prop == null || !typeof(Image).IsAssignableFrom(prop.PropertyType)) return null;
            return prop.GetValue(null) as Image;
        }

        private static Image TryLoadSvg(Assembly assembly, string resourceName, int width, int height)
        {
            try
            {
                using (var stream = assembly.GetManifestResourceStream("Svg." + resourceName + ".svg"))
                {
                    if (stream == null) return null;
                    var doc = SvgDocument.Open<SvgDocument>(stream);
                    return doc.Draw(width, height);
                }
            }
            catch (Exception)
            {
                // Bad or unsupported SVG: fall back to the PNG
                return null;
            }
        }
    }
}
