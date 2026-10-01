using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using Sledge.Providers.Model.Mdl10.Format;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser
{
	/// <summary>
	/// Renders a static thumbnail of a GoldSource .mdl on the CPU (z-buffered, textured, flat shaded).
	/// Uses sequence 0 / frame 0 and the first model of every bodypart. No GPU or engine resources are needed.
	/// </summary>
	public static class ModelThumbnailRenderer
	{
		private const int SuperSample = 2;
		private const int Margin = 6;

		private struct Tri
		{
			public Vector3 P0, P1, P2;
			public Vector2 T0, T1, T2;
			public int Texture; // index into the texture table, or -1
		}

		private class TextureBitmap
		{
			public int Width;
			public int Height;
			public int[] Argb;
			public bool Flat; // chrome etc: use a flat colour instead of sampling
			public bool Fullbright;
		}

		/// <summary>
		/// Render the model. Returns null if the model has no geometry.
		/// </summary>
		public static Bitmap Render(MdlFile mdl, int size, Color background, int skin = 0, float azimuthDegrees = 35f, float elevationDegrees = 22f)
		{
			var transforms = GetBoneTransforms(mdl);
			var textures = BuildTextures(mdl);
			var tris = CollectTriangles(mdl, transforms, skin);
			if (tris.Count == 0) return null;

			// Camera basis (GoldSource: X forward, Y left, Z up). The camera sits in front of the model looking at the origin.
			var az = azimuthDegrees * (float) Math.PI / 180f;
			var el = elevationDegrees * (float) Math.PI / 180f;
			var camPos = new Vector3((float) (Math.Cos(el) * Math.Cos(az)), (float) (Math.Cos(el) * Math.Sin(az)), (float) Math.Sin(el));
			var dir = -camPos; // camera -> scene
			var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ));
			var up = Vector3.Cross(right, dir);
			var light = Vector3.Normalize(-dir + up * 0.6f - right * 0.4f);

			// Fit to the viewport using the projected bounds
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			foreach (var t in tris)
			{
				foreach (var p in new[] { t.P0, t.P1, t.P2 })
				{
					var x = Vector3.Dot(p, right);
					var y = -Vector3.Dot(p, up);
					if (x < minX) minX = x;
					if (x > maxX) maxX = x;
					if (y < minY) minY = y;
					if (y > maxY) maxY = y;
				}
			}

			var px = size * SuperSample;
			var w = Math.Max(maxX - minX, 0.001f);
			var h = Math.Max(maxY - minY, 0.001f);
			var avail = px - Margin * 2 * SuperSample;
			var scale = Math.Min(avail / w, avail / h);
			var offX = (px - w * scale) / 2f - minX * scale;
			var offY = (px - h * scale) / 2f - minY * scale;

			var colour = new int[px * px];
			var depth = new float[px * px];
			var bg = background.ToArgb();
			for (var i = 0; i < colour.Length; i++)
			{
				colour[i] = bg;
				depth[i] = float.MaxValue;
			}

			foreach (var t in tris)
			{
				var tex = t.Texture >= 0 && t.Texture < textures.Count ? textures[t.Texture] : null;

				var n = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0);
				var shade = 1f;
				if (n.LengthSquared() > 1e-9f && (tex == null || !tex.Fullbright))
				{
					n = Vector3.Normalize(n);
					shade = 0.45f + 0.55f * Math.Abs(Vector3.Dot(n, light)); // two-sided
				}

				var s0 = new Vector2(Vector3.Dot(t.P0, right) * scale + offX, -Vector3.Dot(t.P0, up) * scale + offY);
				var s1 = new Vector2(Vector3.Dot(t.P1, right) * scale + offX, -Vector3.Dot(t.P1, up) * scale + offY);
				var s2 = new Vector2(Vector3.Dot(t.P2, right) * scale + offX, -Vector3.Dot(t.P2, up) * scale + offY);
				var z0 = Vector3.Dot(t.P0, dir);
				var z1 = Vector3.Dot(t.P1, dir);
				var z2 = Vector3.Dot(t.P2, dir);

				Rasterise(colour, depth, px, s0, s1, s2, z0, z1, z2, t.T0, t.T1, t.T2, tex, shade);
			}

			using (var big = new Bitmap(px, px, PixelFormat.Format32bppArgb))
			{
				var bd = big.LockBits(new Rectangle(0, 0, px, px), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				try { Marshal.Copy(colour, 0, bd.Scan0, colour.Length); }
				finally { big.UnlockBits(bd); }

				var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
				using (var g = Graphics.FromImage(result))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBicubic;
					g.PixelOffsetMode = PixelOffsetMode.HighQuality;
					g.DrawImage(big, new Rectangle(0, 0, size, size));
				}
				return result;
			}
		}

		/// <summary>
		/// Axis-aligned bounds of the geometry (sequence 0, frame 0, first model of each bodypart), or null if there is none.
		/// </summary>
		public static (Vector3 min, Vector3 max, int triangles)? GetBounds(MdlFile mdl)
		{
			var tris = CollectTriangles(mdl, GetBoneTransforms(mdl), 0);
			if (tris.Count == 0) return null;
			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);
			foreach (var t in tris)
			{
				foreach (var p in new[] { t.P0, t.P1, t.P2 })
				{
					min = Vector3.Min(min, p);
					max = Vector3.Max(max, p);
				}
			}
			return (min, max, tris.Count);
		}

		private static void Rasterise(int[] colour, float[] depth, int px, Vector2 a, Vector2 b, Vector2 c,
			float za, float zb, float zc, Vector2 ta, Vector2 tb, Vector2 tc, TextureBitmap tex, float shade)
		{
			var minX = Math.Max(0, (int) Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
			var maxX = Math.Min(px - 1, (int) Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
			var minY = Math.Max(0, (int) Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
			var maxY = Math.Min(px - 1, (int) Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
			if (minX > maxX || minY > maxY) return;

			var area = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
			if (Math.Abs(area) < 1e-6f) return;
			var inv = 1f / area;

			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					var pxc = x + 0.5f;
					var pyc = y + 0.5f;
					var w0 = ((b.X - pxc) * (c.Y - pyc) - (b.Y - pyc) * (c.X - pxc)) * inv;
					var w1 = ((c.X - pxc) * (a.Y - pyc) - (c.Y - pyc) * (a.X - pxc)) * inv;
					var w2 = 1f - w0 - w1;
					const float eps = -0.0005f;
					if (w0 < eps || w1 < eps || w2 < eps) continue;

					var z = w0 * za + w1 * zb + w2 * zc;
					var idx = y * px + x;
					if (z >= depth[idx]) continue;

					int argb;
					if (tex == null)
					{
						argb = unchecked((int) 0xFFB4B4B4);
					}
					else if (tex.Flat)
					{
						argb = unchecked((int) 0xFFA0A0A8);
					}
					else
					{
						var u = w0 * ta.X + w1 * tb.X + w2 * tc.X;
						var v = w0 * ta.Y + w1 * tb.Y + w2 * tc.Y;
						var iu = Mod((int) Math.Floor(u), tex.Width);
						var iv = Mod((int) Math.Floor(v), tex.Height);
						argb = tex.Argb[iv * tex.Width + iu];
						if ((argb >> 24) == 0) continue; // masked / transparent pixel
					}

					var r = (int) (((argb >> 16) & 0xFF) * shade);
					var g = (int) (((argb >> 8) & 0xFF) * shade);
					var bl = (int) ((argb & 0xFF) * shade);
					depth[idx] = z;
					colour[idx] = unchecked((int) 0xFF000000) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(bl);
				}
			}
		}

		private static int Mod(int v, int m)
		{
			if (m <= 0) return 0;
			var r = v % m;
			return r < 0 ? r + m : r;
		}

		private static int Clamp(int v)
		{
			return v < 0 ? 0 : v > 255 ? 255 : v;
		}

		private static Matrix4x4[] GetBoneTransforms(MdlFile mdl)
		{
			var count = Math.Max(1, mdl.Bones?.Count ?? 0);
			var transforms = new Matrix4x4[count];
			for (var i = 0; i < count; i++) transforms[i] = Matrix4x4.Identity;

			try
			{
				if (mdl.Bones != null && mdl.Bones.Count > 0 && mdl.Sequences != null && mdl.Sequences.Count > 0)
				{
					var seq = mdl.Sequences[0];
					if (seq.Header.NumFrames > 0 && seq.Blends != null && seq.Blends.Length > 0
					    && seq.Blends[0].Frames != null && seq.Blends[0].Frames.Length > 0)
					{
						mdl.GetTransforms(0, 0, 0, ref transforms);
					}
				}
			}
			catch
			{
				// Bad or unsupported animation data: fall back to the bind pose
				for (var i = 0; i < count; i++) transforms[i] = Matrix4x4.Identity;
			}

			return transforms;
		}

		private static List<TextureBitmap> BuildTextures(MdlFile mdl)
		{
			var list = new List<TextureBitmap>();
			if (mdl.Textures == null) return list;

			foreach (var t in mdl.Textures)
			{
				var tb = new TextureBitmap
				{
					Width = Math.Max(1, t.Header.Width),
					Height = Math.Max(1, t.Header.Height),
					Flat = t.Header.Flags.HasFlag(TextureFlags.Chrome),
					Fullbright = t.Header.Flags.HasFlag(TextureFlags.Fullbright)
				};
				tb.Argb = new int[tb.Width * tb.Height];

				var masked = t.Header.Flags.HasFlag(TextureFlags.Masked);
				if (t.Data != null && t.Palette != null && t.Palette.Length >= 768)
				{
					var n = Math.Min(tb.Argb.Length, t.Data.Length);
					for (var i = 0; i < n; i++)
					{
						var idx = t.Data[i];
						if (masked && idx == 255)
						{
							tb.Argb[i] = 0; // transparent
							continue;
						}
						var k = idx * 3;
						tb.Argb[i] = unchecked((int) 0xFF000000) | (t.Palette[k] << 16) | (t.Palette[k + 1] << 8) | t.Palette[k + 2];
					}
				}
				else
				{
					tb.Flat = true;
				}

				list.Add(tb);
			}

			return list;
		}

		private static List<Tri> CollectTriangles(MdlFile mdl, Matrix4x4[] transforms, int skin)
		{
			var tris = new List<Tri>();
			if (mdl.BodyParts == null) return tris;

			short[] skinTextures = null;
			if (mdl.Skins != null && mdl.Skins.Count > 0)
			{
				skinTextures = mdl.Skins[Math.Max(0, Math.Min(skin, mdl.Skins.Count - 1))].Textures;
			}

			foreach (var part in mdl.BodyParts)
			{
				if (part.Models == null || part.Models.Length == 0) continue;
				var model = part.Models[0];
				if (model.Meshes == null) continue;

				foreach (var mesh in model.Meshes)
				{
					var verts = mesh.Vertices;
					if (verts == null) continue;

					var texIndex = -1;
					var skinRef = mesh.Header.SkinRef;
					if (skinTextures != null && skinRef >= 0 && skinRef < skinTextures.Length) texIndex = skinTextures[skinRef];
					else if (mdl.Textures != null && skinRef >= 0 && skinRef < mdl.Textures.Count) texIndex = skinRef;

					for (var i = 0; i + 2 < verts.Length; i += 3)
					{
						var v0 = verts[i];
						var v1 = verts[i + 1];
						var v2 = verts[i + 2];
						tris.Add(new Tri
						{
							P0 = Transform(v0, transforms),
							P1 = Transform(v1, transforms),
							P2 = Transform(v2, transforms),
							T0 = Uv(v0),
							T1 = Uv(v1),
							T2 = Uv(v2),
							Texture = texIndex
						});
					}
				}
			}

			return tris;
		}

		private static Vector3 Transform(MeshVertex v, Matrix4x4[] transforms)
		{
			var bone = v.VertexBone;
			return bone >= 0 && bone < transforms.Length ? Vector3.Transform(v.Vertex, transforms[bone]) : v.Vertex;
		}

		private static Vector2 Uv(MeshVertex v)
		{
			var t = v.Texture;
			return new Vector2(float.IsNaN(t.X) ? 0 : t.X, float.IsNaN(t.Y) ? 0 : t.Y);
		}
	}
}
