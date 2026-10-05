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

        private readonly ContextMenuStrip _contextMenu;

        public RightClickViewportListener(MapViewport viewport)
        {
            Viewport = viewport;
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Closed += (s, e) => StopWatching();
        }

        // The menu is its own popup window, so Windows doesn't close it when the main window is
        // minimized (e.g. clicking the taskbar button) or the app loses focus. While the menu is open,
        // a background timer watches the real top-level window and asks the menu to close.
        // (A background timer is used on purpose: this class may be created off the UI thread, where a
        // WinForms timer would never tick. Only Win32 calls run on the timer thread; the close itself
        // is marshalled to the menu's own thread.)
        private System.Threading.Timer _watchTimer;
        private IntPtr _rootWindow;
        private volatile bool _menuOpen;

        private void StartWatching()
        {
            StopWatching();
            try
            {
                var control = Viewport.Control;
                _rootWindow = control != null && control.IsHandleCreated ? GetAncestor(control.Handle, GA_ROOTOWNER) : IntPtr.Zero;
            }
            catch
            {
                _rootWindow = IntPtr.Zero;
            }

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

            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgProcess);
            var otherApp = fg != IntPtr.Zero && fgProcess != (uint) Process.GetCurrentProcess().Id;

            if (!minimized && !otherApp) return;

            try
            {
                _contextMenu.BeginInvoke(new Action(() =>
                {
                    if (_contextMenu.Visible) _contextMenu.Close(ToolStripDropDownCloseReason.AppFocusChange);
                }));
            }
            catch
            {
                // handle not available (menu already closed/disposed)
            }
        }

        private const uint GA_ROOTOWNER = 3;

        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
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