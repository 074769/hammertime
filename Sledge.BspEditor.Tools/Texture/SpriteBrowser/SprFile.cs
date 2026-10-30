using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Sledge.FileSystem;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>The fixed-size header at the start of a GoldSrc .spr file (IDSP, version 2).</summary>
	public class SprHeader
	{
		public SpriteOrientation Orientation { get; set; }
		public SpriteRenderMode RenderMode { get; set; }
		public float BoundingRadius { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public int NumFrames { get; set; }
		public float BeamLength { get; set; }
		public int SyncType { get; set; }
	}

	/// <summary>One picture of a sprite. Group frames are flattened, each picture keeps its own duration.</summary>
	public class SprFrame : IDisposable
	{
		public int OriginX { get; set; }
		public int OriginY { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }

		/// <summary>Seconds this picture is shown for, or 0 if the sprite has no timing for it (single frames).</summary>
		public float Duration { get; set; }

		/// <summary>32bpp ARGB, straight alpha. For additive sprites the picture is opaque and is added to the background when drawn.</summary>
		public Bitmap Image { get; set; }

		public void Dispose()
		{
			Image?.Dispose();
			Image = null;
		}
	}

	/// <summary>
	/// A reader for GoldSrc .spr files used by the sprite browser (preview, thumbnails, info). The pixel rules match
	/// SprStreamSource so what is shown here looks like what the texture browser and the 3D view show.
	/// </summary>
	public class SprFile : IDisposable
	{
		private const int HeaderSize = 40;
		private const int MaxFrames = 4096;
		private const int MaxDimension = 8192;

		public SprHeader Header { get; private set; }
		public List<SprFrame> Frames { get; } = new List<SprFrame>();

		/// <summary>How many of the frames were stored as animation groups.</summary>
		public int GroupFrames { get; private set; }

		/// <summary>Number of colours stored in the palette.</summary>
		public int PaletteSize { get; private set; }

		public bool HasTiming
		{
			get
			{
				foreach (var f in Frames) if (f.Duration > 0) return true;
				return false;
			}
		}

		/// <summary>Read just the header (cheap). Returns null if this is not a readable .spr.</summary>
		public static SprHeader ReadHeader(IFile file)
		{
			try
			{
				using (var s = file.Open())
				{
					var buffer = new byte[HeaderSize];
					var read = 0;
					while (read < HeaderSize)
					{
						var n = s.Read(buffer, read, HeaderSize - read);
						if (n <= 0) break;
						read += n;
					}
					return read < HeaderSize ? null : ParseHeader(buffer);
				}
			}
			catch
			{
				return null;
			}
		}

		private static SprHeader ParseHeader(byte[] b)
		{
			if (b.Length < HeaderSize) return null;
			if (Encoding.ASCII.GetString(b, 0, 4) != "IDSP") return null;
			if (BitConverter.ToInt32(b, 4) != 2) return null;

			var width = BitConverter.ToInt32(b, 20);
			var height = BitConverter.ToInt32(b, 24);
			var frames = BitConverter.ToInt32(b, 28);
			if (width < 0 || height < 0 || width > MaxDimension || height > MaxDimension || frames < 0 || frames > MaxFrames) return null;

			return new SprHeader
			{
				Orientation = (SpriteOrientation) BitConverter.ToInt32(b, 8),
				RenderMode = (SpriteRenderMode) BitConverter.ToInt32(b, 12),
				BoundingRadius = BitConverter.ToSingle(b, 16),
				Width = width,
				Height = height,
				NumFrames = frames,
				BeamLength = BitConverter.ToSingle(b, 32),
				SyncType = BitConverter.ToInt32(b, 36)
			};
		}

		/// <summary>Read the whole sprite. Returns null if the file is not a readable .spr.</summary>
		public static SprFile Load(IFile file)
		{
			byte[] data;
			using (var s = file.Open())
			using (var ms = new MemoryStream())
			{
				s.CopyTo(ms);
				data = ms.ToArray();
			}

			var header = ParseHeader(data);
			if (header == null) return null;

			var spr = new SprFile { Header = header };
			try
			{
				using (var br = new BinaryReader(new MemoryStream(data)))
				{
					br.BaseStream.Position = HeaderSize;

					var paletteSize = br.ReadInt16();
					spr.PaletteSize = paletteSize;
					var paletteBytes = br.ReadBytes(Math.Max(0, (int) paletteSize) * 3);
					var colours = new Color[256];
					for (var i = 0; i < Math.Min(256, paletteBytes.Length / 3); i++)
					{
						colours[i] = Color.FromArgb(255, paletteBytes[i * 3], paletteBytes[i * 3 + 1], paletteBytes[i * 3 + 2]);
					}

					for (var f = 0; f < header.NumFrames; f++)
					{
						if (br.BaseStream.Position + 4 > br.BaseStream.Length) break;
						var frameType = br.ReadInt32();

						if (frameType == 0)
						{
							if (!ReadFrame(br, spr, header, colours, 0)) break;
						}
						else
						{
							spr.GroupFrames++;
							var count = br.ReadInt32();
							if (count < 0 || count > MaxFrames) break;

							// Stored as the time each picture ends at: turn them into durations
							var ends = new float[count];
							for (var i = 0; i < count; i++) ends[i] = br.ReadSingle();

							var ok = true;
							for (var i = 0; i < count; i++)
							{
								var duration = i == 0 ? ends[0] : ends[i] - ends[i - 1];
								if (!ReadFrame(br, spr, header, colours, duration > 0 ? duration : 0.1f))
								{
									ok = false;
									break;
								}
							}
							if (!ok) break;
						}
					}
				}
			}
			catch
			{
				// Truncated file: keep the frames that were read
			}

			if (spr.Frames.Count == 0)
			{
				spr.Dispose();
				return null;
			}
			return spr;
		}

		private static bool ReadFrame(BinaryReader br, SprFile spr, SprHeader header, Color[] colours, float duration)
		{
			if (br.BaseStream.Position + 16 > br.BaseStream.Length) return false;
			var originX = br.ReadInt32();
			var originY = br.ReadInt32();
			var width = br.ReadInt32();
			var height = br.ReadInt32();
			if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension) return false;

			var length = (long) width * height;
			if (br.BaseStream.Position + length > br.BaseStream.Length) return false;
			var pixels = br.ReadBytes((int) length);

			spr.Frames.Add(new SprFrame
			{
				OriginX = originX,
				OriginY = originY,
				Width = width,
				Height = height,
				Duration = duration,
				Image = ToBitmap(pixels, width, height, colours, header.RenderMode)
			});
			return true;
		}

		private static Bitmap ToBitmap(byte[] pixels, int width, int height, Color[] colours, SpriteRenderMode mode)
		{
			// Palette lookup table in BGRA order, one entry per index
			var lut = new byte[256 * 4];
			var last = colours[255];
			for (var i = 0; i < 256; i++)
			{
				var c = colours[i];
				byte a = 255;
				var r = c.R;
				var g = c.G;
				var b = c.B;

				if (mode == SpriteRenderMode.IndexAlpha && i < 255)
				{
					a = (byte) ((c.R + c.G + c.B) / 3);
					r = last.R;
					g = last.G;
					b = last.B;
				}
				else if (mode == SpriteRenderMode.AlphaTest && i == 255)
				{
					a = 0;
				}

				lut[i * 4 + 0] = b;
				lut[i * 4 + 1] = g;
				lut[i * 4 + 2] = r;
				lut[i * 4 + 3] = a;
			}

			var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try
			{
				var row = new byte[width * 4];
				for (var y = 0; y < height; y++)
				{
					for (var x = 0; x < width; x++)
					{
						var idx = pixels[y * width + x] * 4;
						row[x * 4 + 0] = lut[idx + 0];
						row[x * 4 + 1] = lut[idx + 1];
						row[x * 4 + 2] = lut[idx + 2];
						row[x * 4 + 3] = lut[idx + 3];
					}
					Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
				}
			}
			finally
			{
				bmp.UnlockBits(data);
			}
			return bmp;
		}

		/// <summary>
		/// Draw a frame, centred in the given rectangle, onto a background colour. Additive sprites are added to the
		/// background (like the engine does); every other kind is blended with its alpha.
		/// </summary>
		public static Bitmap Compose(SprFrame frame, SpriteRenderMode mode, Color background)
		{
			var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
			if (mode == SpriteRenderMode.Additive)
			{
				var src = frame.Image.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
				var dst = bmp.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				try
				{
					var row = new byte[frame.Width * 4];
					for (var y = 0; y < frame.Height; y++)
					{
						Marshal.Copy(IntPtr.Add(src.Scan0, y * src.Stride), row, 0, row.Length);
						for (var x = 0; x < frame.Width; x++)
						{
							row[x * 4 + 0] = (byte) Math.Min(255, row[x * 4 + 0] + background.B);
							row[x * 4 + 1] = (byte) Math.Min(255, row[x * 4 + 1] + background.G);
							row[x * 4 + 2] = (byte) Math.Min(255, row[x * 4 + 2] + background.R);
							row[x * 4 + 3] = 255;
						}
						Marshal.Copy(row, 0, IntPtr.Add(dst.Scan0, y * dst.Stride), row.Length);
					}
				}
				finally
				{
					frame.Image.UnlockBits(src);
					bmp.UnlockBits(dst);
				}
			}
			else
			{
				using (var g = Graphics.FromImage(bmp))
				{
					g.Clear(background);
					g.CompositingMode = CompositingMode.SourceOver;
					g.DrawImageUnscaled(frame.Image, 0, 0);
				}
			}
			return bmp;
		}

		/// <summary>
		/// A square thumbnail of the first frame on a solid background, fitted into the square (never cropped).
		/// Small sprites are enlarged with hard pixels, big ones are smoothed down.
		/// </summary>
		public Bitmap RenderThumbnail(int size, Color background)
		{
			var thumb = new Bitmap(size, size, PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(thumb))
			{
				g.Clear(background);
				if (Frames.Count == 0) return thumb;

				var frame = Frames[0];
				using (var composed = Compose(frame, Header.RenderMode, background))
				{
					var pad = 4;
					var avail = size - pad * 2;
					var scale = Math.Min(avail / (float) frame.Width, avail / (float) frame.Height);
					if (scale >= 1) scale = Math.Max(1, (float) Math.Floor(scale)); // whole-number zoom keeps pixels crisp
					var w = Math.Max(1, (int) Math.Round(frame.Width * scale));
					var h = Math.Max(1, (int) Math.Round(frame.Height * scale));

					g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.Half;
					g.DrawImage(composed, new Rectangle((size - w) / 2, (size - h) / 2, w, h));
				}
			}
			return thumb;
		}

		public void Dispose()
		{
			foreach (var f in Frames) f.Dispose();
			Frames.Clear();
		}
	}
}
