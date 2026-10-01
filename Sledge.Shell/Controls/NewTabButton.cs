using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sledge.Shell.Controls
{
    /// <summary>
    /// The "+" tab after the last document tab. A real control, so it gets its own
    /// hover / press / click handling. Drawn with the same shape and colours as a tab.
    /// </summary>
    public class NewTabButton : Control
    {
        private bool _hover;
        private bool _pressed;

        internal ClosableTabControl Source { get; set; }

        public NewTabButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            var wasPressed = _pressed;
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
            // Click is raised by the base class when released over the control
            if (wasPressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) OnClick(EventArgs.Empty);
        }

        // Stop the base class raising Click a second time on mouse up
        protected override void OnMouseClick(MouseEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var src = Source;
            var back = src != null ? src.TabBackColor : BackColor;
            var fore = src != null ? src.TabForeColor : ForeColor;

            // Parent colour behind the tab shape
            using (var b = new SolidBrush(src != null ? src.BackColor : BackColor)) g.FillRectangle(b, ClientRectangle);

            var tab = src != null ? src.FirstTabRect : new Rectangle(0, 2, 0, 22);
            var rect = new Rectangle(0, tab.Top, Width - 1, tab.Height);

            var points = new[]
            {
                new Point(rect.Left, rect.Bottom),
                new Point(rect.Left, rect.Top + 3),
                new Point(rect.Left + 3, rect.Top),
                new Point(rect.Right - 3, rect.Top),
                new Point(rect.Right, rect.Top + 3),
                new Point(rect.Right, rect.Bottom),
                new Point(rect.Left, rect.Bottom)
            };

            var fill = back;
            if (_pressed && _hover) fill = ControlPaint.Light(back, 1);
            else if (_hover) fill = ControlPaint.Light(back, 0.8f);

            using (var b = new SolidBrush(fill)) g.FillPolygon(b, points);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawPolygon(SystemPens.ControlDark, points);

            using (var pen = new Pen(fore))
            {
                var cx = rect.Left + rect.Width / 2;
                var cy = rect.Top + 3 + (rect.Height - 3) / 2;
                const int arm = 5;
                g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
            }
        }
    }
}
