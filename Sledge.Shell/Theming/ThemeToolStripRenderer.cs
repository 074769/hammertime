using System.Drawing;
using System.Windows.Forms;
using Sledge.Common.Shell.Theming;

namespace Sledge.Shell.Theming
{
    /// <summary>
    /// The renderer for every menu bar, context menu and drop-down. Stock look in light mode,
    /// palette colours in dark mode (including text and arrows, which the colour table can't reach).
    /// </summary>
    public class ThemeToolStripRenderer : ToolStripProfessionalRenderer
    {
        public ThemeToolStripRenderer() : base(new ThemeColorTable())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (Theme.IsDark) e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (Theme.IsDark) e.ArrowColor = e.Item != null && e.Item.Enabled ? Theme.Text : Theme.TextDisabled;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (Theme.IsDark && e.Item != null)
            {
                // The stock check mark is black: draw our own so it shows on the dark menu
                var r = e.ImageRectangle;
                using (var back = new SolidBrush(Theme.Checked))
                using (var border = new Pen(Theme.BorderStrong))
                using (var pen = new Pen(Theme.Text, 2))
                {
                    e.Graphics.FillRectangle(back, r);
                    e.Graphics.DrawRectangle(border, r.X, r.Y, r.Width - 1, r.Height - 1);
                    var smooth = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new Point(r.Left + 3, r.Top + r.Height / 2),
                        new Point(r.Left + r.Width / 2 - 1, r.Bottom - 4),
                        new Point(r.Right - 3, r.Top + 3)
                    });
                    e.Graphics.SmoothingMode = smooth;
                }
                return;
            }
            base.OnRenderItemCheck(e);
        }
    }
}
