using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using Sledge.FileSystem;
using Sledge.Providers.Texture.Wad.Format;

namespace Sledge.Providers.Texture.Wad
{
    public class WadStreamSource : ITextureStreamSource
    {
        private readonly WadPackageStreamSource _stream;
        private readonly WadPackage _package;

        public WadStreamSource(IFile file)
        {
            _package = new WadPackage(file);
            _stream = new WadPackageStreamSource(_package);
        }

        public bool HasImage(string item)
        {
            return _stream.HasEntry(item);
        }

        public async Task<ICollection<Bitmap>> GetImage(string item, int maxWidth, int maxHeight)
        {
            var entry = _stream.GetEntry(item);
            if (entry == null) return null;

            return await Task.Factory.StartNew(() =>
            {
                using (var s = _stream.OpenEntry(entry))
                {
                    return new Collection<Bitmap>() { PostProcessBitmap(_package.File.Name, entry.Name, new Bitmap(s)) } as ICollection<Bitmap>;
                }
            });
        }

		public async Task<ICollection<Bitmap>> GetRawImage(string item, int maxWidth, int maxHeight)
		{
			var entry = _stream.GetEntry(item);
			if (entry == null) return null;

			return await Task.Factory.StartNew(() =>
			{
				using (var s = _stream.OpenEntry(entry))
				{
					return new Collection<Bitmap>() { new Bitmap(s) } as ICollection<Bitmap>;
				}
			});
		}

		private static Bitmap PostProcessBitmap(string packageName, string name, Bitmap bmp)
        {
            // Transparent textures are named like: {Name
            if (!name.StartsWith("{")) return bmp;
            if (bmp.PixelFormat != PixelFormat.Format8bppIndexed) return bmp;

            var palette = bmp.Palette.Entries;
            if (palette.Length == 0) return bmp;
            var last = palette[palette.Length - 1];

            // Two transparency types: "blue" transparency and "decal" transparency
            // Can't be clever and detect the transparency type automatically - Goldsource is too unpredictable
            // decal.wad is hard-coded in the engine, so this is relatively safe, except for custom engines
            var blueTransparency = packageName.IndexOf("decal", StringComparison.CurrentCultureIgnoreCase) < 0;

            // Work out the final colour of every palette index. This has to be done on the pixels: setting alpha on the
            // palette of an 8bpp bitmap is ignored when it is drawn (GDI+ needs a palette flag that can't be set from here),
            // which is why the transparent colour used to show up as a solid colour.
            var lut = new Color[palette.Length];
            if (blueTransparency)
            {
                // Palette index 255 is the transparent one; it gets no colour at all so it can't bleed into the edges
                for (var i = 0; i < palette.Length - 1; i++) lut[i] = Color.FromArgb(255, palette[i]);
                lut[palette.Length - 1] = Color.FromArgb(0, 0, 0, 0);
            }
            else
            {
                // Decal: the greyscale value is the alpha, the colour is the last palette entry.
                // If the first item is black, we need to flip the transparency calculation (I think...)
                var isBlack = palette[0].R == 0 && palette[0].G == 0 && palette[0].B == 0;
                for (var i = 0; i < palette.Length - 1; i++) lut[i] = Color.FromArgb(isBlack ? palette[i].R : 255 - palette[i].R, last);
                lut[palette.Length - 1] = last;
            }

            // Build a proper 32bit image with an alpha channel
            var width = bmp.Width;
            var height = bmp.Height;
            var clone = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var src = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
            var dst = clone.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var indices = new byte[width];
                var row = new byte[width * 4];
                for (var y = 0; y < height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(src.Scan0, y * src.Stride), indices, 0, width);
                    for (var x = 0; x < width; x++)
                    {
                        var c = lut[Math.Min((int) indices[x], lut.Length - 1)];
                        row[x * 4 + 0] = c.B;
                        row[x * 4 + 1] = c.G;
                        row[x * 4 + 2] = c.R;
                        row[x * 4 + 3] = c.A;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, y * dst.Stride), row.Length);
                }
            }
            finally
            {
                bmp.UnlockBits(src);
                clone.UnlockBits(dst);
            }

            bmp.Dispose();
            return clone;
        }

        public void Dispose()
        {
            _stream.Dispose();
        }


	}
}