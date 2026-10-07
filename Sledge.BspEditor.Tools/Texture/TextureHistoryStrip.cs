using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Sledge.BspEditor.Tools.Texture
{
    /// <summary>
    /// A horizontal strip of texture preview cells. Exactly <see cref="VisibleCells"/> cells fill the
    /// width of the control, any further cells are reached by scrolling sideways.
    /// </summary>
    public class TextureHistoryStrip : Control
    {
        public const int VisibleCells = 2;
        private const int Gap = 3;

        private readonly List<string> _names = new List<string>();
        private readonly Dictionary<string, Bitmap> _thumbnails = new Dictionary<string, Bitmap>(StringComparer.InvariantCultureIgnoreCase);
        private readonly HScrollBar _scroll;
        private readonly ToolTip _tip = new ToolTip();

        private string _selected;
        private int _offset;
        private int _hoverIndex = -1;
        private bool _updating;

        /// <summary>
        /// Raised when the user clicks a cell. The argument is the texture name.
        /// </summary>
        public event EventHandler<string> TextureClicked;

        public TextureHistoryStrip()
        {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            _scroll = new HScrollBar { Dock = DockStyle.Bottom, Visible = false, Minimum = 0 };
            _scroll.ValueChanged += ScrollValueChanged;
            Controls.Add(_scroll);

            TabStop = false;
        }

        /// <summary>
        /// True when there are more cells than fit, so the scroll bar is shown.
        /// </summary>
        public bool NeedsScrollBar => _names.Count > VisibleCells;

        public int ScrollBarHeight => _scroll.Height;

        public static int CellWidthFor(int stripWidth)
        {
            return Math.Max(1, (stripWidth - Gap * (VisibleCells - 1)) / VisibleCells);
        }

        private int CellWidth => CellWidthFor(Width);

        private int CellAreaHeight => Math.Max(1, Height - (_scroll.Visible ? _scroll.Height : 0));

        private int TotalWidth()
        {
            var n = _names.Count;
            return n <= 0 ? 0 : n * CellWidth + (n - 1) * Gap;
        }

        public string SelectedName
        {
            get => _selected;
            set
            {
                _selected = value;
                var idx = IndexOf(value);
                if (idx >= 0) EnsureVisible(idx);
                Invalidate();
            }
        }

        private int IndexOf(string name)
        {
            if (name == null) return -1;
            return _names.FindIndex(x => string.Equals(x, name, StringComparison.InvariantCultureIgnoreCase));
        }

        /// <summary>
        /// Replace the list of textures shown. Thumbnails of textures no longer listed are disposed.
        /// </summary>
        public void SetNames(IEnumerable<string> names, bool scrollToStart)
        {
            _names.Clear();
            _names.AddRange(names);

            var stale = _thumbnails.Keys
                .Where(k => !_names.Contains(k, StringComparer.InvariantCultureIgnoreCase))
                .ToList();
            foreach (var key in stale)
            {
                _thumbnails[key].Dispose();
                _thumbnails.Remove(key);
            }

            if (scrollToStart) _offset = 0;
            _hoverIndex = -1;
            UpdateScroll();
            Invalidate();
        }

        /// <summary>
        /// Give the strip a thumbnail for a texture. The strip takes ownership of the bitmap.
        /// </summary>
        public void SetThumbnail(string name, Bitmap bitmap)
        {
            if (name == null || bitmap == null) return;

            if (IndexOf(name) < 0)
            {
                bitmap.Dispose();
                return;
            }

            if (_thumbnails.TryGetValue(name, out var old) && !ReferenceEquals(old, bitmap)) old.Dispose();
            _thumbnails[name] = bitmap;
            Invalidate();
        }

        public void ClearThumbnails()
        {
            foreach (var b in _thumbnails.Values) b.Dispose();
            _thumbnails.Clear();
            Invalidate();
        }

        private void EnsureVisible(int index)
        {
            var step = CellWidth + Gap;
            var left = index * step;
            var right = left + CellWidth;

            // Only move when the whole cell is out of sight, so clicking a half visible cell doesn't make it jump
            if (right <= _offset || left >= _offset + Width)
            {
                _offset = left;
                UpdateScroll();
            }
        }

        private void UpdateScroll()
        {
            var need = NeedsScrollBar;
            var total = TotalWidth();
            var view = Math.Max(1, Width);
            var max = Math.Max(0, total - view);
            _offset = Math.Min(Math.Max(_offset, 0), max);

            _updating = true;
            try
            {
                if (_scroll.Visible != need) _scroll.Visible = need;
                if (need)
                {
                    _scroll.Minimum = 0;
                    _scroll.Maximum = Math.Max(1, total - 1);
                    _scroll.LargeChange = view;
                    _scroll.SmallChange = Math.Max(1, CellWidth / 4);
                    _scroll.Value = Math.Min(_offset, _scroll.Maximum);
                }
            }
            finally
            {
                _updating = false;
            }
        }

        private void ScrollValueChanged(object sender, EventArgs e)
        {
            if (_updating) return;
            _offset = _scroll.Value;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!_scroll.Visible)
            {
                base.OnMouseWheel(e);
                return;
            }

            var max = Math.Max(0, TotalWidth() - Width);
            var step = (CellWidth + Gap) / 2;
            _offset = Math.Min(Math.Max(_offset - Math.Sign(e.Delta) * step, 0), max);
            UpdateScroll();
            Invalidate();

            if (e is HandledMouseEventArgs h) h.Handled = true;
        }

        private int IndexAt(Point p)
        {
            if (p.Y < 0 || p.Y >= CellAreaHeight) return -1;
            var cellW = CellWidth;
            var step = cellW + Gap;
            var x = p.X + _offset;
            if (x < 0) return -1;
            var i = x / step;
            if (i >= _names.Count) return -1;
            return x - i * step < cellW ? i : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var idx = IndexAt(e.Location);
            if (idx == _hoverIndex) return;

            _hoverIndex = idx;
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            _tip.SetToolTip(this, idx >= 0 ? _names[idx] : string.Empty);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex < 0) return;
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;

            var idx = IndexAt(e.Location);
            if (idx < 0) return;

            _selected = _names[idx];
            Invalidate();
            TextureClicked?.Invoke(this, _names[idx]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var cellW = CellWidth;
            var cellH = CellAreaHeight;
            var step = cellW + Gap;

            g.Clear(BackColor);
            g.SetClip(new Rectangle(0, 0, Width, cellH));

            var slots = Math.Max(_names.Count, VisibleCells);

            using (var border = new Pen(ControlPaint.Dark(BackColor, 0.1f)))
            using (var hover = new Pen(SystemColors.HotTrack))
            using (var selected = new Pen(SystemColors.Highlight, 2))
            {
                for (var i = 0; i < slots; i++)
                {
                    var x = i * step - _offset;
                    if (x + cellW < 0 || x > Width) continue;

                    g.DrawRectangle(border, new Rectangle(x, 0, cellW - 1, cellH - 1));
                    if (i >= _names.Count) continue;

                    var name = _names[i];
                    var area = Rectangle.Inflate(new Rectangle(x, 0, cellW, cellH), -3, -3);
                    DrawTexture(g, name, area);

                    if (string.Equals(name, _selected, StringComparison.InvariantCultureIgnoreCase))
                    {
                        g.DrawRectangle(selected, new Rectangle(x + 1, 1, cellW - 3, cellH - 3));
                    }
                    else if (i == _hoverIndex)
                    {
                        g.DrawRectangle(hover, new Rectangle(x, 0, cellW - 1, cellH - 1));
                    }
                }
            }
        }

        private void DrawTexture(Graphics g, string name, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return;

            if (_thumbnails.TryGetValue(name, out var bmp) && bmp.Width > 0 && bmp.Height > 0)
            {
                var scale = Math.Min((float) area.Width / bmp.Width, (float) area.Height / bmp.Height);
                var w = Math.Max(1, (int) (bmp.Width * scale));
                var h = Math.Max(1, (int) (bmp.Height * scale));
                var dest = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);

                var oldInterpolation = g.InterpolationMode;
                var oldOffset = g.PixelOffsetMode;
                g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, dest);
                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldOffset;
            }
            else
            {
                TextRenderer.DrawText(g, name, Font, area, ForeColor,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearThumbnails();
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
