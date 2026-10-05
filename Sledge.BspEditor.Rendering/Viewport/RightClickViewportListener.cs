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
            _watchTimer = new Timer { Interval = 150 };
            _watchTimer.Tick += (s, e) => CloseMenuIfAppHidden();
        }

        // The menu is its own popup window, so Windows doesn't close it when the main window is
        // minimized (e.g. clicking the taskbar button) or the app loses focus. The viewport lives in a
        // docked panel (not the shell window itself), so form events on it are unreliable.
        // Instead, poll the real top-level window while the menu is open.
        private readonly Timer _watchTimer;

        private void StartWatching() => _watchTimer.Start();

        private void StopWatching() => _watchTimer.Stop();

        private void CloseMenuIfAppHidden()
        {
            if (!_contextMenu.Visible)
            {
                StopWatching();
                return;
            }

            var control = Viewport.Control;
            if (control == null || control.IsDisposed || !control.IsHandleCreated)
            {
                _contextMenu.Close(ToolStripDropDownCloseReason.AppFocusChange);
                return;
            }

            var root = GetAncestor(control.Handle, GA_ROOTOWNER);
            var minimized = root != IntPtr.Zero && IsIconic(root);

            var fg = GetForegroundWindow();
            GetWindowThreadProcessId(fg, out var fgProcess);
            var otherApp = fg != IntPtr.Zero && fgProcess != (uint) Process.GetCurrentProcess().Id;

            if (minimized || otherApp)
            {
                _contextMenu.Close(ToolStripDropDownCloseReason.AppFocusChange);
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
            _watchTimer.Dispose();
        }

        public void UpdateFrame(long frame)
        {

        }

        #endregion
    }
}