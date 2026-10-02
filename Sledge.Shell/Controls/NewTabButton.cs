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

        private Control _colorSource;

        /// <summary>
        /// The control (the menu bar) whose BackColor the inside of the button copies, so the "+" always matches it
        /// </summary>
        internal Control ColorSource
        {
            get => _colorSource;
            set
            {
                if (_colorSource != null) _colorSource.BackColorChanged -= ColorSourceChanged;
                _colorSource = value;
                if (_colorSource != null) _colorSource.BackColorChanged += ColorSourceChanged;
                Invalidate();
            }
        }

        private void ColorSourceChanged(object sender, EventArgs e) { Invalidate(); }

        private static Color Blend(Color from, Color to, float amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

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
            // The base class raises Click when the mouse is released over the control
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var src = Source;
            var back = src != null ? src.TabBackColor : BackColor;
            var fore = src != null ? src.TabForeColor : ForeColor;

            // Strip colour behind the tab shape (src.BackColor is the un-themed control colour, which showed up as white above the button)
            var strip = src != null ? src.StripColor : BackColor;
            using (var b = new SolidBrush(strip)) g.FillRectangle(b, ClientRectangle);

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

            // Mid grey (the inactive-tab grey): clearly lighter than the dark bar, clearly darker than the selected tab.
            // Hover/press only nudge it lighter, so it never turns into the selected tab's light grey.
            var fill = back;
            if (_pressed && _hover) fill = Blend(back, Color.White, 0.30f);
            else if (_hover) fill = Blend(back, Color.White, 0.15f);

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
