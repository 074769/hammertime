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
        private Color _insideColor = Color.Empty;

        /// <summary>
        /// The control (the menu bar) whose painted colour the inside of the button copies.
        /// The colour is read from what the control actually draws, not from its BackColor,
        /// because the menu bar's renderer paints its own background.
        /// </summary>
        internal Control ColorSource
        {
            get => _colorSource;
            set
            {
                if (_colorSource != null)
                {
                    _colorSource.BackColorChanged -= ColorSourceChanged;
                    _colorSource.SizeChanged -= ColorSourceChanged;
                    if (_colorSource is ToolStrip oldStrip) oldStrip.RendererChanged -= ColorSourceChanged;
                }
                _colorSource = value;
                if (_colorSource != null)
                {
                    _colorSource.BackColorChanged += ColorSourceChanged;
                    _colorSource.SizeChanged += ColorSourceChanged;
                    if (_colorSource is ToolStrip newStrip) newStrip.RendererChanged += ColorSourceChanged;
                }
                ColorSourceChanged(this, EventArgs.Empty);
            }
        }

        private void ColorSourceChanged(object sender, EventArgs e)
        {
            _insideColor = Color.Empty; // re-sample on next paint
            Invalidate();
        }

        /// <summary>
        /// Samples the menu bar's real background: renders it to a bitmap and reads a pixel from the
        /// empty padding at its left edge (no menu item is drawn there).
        /// </summary>
        private Color GetInsideColor(Color fallback)
        {
            if (_colorSource == null) return fallback;
            if (!_insideColor.IsEmpty) return _insideColor;

            var w = _colorSource.Width;
            var h = _colorSource.Height;
            if (w < 2 || h < 2) return _colorSource.BackColor;

            try
            {
                using (var bmp = new Bitmap(w, h))
                {
                    _colorSource.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    _insideColor = bmp.GetPixel(Math.Min(2, w - 1), h / 2);
                }
            }
            catch
            {
                _insideColor = _colorSource.BackColor;
            }
            return _insideColor;
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
            var inside = GetInsideColor(strip);
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

            // Flat by default so it never looks like a selected/inactive document tab; the tab colour only appears on hover
            var fill = inside;
            if (_pressed && _hover) fill = ControlPaint.Light(back, 0.8f);
            else if (_hover) fill = back;

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
