using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sledge.Shell.Controls
{
    /// <summary>
    /// The scrolling panel of the settings page. It differs from a plain scrolling panel in two ways:
    /// it never scrolls by itself to show the control that has the focus (it only scrolls when the user
    /// scrolls it), and it scrolls while the scroll bar thumb is being dragged, not once it is let go.
    /// </summary>
    public class SettingsScrollPanel : TableLayoutPanel
    {
        private const int WM_VSCROLL = 0x0115;
        private const int SB_VERT = 1;
        private const int SB_THUMBTRACK = 5;
        private const uint SIF_TRACKPOS = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hwnd, int nBar, ref SCROLLINFO lpsi);

        /// <summary>
        /// A scrolling panel scrolls the control that gets the focus into view. Staying where it is instead.
        /// </summary>
        protected override Point ScrollToControl(Control activeControl)
        {
            return DisplayRectangle.Location;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_VSCROLL && (m.WParam.ToInt64() & 0xFFFF) == SB_THUMBTRACK)
            {
                var info = new SCROLLINFO { cbSize = (uint) Marshal.SizeOf(typeof(SCROLLINFO)), fMask = SIF_TRACKPOS };
                if (GetScrollInfo(Handle, SB_VERT, ref info))
                {
                    VerticalScroll.Value = Math.Max(VerticalScroll.Minimum, Math.Min(VerticalScroll.Maximum, info.nTrackPos));
                    return;
                }
            }

            base.WndProc(ref m);
        }
    }
}
