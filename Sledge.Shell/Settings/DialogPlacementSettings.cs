using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings
{
    /// <summary>
    /// Remembers where the user put each dialog window (Object Properties, Texture Application,
    /// UV Mapping, Settings, Map Information, ...) and restores it the next time the editor starts.
    /// DialogRegister calls <see cref="Attach"/> for every registered dialog form.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class DialogPlacementSettings : ISettingsContainer
    {
        private const string Prefix = "Dialog:";

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Rectangle> Placements = new Dictionary<string, Rectangle>();

        public string Name => "Sledge.Shell.DialogPlacement";
        public bool ValuesLoaded { get; private set; }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            lock (Lock)
            {
                foreach (var key in store.GetKeys().Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)))
                {
                    var name = key.Substring(Prefix.Length);
                    // Positions captured live this session win over anything re-read from disk
                    if (Placements.ContainsKey(name)) continue;
                    if (TryParse(store.Get<string>(key), out var rect)) Placements[name] = rect;
                }
            }
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            lock (Lock)
            {
                foreach (var kv in Placements)
                {
                    var r = kv.Value;
                    store.Set(Prefix + kv.Key, string.Join(",",
                        r.X.ToString(CultureInfo.InvariantCulture),
                        r.Y.ToString(CultureInfo.InvariantCulture),
                        r.Width.ToString(CultureInfo.InvariantCulture),
                        r.Height.ToString(CultureInfo.InvariantCulture)));
                }
            }
        }

        /// <summary>
        /// Restore the saved position of a dialog and start tracking it. Call on the UI thread.
        /// </summary>
        public static void Attach(Form form)
        {
            if (form == null) return;
            var name = form.GetType().FullName;

            Rectangle saved;
            bool hasSaved;
            lock (Lock) hasSaved = Placements.TryGetValue(name, out saved);

            if (hasSaved && IsUsable(saved))
            {
                var sizable = form.FormBorderStyle == FormBorderStyle.Sizable
                              || form.FormBorderStyle == FormBorderStyle.SizableToolWindow;
                var size = sizable ? ClampSize(form, saved.Size) : form.Size;
                form.StartPosition = FormStartPosition.Manual;
                form.Bounds = new Rectangle(saved.Location, size);
            }

            Action capture = () => Capture(form, name);
            form.LocationChanged += (s, e) => capture();
            form.SizeChanged += (s, e) => capture();
            form.VisibleChanged += (s, e) => capture();
        }

        private static void Capture(Form form, string name)
        {
            if (form.IsDisposed || !form.IsHandleCreated || !form.Visible) return;
            var rect = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            if (rect.Width <= 0 || rect.Height <= 0 || !IsUsable(rect)) return;
            lock (Lock) Placements[name] = rect;
        }

        private static Size ClampSize(Form form, Size size)
        {
            var min = form.MinimumSize;
            var area = Screen.FromPoint(new Point(form.Left, form.Top)).WorkingArea;
            var w = Math.Min(Math.Max(size.Width, Math.Max(min.Width, 100)), area.Width);
            var h = Math.Min(Math.Max(size.Height, Math.Max(min.Height, 100)), area.Height);
            return new Size(w, h);
        }

        /// <summary>The title bar must be reachable on a connected screen (monitors can be unplugged).</summary>
        private static bool IsUsable(Rectangle r)
        {
            var title = new Rectangle(r.Left, r.Top, Math.Max(r.Width, 1), 32);
            return Screen.AllScreens.Any(s =>
            {
                var i = Rectangle.Intersect(title, s.WorkingArea);
                return i.Width >= 80 && i.Height >= 16;
            });
        }

        private static bool TryParse(string value, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (string.IsNullOrEmpty(value)) return false;
            var parts = value.Split(',');
            if (parts.Length != 4) return false;
            var n = new int[4];
            for (var i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return false;
            }
            if (n[2] <= 0 || n[3] <= 0) return false;
            rect = new Rectangle(n[0], n[1], n[2], n[3]);
            return true;
        }
    }
}
