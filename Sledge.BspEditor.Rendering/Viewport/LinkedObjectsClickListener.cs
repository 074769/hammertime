using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using Sledge.BspEditor.Rendering.Overlay;

namespace Sledge.BspEditor.Rendering.Viewport
{
    [Export(typeof(IViewportEventListenerFactory))]
    public class LinkedObjectsClickListenerFactory : IViewportEventListenerFactory
    {
        public IEnumerable<IViewportEventListener> Create(MapViewport viewport)
        {
            yield return new LinkedObjectsClickListener(viewport);
        }
    }

    /// <summary>
    /// Clicking an instance's label in a 2D or 3D viewport selects every object in that instance.
    /// Hold Alt to select the whole link (every instance), or Ctrl or Shift to add to the current selection.
    ///
    /// Runs before the tools ("D"), so a click on a label doesn't also reach the selection tool.
    /// </summary>
    public class LinkedObjectsClickListener : IViewportEventListener
    {
        public string OrderHint => "C";
        public MapViewport Viewport { get; set; }

        private bool _captured;
        private bool _suppressClick;

        public LinkedObjectsClickListener(MapViewport viewport)
        {
            Viewport = viewport;
        }

        public bool IsActive()
        {
            return LinkedObjectsOverlay.Current != null;
        }

        private bool OverLabel(ViewportEvent e)
        {
            var overlay = LinkedObjectsOverlay.Current;
            return overlay != null && overlay.IsOverLabel(Viewport.Viewport, e.X, e.Y);
        }

        public void MouseDown(ViewportEvent e)
        {
            _captured = false;
            _suppressClick = false;

            if (e.Button != MouseButtons.Left || !OverLabel(e)) return;

            // Take the press so the tools don't start a selection box or drag from it
            _captured = true;
            e.Handled = true;
        }

        public void MouseUp(ViewportEvent e)
        {
            if (!_captured) return;
            _captured = false;
            _suppressClick = true;
            e.Handled = true;

            if (e.Button != MouseButtons.Left) return;
            LinkedObjectsOverlay.Current?.ClickLabel(Viewport.Viewport, e.X, e.Y, e.Control || e.Shift, e.Alt);
        }

        public void MouseClick(ViewportEvent e)
        {
            if (!_suppressClick) return;
            _suppressClick = false;
            e.Handled = true;
        }

        public void DragStart(ViewportEvent e)
        {
            if (_captured) e.Handled = true;
        }

        public void DragMove(ViewportEvent e)
        {
            if (_captured) e.Handled = true;
        }

        public void DragEnd(ViewportEvent e)
        {
            if (_captured) e.Handled = true;
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

        public void MouseDoubleClick(ViewportEvent e)
        {
            // A double click on a label is two label clicks: don't let it reach the tools as one
            if (OverLabel(e)) e.Handled = true;
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
        }

        public void UpdateFrame(long frame)
        {
        }

        #endregion
    }
}
