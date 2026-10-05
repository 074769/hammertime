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
            _contextMenu.Closed += (s, e) => UnhookForm();
        }

        // The menu is its own popup window, so Windows doesn't close it when the main window is
        // minimized (e.g. clicking the taskbar button) or the app loses focus. Close it ourselves.
        private Form _hookedForm;

        private void HookForm()
        {
            UnhookForm();
            _hookedForm = Viewport.Control?.FindForm();
            if (_hookedForm == null) return;
            _hookedForm.Resize += CloseMenuIfHidden;
            _hookedForm.Deactivate += CloseMenuIfHidden;
        }

        private void UnhookForm()
        {
            if (_hookedForm == null) return;
            _hookedForm.Resize -= CloseMenuIfHidden;
            _hookedForm.Deactivate -= CloseMenuIfHidden;
            _hookedForm = null;
        }

        private void CloseMenuIfHidden(object sender, System.EventArgs e)
        {
            var form = _hookedForm;
            if (form == null || !_contextMenu.Visible) return;
            if (form.WindowState == FormWindowState.Minimized || Form.ActiveForm == null)
            {
                _contextMenu.Close(ToolStripDropDownCloseReason.AppFocusChange);
            }
        }

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
            HookForm();
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
            UnhookForm();
        }

        public void UpdateFrame(long frame)
        {

        }

        #endregion
    }
}