using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings
{
    /// <summary>
    /// Remembers where the user put each pop-up window (Object Properties, Texture Application,
    /// UV Mapping, Settings, Compile, Transform, ...) and restores it the next time it opens.
    /// DialogRegister calls <see cref="Attach"/> for every registered dialog and <see cref="Install"/>
    /// once; any other pop-up is picked up on its first paint.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    public class DialogPlacementSettings : ISettingsContainer
    {
        private const string Prefix = "Dialog:";

        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Rectangle> Placements = new Dictionary<string, Rectangle>();
        private static readonly ConditionalWeakTable<Form, object> Attached = new ConditionalWeakTable<Form, object>();
        private static readonly List<WeakReference<Form>> TrackedForms = new List<WeakReference<Form>>();
        private static bool _installed;
        private static volatile bool _loaded;
        private static Timer _saveTimer;

        // Windows that place themselves, remember themselves, or aren't movable pop-ups
        private static readonly HashSet<string> Excluded = new HashSet<string>
        {
            "ModelBrowserDialog", "ViewportWindow", "SplashForm", "CommandBox", "ExceptionWindow"
        };

        public string Name => "Sledge.Shell.DialogPlacement";
        public bool ValuesLoaded => _loaded;

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
            _loaded = true;

            // Dialogs register at startup, before the settings file is read: apply the saved
            // positions to every dialog that is already attached and not on screen yet.
            List<Form> forms;
            lock (Lock)
            {
                forms = TrackedForms.Select(w => w.TryGetTarget(out var f) ? f : null).Where(f => f != null).ToList();
            }
            foreach (var form in forms)
            {
                var f = form;
                OnUiThread(f, () =>
                {
                    if (!f.IsDisposed && !f.Visible) Restore(f);
                });
            }
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
        /// Call once on the UI thread at startup. Pop-up windows that aren't registered dialogs
        /// (browsers, compile/transform/export windows, prompts...) are picked up on their first paint.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            Application.AddMessageFilter(new NewWindowFilter());
        }

        private sealed class NewWindowFilter : IMessageFilter
        {
            private const int WmPaint = 0x000F;

            public bool PreFilterMessage(ref System.Windows.Forms.Message m)
            {
                if (m.Msg != WmPaint) return false;
                var form = (Control.FromHandle(m.HWnd)?.TopLevelControl) as Form;
                if (form == null || !ShouldTrack(form)) return false;
                Attach(form);
                return false;
            }
        }

        private static bool ShouldTrack(Form form)
        {
            if (form.IsDisposed || !form.TopLevel || form.IsMdiChild) return false;
            if (form is Forms.Shell) return false;
            if (form.FormBorderStyle == FormBorderStyle.None) return false;
            if (form.WindowState != FormWindowState.Normal) return false;
            return !Excluded.Contains(form.GetType().Name);
        }

        /// <summary>
        /// Restore the saved position of a window and start tracking it. Call on the UI thread.
        /// </summary>
        public static void Attach(Form form)
        {
            if (form == null) return;
            lock (Lock)
            {
                if (Attached.TryGetValue(form, out _)) return;
                Attached.Add(form, new object());
                if (!Excluded.Contains(form.GetType().Name)) TrackedForms.Add(new WeakReference<Form>(form));
            }
            if (Excluded.Contains(form.GetType().Name)) return;

            Restore(form);

            var name = form.GetType().FullName;
            form.LocationChanged += (s, e) => Capture(form, name);
            form.SizeChanged += (s, e) => Capture(form, name);
            form.ResizeEnd += (s, e) => ScheduleSave();
            form.VisibleChanged += (s, e) =>
            {
                Capture(form, name);
                if (!form.Visible) ScheduleSave();
            };
        }

        private static void Restore(Form form)
        {
            var name = form.GetType().FullName;
            Rectangle saved;
            lock (Lock)
            {
                if (!Placements.TryGetValue(name, out saved)) return;
            }
            if (!IsUsable(saved)) return;

            var sizable = form.FormBorderStyle == FormBorderStyle.Sizable
                          || form.FormBorderStyle == FormBorderStyle.SizableToolWindow;
            var size = sizable ? ClampSize(form, saved) : form.Size;
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(saved.Location, size);
        }

        private static void Capture(Form form, string name)
        {
            if (form.IsDisposed || !form.IsHandleCreated || !form.Visible) return;
            var rect = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            if (rect.Width <= 0 || rect.Height <= 0 || !IsUsable(rect)) return;
            lock (Lock) Placements[name] = rect;
        }

        /// <summary>Write the settings file shortly after a window was moved, instead of only at shutdown.</summary>
        private static void ScheduleSave()
        {
            if (!_loaded) return;
            if (_saveTimer == null)
            {
                _saveTimer = new Timer { Interval = 1500 };
                _saveTimer.Tick += (s, e) =>
                {
                    _saveTimer.Stop();
                    Oy.Publish("Settings:Save");
                };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private static void OnUiThread(Form form, Action action)
        {
            if (form.IsHandleCreated && form.InvokeRequired) form.BeginInvoke(action);
            else action();
        }

        private static Size ClampSize(Form form, Rectangle saved)
        {
            var min = form.MinimumSize;
            var area = Screen.FromPoint(saved.Location).WorkingArea;
            var w = Math.Min(Math.Max(saved.Width, Math.Max(min.Width, 100)), area.Width);
            var h = Math.Min(Math.Max(saved.Height, Math.Max(min.Height, 100)), area.Height);
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
