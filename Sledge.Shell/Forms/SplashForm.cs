using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Sledge.Shell.Forms
{
    /// <summary>
    /// Borderless window shown while Hammertime starts: a 500x300 picture with a small progress bar
    /// (and the current status text) underneath. Startup closes it once the shell is ready.
    /// </summary>
    internal class SplashForm : Form
    {
        private const int ArtWidth = 500;
        private const int ArtHeight = 300;
        private const int BarHeight = 22;

        private static readonly Color TrackColour = Color.FromArgb(36, 36, 40);
        private static readonly Color FillColour = Color.FromArgb(160, 112, 38);
        private static readonly Color BorderColour = Color.FromArgb(128, 128, 128);

        // The picture is a 1000x600 render of SplashScreen.svg (cropped to 5:3, never stretched),
        // drawn at 500x300. It is a PNG so that the splash can paint straight away at startup.
        private readonly Bitmap _art;
        private string _status = "Starting...";
        private int _percent;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(32, 32, 36);
            ClientSize = new Size(ArtWidth, ArtHeight + BarHeight);
            DoubleBuffered = true;

            _art = LoadArt();
        }

        private static Bitmap LoadArt()
        {
            // The splash must never stop the editor from starting, so a missing or bad picture just means no picture
            try
            {
                using (var stream = typeof(SplashForm).Assembly.GetManifestResourceStream("Sledge.Shell.SplashScreen.png"))
                {
                    if (stream == null) return null;
                    using (var image = Image.FromStream(stream)) return new Bitmap(image);
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Update the status text and progress (0-100) and repaint the bar straight away,
        /// even while the UI thread is busy loading.
        /// </summary>
        public void SetStatus(string text, int percent)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => SetStatus(text, percent)));
                return;
            }

            _status = text;
            _percent = Math.Max(0, Math.Min(100, percent));

            if (!IsHandleCreated) return;
            Invalidate(new Rectangle(0, ArtHeight, ArtWidth, BarHeight));
            Update();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            var artRect = new Rectangle(0, 0, ArtWidth, ArtHeight);
            if (_art != null && e.ClipRectangle.IntersectsWith(artRect))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY); // no light edge when scaling down
                    g.DrawImage(_art, artRect, 0, 0, _art.Width, _art.Height, GraphicsUnit.Pixel, attributes);
                }
            }

            var bar = new Rectangle(0, ArtHeight, ArtWidth, BarHeight);
            using (var track = new SolidBrush(TrackColour)) g.FillRectangle(track, bar);

            var fillWidth = ArtWidth * _percent / 100;
            if (fillWidth > 0)
            {
                using (var fill = new SolidBrush(FillColour)) g.FillRectangle(fill, 0, ArtHeight, fillWidth, BarHeight);
            }

            TextRenderer.DrawText(g, _status, SystemFonts.MessageBoxFont, bar, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            using (var border = new Pen(BorderColour)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _art?.Dispose();
            base.Dispose(disposing);
        }
    }
}
