using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
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

        /// <summary>
        /// Loads a user-supplied icon from disk (.svg, .png, .ico, .bmp, .jpg, .gif).
        /// Returns null if the file is missing or can't be read.
        /// </summary>
        public static Image LoadFromFile(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            var key = "file|" + path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + size;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            try
            {
                Image result;
                var ext = Path.GetExtension(path) ?? "";
                if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var doc = SvgDocument.Open<SvgDocument>(path);
                    result = doc.Draw(size, size);
                }
                else if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    using (var icon = new Icon(path, size, size)) result = icon.ToBitmap();
                }
                else
                {
                    // Copy into a new bitmap so the file isn't left locked
                    using (var img = Image.FromFile(path)) result = new Bitmap(img, size, size);
                }
                if (result != null) Cache[key] = result;
                return result;
            }
            catch (Exception)
            {
                return null;
            }
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
