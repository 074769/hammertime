using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;

namespace Sledge.BspEditor.Rendering.Viewport
{
    public class RightClickViewportListener : IViewportEventListener
    {
        public string OrderHint => "U";
        public MapViewport Viewport { get; set; }

        private ContextMenuStrip _contextMenu;

        public RightClickViewportListener(MapViewport viewport)
        {
            Viewport = viewport;
            _contextMenu = CreateMenu();
        }

        private ContextMenuStrip CreateMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Closed += (s, e) => StopWatching();
            return menu;
        }

        // Last resort: throw the menu (and its native window) away and make a fresh one
        private void RecreateMenu()
        {
            var old = _contextMenu;
            _contextMenu = CreateMenu();
            try { old.Dispose(); } catch { /* ignore */ }
        }

        // The menu is its own popup window, so Windows doesn't close it when the main window is
        // minimized (e.g. clicking the taskbar button) or the app loses focus. While the menu is open,
        // a background timer watches the real top-level window and asks the menu to close.
        // (A background timer is used on purpose: this class may be created off the UI thread, where a
        // WinForms timer would never tick. Only Win32 calls run on the timer thread; the close itself
        // is marshalled to the menu's own thread.)
        private System.Threading.Timer _watchTimer;
        private IntPtr _rootWindow;
        private IntPtr _menuWindow;
        private volatile bool _menuOpen;
        private int _hiddenTicks;
        private static readonly uint ProcessId = (uint) Process.GetCurrentProcess().Id;

        private void StartWatching()
        {
            StopWatching();
            try
            {
                var control = Viewport.Control;
                _rootWindow = control != null && control.IsHandleCreated ? GetAncestor(control.Handle, GA_ROOTOWNER) : IntPtr.Zero;
                _menuWindow = _contextMenu.IsHandleCreated ? _contextMenu.Handle : IntPtr.Zero;
            }
            catch
            {
                _rootWindow = IntPtr.Zero;
                _menuWindow = IntPtr.Zero;
            }

            _hiddenTicks = 0;
            _menuOpen = true;
            _watchTimer = new System.Threading.Timer(_ => Watch(), null, 150, 150);
        }

        private void StopWatching()
        {
            _menuOpen = false;
            var timer = _watchTimer;
            _watchTimer = null;
            timer?.Dispose();
        }

        private void Watch()
        {
            if (!_menuOpen) return;

            var minimized = _rootWindow != IntPtr.Zero && IsIconic(_rootWindow);

            // The main window handle is a second opinion in case the viewport's own root window isn't the shell
            try
            {
                var main = Process.GetCurrentProcess().MainWindowHandle;
                if (main != IntPtr.Zero && IsIconic(main)) minimized = true;
            }
            catch
            {
                // ignore
            }

            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgProcess);
            var otherApp = fg != IntPtr.Zero && fgProcess != ProcessId;

            if (!minimized && !otherApp)
            {
                _hiddenTicks = 0;
                return;
            }

            _hiddenTicks++;
            var menu = _contextMenu;

            // The menu we know about reports as not visible, so look for any popup window of ours that is
            // still on screen while the app is minimized and hide it.
            SweepVisiblePopups();

            // When the app is minimized, Windows hides the menu's popup window along with its owner but
            // keeps it "shown" internally, and brings it back when the app is restored. Close() doesn't
            // clear that, so destroy the menu window and make a fresh menu. Do it once, right away.
            StopWatching();
            try
            {
                menu.BeginInvoke(new Action(() =>
                {
                    try { menu.Close(ToolStripDropDownCloseReason.AppFocusChange); } catch { /* ignore */ }
                    RecreateMenu();
                }));
            }
            catch
            {
                // handle not available (menu already closed/disposed)
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        private void SweepVisiblePopups()
        {
            try
            {
                EnumWindows((hwnd, lParam) =>
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid != ProcessId || !IsWindowVisible(hwnd)) return true;

                    var style = GetWindowLong(hwnd, GWL_STYLE);
                    var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    var isPopup = (style & WS_POPUP) != 0;
                    var isTopmost = (exStyle & WS_EX_TOPMOST) != 0;
                    var isMainWindow = hwnd == _rootWindow;

                    if (!isMainWindow && isPopup && isTopmost) ShowWindow(hwnd, SW_HIDE);

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // ignore
            }
        }

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_POPUP = unchecked((int) 0x80000000);
        private const int WS_EX_TOPMOST = 0x8;

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);

        private const uint GA_ROOTOWNER = 3;
        private const int SW_HIDE = 0;

        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        public void MouseClick(ViewportEvent e)
        {
            if (e.Button == MouseButtons.Right)
            {
                OpenMenu(e);
            }
        }

        private async Task OpenMenu(ViewportEvent e)
        {
            var mb = new RightClickMenuBuilder(Viewport, e);
            await Oy.Publish("MapViewport:RightClick", mb);
            if (mb.Intercepted || mb.IsEmpty) return;
            mb.Populate(_contextMenu);
            _contextMenu.Show(Viewport.Control, e.X, e.Y);
            StartWatching();
        }

        public bool IsActive()
        {
            return true;
        }

        #region Not required
        
        public void KeyUp(ViewportEvent e)
        {

        }

        public void KeyDown(ViewportEvent e)
        {

        }

        public void MouseMove(ViewportEvent e)
        {

        }

        public void MouseWheel(ViewportEvent e)
        {

        }

        public void MouseUp(ViewportEvent e)
        {

        }

        public void MouseDown(ViewportEvent e)
        {

        }

        public void MouseDoubleClick(ViewportEvent e)
        {

        }

        public void DragStart(ViewportEvent e)
        {

        }

        public void DragMove(ViewportEvent e)
        {

        }

        public void DragEnd(ViewportEvent e)
        {

        }

        public void MouseEnter(ViewportEvent e)
        {
        }

        public void MouseLeave(ViewportEvent e)
        {
        }

        public bool Filter(string hotkey, int keys)
        {
            return false;
        }

        public void Dispose()
        {
            StopWatching();
        }

        public void UpdateFrame(long frame)
        {

        }

        #endregion
    }
}