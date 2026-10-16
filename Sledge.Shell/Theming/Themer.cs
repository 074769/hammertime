using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Theming;

namespace Sledge.Shell.Theming
{
    /// <summary>
    /// Applies the <see cref="Theme"/> palette to WinForms controls.
    ///
    /// - <see cref="Track"/> a window once: it is themed now (if dark), re-themed whenever the palette changes,
    ///   and anything added to it later is themed as it arrives (no per-dialog code needed).
    /// - A message filter picks up windows nobody registered (new dialogs, pop-up forms) the first time they paint.
    /// - A control tagged <see cref="NoThemeTag"/> is skipped together with its children.
    /// - A control with its own deliberate BackColor (black preview surfaces, custom swatches) is a "colour island":
    ///   it and its children keep their colours, only the text colour is made readable.
    /// </summary>
    public static class Themer
    {
        /// <summary>Controls with this Tag (and their children) keep their own colours.</summary>
        public const string NoThemeTag = "NoTheme";

        private sealed class Applied
        {
            public int Back;
            public int Fore;
        }

        private static readonly ConditionalWeakTable<Control, object> Tracked = new ConditionalWeakTable<Control, object>();
        private static readonly ConditionalWeakTable<Control, Applied> AppliedColours = new ConditionalWeakTable<Control, Applied>();
        private static readonly ConditionalWeakTable<Control, object> NativeDark = new ConditionalWeakTable<Control, object>();
        private static readonly object Marker = new object();

        private static readonly List<WeakReference<Control>> Roots = new List<WeakReference<Control>>();
        private static bool _installed;

        private static readonly MethodInfo ShouldSerializeBackColor =
            typeof(Control).GetMethod("ShouldSerializeBackColor", BindingFlags.Instance | BindingFlags.NonPublic);

        #region Setup

        /// <summary>
        /// Call once, on the UI thread, at startup: makes every tool strip / menu use the themed renderer,
        /// watches for windows that were never registered and re-themes everything when the palette changes.
        /// </summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            ToolStripManager.Renderer = new ThemeToolStripRenderer();
            Application.AddMessageFilter(new NewWindowFilter());
            Theme.Changed += (s, e) => ReapplyAll();
        }

        /// <summary>
        /// Register a window or panel. It is themed immediately when the dark palette is active and
        /// themed again after every palette change; controls added to it later are themed as they arrive.
        /// </summary>
        public static void Track(Control root)
        {
            if (root == null || root.IsDisposed) return;

            if (!Tracked.TryGetValue(root, out _))
            {
                lock (Roots) Roots.Add(new WeakReference<Control>(root));
            }
            Hook(root);

            if (Theme.IsDark) ApplyCore(root);
        }

        /// <summary>Theme a control tree right now using the current palette (for controls built on demand).</summary>
        public static void Apply(Control control)
        {
            ApplyCore(control);
        }

        private static void Hook(Control c)
        {
            if (c == null || Tracked.TryGetValue(c, out _)) return;
            Tracked.Add(c, Marker);
            c.ControlAdded += OnControlAdded;
            foreach (Control child in c.Controls) Hook(child);
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            Hook(e.Control);
            if (Theme.IsDark) ApplyCore(e.Control);
        }

        private static void ReapplyAll()
        {
            var alive = new List<Control>();
            lock (Roots)
            {
                for (var i = Roots.Count - 1; i >= 0; i--)
                {
                    Control c;
                    if (Roots[i].TryGetTarget(out c) && !c.IsDisposed) alive.Add(c);
                    else Roots.RemoveAt(i);
                }
            }

            foreach (var root in alive)
            {
                var r = root;
                if (r.IsHandleCreated && r.InvokeRequired) r.BeginInvoke(new Action(() => ApplyCore(r)));
                else ApplyCore(r);
            }
        }

        private sealed class NewWindowFilter : IMessageFilter
        {
            private const int WmPaint = 0x000F;

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmPaint) return false;
                var c = Control.FromHandle(m.HWnd);
                if (c == null) return false;
                var top = c.TopLevelControl ?? c;
                if (!Tracked.TryGetValue(top, out _)) Track(top);
                return false;
            }
        }

        #endregion

        #region Applying

        private static void ApplyCore(Control c)
        {
            if (c == null || c.IsDisposed) return;
            if (c.Tag is string tag && tag == NoThemeTag) return;

            if (IsColourIsland(c))
            {
                c.ForeColor = Luma(c.BackColor) > 140 ? Color.Black : Theme.Text;
                return;
            }

            var children = new Control[c.Controls.Count];
            c.Controls.CopyTo(children, 0);
            foreach (var child in children) ApplyCore(child);

            var strip = c as ToolStrip;
            if (strip != null)
            {
                // Controls hosted inside a tool strip (combo boxes, text boxes in the toolbar)
                foreach (ToolStripItem item in strip.Items)
                {
                    var host = item as ToolStripControlHost;
                    if (host != null) ApplyCore(host.Control);
                }
            }

            ApplyOne(c);
        }

        private static void ApplyOne(Control c)
        {
            var dark = Theme.IsDark;

            var form = c as Form;
            if (form != null)
            {
                SetColours(form, Theme.Control, Theme.Text);
                OnHandle(form, () => SetTitleBar(form, dark));
                var dialog = form as IDialog;
                if (dialog != null) dialog.UseDarkTheme(dark);
                return;
            }

            var strip = c as ToolStrip;
            if (strip != null)
            {
                if (strip is ToolStripDropDown)
                {
                    // The drop-down background is painted by the renderer, only the text colour is inherited
                    strip.ForeColor = Theme.Text;
                }
                else
                {
                    SetColours(strip, Theme.Control, Theme.Text);
                }
                strip.Invalidate();
                return;
            }

            var button = c as Button;
            if (button != null)
            {
                SetColours(button, Theme.ControlAlt, Theme.Text);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Theme.ButtonBorder;
                button.FlatAppearance.MouseOverBackColor = dark ? Theme.Hover : Color.Empty;
                button.FlatAppearance.MouseDownBackColor = dark ? Theme.Pressed : Color.Empty;
                return;
            }

            if (c is ButtonBase || c is GroupBox || c is Label)
            {
                // FlatStyle.System ignores the colours, which leaves black text on the dark background
                if (dark) FixSystemFlatStyle(c);
                SetColours(c, Theme.Control, Theme.Text);
                return;
            }

            if (c is ComboBox || c is TextBoxBase || c is UpDownBase)
            {
                SetColours(c, Theme.Input, Theme.Text);
                SetNativeTheme(c, "DarkMode_CFD", null);
                return;
            }

            if (c is ListBox || c is ListView || c is TreeView)
            {
                SetColours(c, Theme.Input, Theme.Text);
                var tree = c as TreeView;
                if (tree != null) tree.LineColor = dark ? Theme.BorderStrong : Color.Empty;
                SetNativeTheme(c, "DarkMode_Explorer", "Explorer");
                return;
            }

            SetColours(c, Theme.Control, Theme.Text);

            var scrollable = c as ScrollableControl;
            if (scrollable != null && scrollable.AutoScroll) SetNativeTheme(c, "DarkMode_Explorer", "Explorer");
        }

        private static void SetColours(Control c, Color back, Color fore)
        {
            c.BackColor = back;
            c.ForeColor = fore;
            Applied a;
            if (!AppliedColours.TryGetValue(c, out a))
            {
                a = new Applied();
                AppliedColours.Add(c, a);
            }
            a.Back = back.ToArgb();
            a.Fore = fore.ToArgb();
        }

        private static void FixSystemFlatStyle(Control c)
        {
            var b = c as ButtonBase;
            if (b != null && b.FlatStyle == FlatStyle.System) b.FlatStyle = FlatStyle.Standard;
            var g = c as GroupBox;
            if (g != null && g.FlatStyle == FlatStyle.System) g.FlatStyle = FlatStyle.Standard;
            var l = c as Label;
            if (l != null && l.FlatStyle == FlatStyle.System) l.FlatStyle = FlatStyle.Standard;
        }

        /// <summary>
        /// A control whose BackColor was chosen on purpose (not a system colour, not transparent, not something
        /// this class applied) keeps it. Its children keep theirs too, so the island stays readable.
        /// </summary>
        private static bool IsColourIsland(Control c)
        {
            if (c is Form || c is ToolStrip || ShouldSerializeBackColor == null) return false;

            var back = c.BackColor;
            if (back.A != 255 || back.IsSystemColor) return false;

            Applied a;
            if (AppliedColours.TryGetValue(c, out a) && a.Back == back.ToArgb()) return false;

            try
            {
                return (bool) ShouldSerializeBackColor.Invoke(c, null);
            }
            catch
            {
                return false;
            }
        }

        private static float Luma(Color c)
        {
            return 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        }

        #endregion

        #region Native theming

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmUseImmersiveDarkMode = 20;

        private static void SetTitleBar(Form form, bool dark)
        {
            try
            {
                var value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
                }
            }
            catch
            {
                // Older Windows: no dark title bar, nothing else to do
            }
        }

        /// <summary>
        /// Switch the Win32 theme of a control: dark scroll bars, combo box arrows, spin buttons.
        /// Going back to light only undoes what the dark theme changed.
        /// </summary>
        private static void SetNativeTheme(Control c, string darkTheme, string lightTheme)
        {
            if (Theme.IsDark)
            {
                if (!NativeDark.TryGetValue(c, out _)) NativeDark.Add(c, Marker);
                OnHandle(c, () => Try(() => SetWindowTheme(c.Handle, darkTheme, null)));
            }
            else if (NativeDark.TryGetValue(c, out _))
            {
                NativeDark.Remove(c);
                OnHandle(c, () => Try(() => SetWindowTheme(c.Handle, lightTheme, null)));
            }
        }

        private static void Try(Action action)
        {
            try { action(); }
            catch { /* theming is cosmetic, never let it break a window */ }
        }

        /// <summary>Run an action now if the control has a window handle, otherwise as soon as it gets one.</summary>
        private static void OnHandle(Control c, Action action)
        {
            if (c.IsHandleCreated)
            {
                action();
                return;
            }

            EventHandler handler = null;
            handler = (s, e) =>
            {
                c.HandleCreated -= handler;
                action();
            };
            c.HandleCreated += handler;
        }

        #endregion
    }
}
