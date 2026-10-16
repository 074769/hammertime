using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sledge.Shell.Theming
{
    /// <summary>
    /// Makes icons drawn in black / dark grey (most of the toolbar icons) visible on a dark background.
    /// Neutral (grey) pixels are inverted, dark coloured pixels are lifted, bright coloured pixels are left alone.
    /// The adapted copy is cached per source image; the source image is never changed.
    /// </summary>
    public static class ThemeIcons
    {
        private static readonly ConditionalWeakTable<Image, Image> Cache = new ConditionalWeakTable<Image, Image>();

        /// <summary>The image to draw on a dark background (the same image if it can't be adapted).</summary>
        public static Image ForDark(Image source)
        {
            if (source == null) return null;

            lock (Cache)
            {
                Image cached;
                if (Cache.TryGetValue(source, out cached)) return cached;

                Image adapted;
                try
                {
                    adapted = Adapt(source);
                }
                catch
                {
                    adapted = source;
                }
                Cache.Add(source, adapted);
                return adapted;
            }
        }

        private static Image Adapt(Image source)
        {
            var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.DrawImage(source, new Rectangle(0, 0, bmp.Width, bmp.Height));
            }

            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[Math.Abs(data.Stride) * data.Height];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);

                for (var i = 0; i + 3 < bytes.Length; i += 4)
                {
                    if (bytes[i + 3] == 0) continue;

                    int b = bytes[i], g = bytes[i + 1], r = bytes[i + 2];
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));
                    var saturation = max == 0 ? 0f : (max - min) / (float) max;
                    var luma = 0.299f * r + 0.587f * g + 0.114f * b;

                    if (saturation < 0.25f)
                    {
                        // Black / grey / white artwork: invert so black lines become light
                        r = 255 - r;
                        g = 255 - g;
                        b = 255 - b;
                    }
                    else if (luma < 110f)
                    {
                        // Dark colours (navy, dark red...): lift towards white, keep the hue
                        r += (int) ((255 - r) * 0.45f);
                        g += (int) ((255 - g) * 0.45f);
                        b += (int) ((255 - b) * 0.45f);
                    }

                    bytes[i] = (byte) b;
                    bytes[i + 1] = (byte) g;
                    bytes[i + 2] = (byte) r;
                }

                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            return bmp;
        }
    }
}
