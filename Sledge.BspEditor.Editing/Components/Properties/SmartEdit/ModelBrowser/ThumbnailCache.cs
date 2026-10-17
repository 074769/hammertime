using System;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Sledge.FileSystem;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser
{
	/// <summary>
	/// A persistent on-disk cache of model thumbnails. An entry is only valid for the exact model it was made from:
	/// the file name carries a hash of the path, the file size and a change stamp (last write time for files on disk,
	/// a content hash for files inside packages) so an edited model gets a fresh thumbnail automatically.
	/// </summary>
	public static class ThumbnailCache
	{
		/// <summary>Bump when thumbnail rendering changes so old thumbnails are regenerated.</summary>
		private const int RenderVersion = 1;

		private static readonly object Lock = new object();
		private static string _folder;

		private static string Folder
		{
			get
			{
				if (_folder == null)
				{
					_folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hammertime", "ModelThumbnails");
				}
				return _folder;
			}
		}

		/// <summary>
		/// Get the thumbnail for a model from the cache, or render it with <paramref name="render"/> and cache it.
		/// Returns null if the model could not be rendered. The caller owns the returned bitmap.
		/// </summary>
		public static Bitmap GetOrCreate(IFile file, int size, Func<Bitmap> render)
		{
			string prefix = null;
			string entry = null;
			try
			{
				prefix = Hash(file.FullPathName.ToLowerInvariant());
				entry = prefix + "_" + file.Size + "_" + Stamp(file).ToString("x") + "_v" + RenderVersion + "s" + size;
			}
			catch
			{
				// Can't build a key for this file: just render it
				return render();
			}

			var png = Path.Combine(Folder, entry + ".png");
			var none = Path.Combine(Folder, entry + ".none");

			try
			{
				if (File.Exists(png))
				{
					using (var fs = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.Read))
					using (var img = Image.FromStream(fs))
					{
						return new Bitmap(img); // detach from the stream
					}
				}
				if (File.Exists(none)) return null; // known to be unrenderable
			}
			catch
			{
				// Corrupt cache entry: fall through and regenerate it
			}

			var bmp = render();

			try
			{
				lock (Lock)
				{
					Directory.CreateDirectory(Folder);

					// Drop stale entries for this model (older versions of the file)
					foreach (var old in Directory.GetFiles(Folder, prefix + "_*"))
					{
						try { File.Delete(old); } catch { }
					}

					if (bmp != null)
					{
						var tmp = png + ".tmp";
						bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);
						File.Move(tmp, png);
					}
					else
					{
						File.WriteAllBytes(none, new byte[0]);
					}
				}
			}
			catch
			{
				// The cache is best effort
			}

			return bmp;
		}

		private static long Stamp(IFile file)
		{
			if (file.Type == FileSystemType.Native)
			{
				return File.GetLastWriteTimeUtc(file.FullPathName).Ticks;
			}

			// Files inside packages have no timestamp: hash the contents (FNV-1a, 64 bit)
			unchecked
			{
				var h = 14695981039346656037UL;
				using (var s = file.Open())
				{
					var buffer = new byte[64 * 1024];
					int read;
					while ((read = s.Read(buffer, 0, buffer.Length)) > 0)
					{
						for (var i = 0; i < read; i++) h = (h ^ buffer[i]) * 1099511628211UL;
					}
				}
				return (long) h;
			}
		}

		private static string Hash(string text)
		{
			using (var sha = SHA1.Create())
			{
				var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
				var sb = new StringBuilder();
				for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2"));
				return sb.ToString();
			}
		}
	}
}
