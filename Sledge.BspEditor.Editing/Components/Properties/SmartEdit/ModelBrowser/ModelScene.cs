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
	public enum ModelShading
	{
		/// <summary>Textured, per-vertex (smooth) lighting.</summary>
		Smooth = 0,

		/// <summary>Textured, per-face (flat) lighting.</summary>
		Flat = 1,

		/// <summary>Textured, no lighting.</summary>
		Unlit = 2,

		/// <summary>Untextured grey, smooth lighting.</summary>
		Solid = 3,

		/// <summary>Triangle edges only.</summary>
		Wireframe = 4,

		/// <summary>Per texture, from the model's own flags: flatshade = flat, fullbright = unlit, otherwise smooth.</summary>
		FromModel = 5
	}

	public class ModelRenderOptions
	{
		public int Width = 256;
		public int Height = 256;
		public Color Background = Color.Black;

		public float Azimuth = 35f;
		public float Elevation = 22f;
		public float Zoom = 1f;
		public PointF Pan;

		public ModelShading Shading = ModelShading.FromModel;

		/// <summary>Fit the projected bounds tightly into the image (thumbnails). Zoom and pan are ignored.</summary>
		public bool FitTight;

		/// <summary>1 = no anti-aliasing, 2 = 2x2 super sampling.</summary>
		public int SuperSample = 1;

		public int Sequence;
		public int Frame;
		public float SubFrame;
		public int Skin;

		/// <summary>Model index to show per bodypart (null = the first model of each).</summary>
		public int[] Body;

		/// <summary>Skip triangles facing away from the camera (GoldSource culls model back faces in game).</summary>
		public bool BackfaceCulling = true;

		/// <summary>Draw textures flagged additive with additive blending (after the opaque faces, no depth write).</summary>
		public bool Additive = true;

		/// <summary>
		/// Depth tolerance in model units. Faces closer together than this are treated as coplanar and the one drawn
		/// last wins (like the engine's LEQUAL depth test), which stops overlapping faces flickering. 0 = strict test.
		/// </summary>
		public float DepthEpsilon = 0.05f;
	}

	/// <summary>
	/// A CPU (z-buffered, textured) renderer for a GoldSource .mdl. Not thread safe: use one instance per thread
	/// (the viewer only ever renders from one background task at a time).
	/// </summary>
	public class ModelScene
	{
		private const int Margin = 6;

		private struct Tri
		{
			public Vector3 P0, P1, P2;
			public Vector3 N0, N1, N2;
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
			public bool Additive;
			public bool Flatshade;
		}

		private class Fit
		{
			public Vector3 Center;
			public float Radius;
		}

		private readonly MdlFile _mdl;
		private List<TextureBitmap> _textures;
		private readonly Dictionary<long, Fit> _fitCache = new Dictionary<long, Fit>();
		private int[] _colour;
		private float[] _depth;
		private float _windingSign; // +1 / -1 once detected, 0 = unknown

		public ModelScene(MdlFile mdl)
		{
			_mdl = mdl;
		}

		public MdlFile Model
		{
			get { return _mdl; }
		}

		private List<TextureBitmap> Textures
		{
			get
			{
				if (_textures == null) _textures = BuildTextures(_mdl);
				return _textures;
			}
		}

		#region Public helpers

		/// <summary>
		/// True if the sequence has usable animation data for GetTransforms.
		/// </summary>
		public static bool IsAnimatable(MdlFile mdl, int sequence)
		{
			try
			{
				if (mdl == null || mdl.Bones == null || mdl.Bones.Count == 0) return false;
				if (mdl.Sequences == null || sequence < 0 || sequence >= mdl.Sequences.Count) return false;
				var seq = mdl.Sequences[sequence];
				if (seq.Header.NumFrames <= 0 || seq.Blends == null || seq.Blends.Length == 0) return false;
				var frames = seq.Blends[0].Frames;
				return frames != null && frames.Length >= seq.Header.NumFrames;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Axis-aligned bounds of the geometry (sequence 0, frame 0, first model of each bodypart), or null if there is none.
		/// </summary>
		public (Vector3 min, Vector3 max, int triangles)? GetBounds()
		{
			var tris = Collect(GetTransforms(0, 0, 0f), 0, null);
			if (tris.Count == 0) return null;
			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);
			foreach (var t in tris)
			{
				min = Vector3.Min(min, Vector3.Min(t.P0, Vector3.Min(t.P1, t.P2)));
				max = Vector3.Max(max, Vector3.Max(t.P0, Vector3.Max(t.P1, t.P2)));
			}
			return (min, max, tris.Count);
		}

		#endregion

		#region Geometry

		private Matrix4x4[] GetTransforms(int sequence, int frame, float subFrame)
		{
			var count = Math.Max(1, _mdl.Bones != null ? _mdl.Bones.Count : 0);
			var transforms = new Matrix4x4[count];
			for (var i = 0; i < count; i++) transforms[i] = Matrix4x4.Identity;

			if (!IsAnimatable(_mdl, sequence)) return transforms;

			try
			{
				_mdl.GetTransforms(sequence, frame, subFrame, ref transforms);
			}
			catch
			{
				// Bad or unsupported animation data: fall back to the bind pose
				transforms = new Matrix4x4[count];
				for (var i = 0; i < count; i++) transforms[i] = Matrix4x4.Identity;
			}

			return transforms;
		}

		private List<Tri> Collect(Matrix4x4[] transforms, int skin, int[] body)
		{
			var tris = new List<Tri>();
			var parts = _mdl.BodyParts;
			if (parts == null) return tris;

			short[] skinTextures = null;
			if (_mdl.Skins != null && _mdl.Skins.Count > 0)
			{
				skinTextures = _mdl.Skins[Math.Max(0, Math.Min(skin, _mdl.Skins.Count - 1))].Textures;
			}

			for (var pi = 0; pi < parts.Count; pi++)
			{
				var part = parts[pi];
				if (part.Models == null || part.Models.Length == 0) continue;

				var mi = body != null && pi < body.Length ? Math.Max(0, Math.Min(body[pi], part.Models.Length - 1)) : 0;
				var model = part.Models[mi];
				if (model.Meshes == null) continue;

				foreach (var mesh in model.Meshes)
				{
					var verts = mesh.Vertices;
					if (verts == null) continue;

					var texIndex = -1;
					var skinRef = mesh.Header.SkinRef;
					if (skinTextures != null && skinRef >= 0 && skinRef < skinTextures.Length) texIndex = skinTextures[skinRef];
					else if (_mdl.Textures != null && skinRef >= 0 && skinRef < _mdl.Textures.Count) texIndex = skinRef;

					for (var i = 0; i + 2 < verts.Length; i += 3)
					{
						var v0 = verts[i];
						var v1 = verts[i + 1];
						var v2 = verts[i + 2];
						tris.Add(new Tri
						{
							P0 = TransformPoint(v0, transforms),
							P1 = TransformPoint(v1, transforms),
							P2 = TransformPoint(v2, transforms),
							N0 = TransformNormal(v0, transforms),
							N1 = TransformNormal(v1, transforms),
							N2 = TransformNormal(v2, transforms),
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

		private static Vector3 TransformPoint(MeshVertex v, Matrix4x4[] transforms)
		{
			var bone = v.VertexBone;
			return bone >= 0 && bone < transforms.Length ? Vector3.Transform(v.Vertex, transforms[bone]) : v.Vertex;
		}

		private static Vector3 TransformNormal(MeshVertex v, Matrix4x4[] transforms)
		{
			var n = v.Normal;
			if (n.LengthSquared() < 1e-12f) return Vector3.Zero;
			var bone = v.NormalBone;
			if (bone >= 0 && bone < transforms.Length) n = Vector3.TransformNormal(n, transforms[bone]);
			var len = n.Length();
			return len > 1e-6f ? n / len : Vector3.Zero;
		}

		private static Vector2 Uv(MeshVertex v)
		{
			var t = v.Texture;
			return new Vector2(float.IsNaN(t.X) ? 0 : t.X, float.IsNaN(t.Y) ? 0 : t.Y);
		}

		private static int BodyKey(int[] body)
		{
			var h = 17;
			if (body != null)
			{
				foreach (var b in body) h = unchecked(h * 31 + b);
			}
			return h;
		}

		/// <summary>
		/// A stable bounding sphere for a sequence + bodygroup combination, sampled over a handful of frames, so the
		/// model doesn't change size while it rotates or animates.
		/// </summary>
		private Fit GetFit(int sequence, int[] body)
		{
			var key = ((long) sequence << 32) | (uint) BodyKey(body);
			Fit fit;
			if (_fitCache.TryGetValue(key, out fit)) return fit;

			var min = new Vector3(float.MaxValue);
			var max = new Vector3(float.MinValue);
			var any = false;

			var animatable = IsAnimatable(_mdl, sequence);
			var total = animatable ? _mdl.Sequences[sequence].Header.NumFrames : 1;
			var samples = Math.Max(1, Math.Min(total, 6));
			for (var i = 0; i < samples; i++)
			{
				var frame = samples <= 1 ? 0 : (int) ((long) i * total / samples);
				foreach (var t in Collect(GetTransforms(sequence, frame, 0f), 0, body))
				{
					min = Vector3.Min(min, Vector3.Min(t.P0, Vector3.Min(t.P1, t.P2)));
					max = Vector3.Max(max, Vector3.Max(t.P0, Vector3.Max(t.P1, t.P2)));
					any = true;
				}
			}

			fit = any
				? new Fit { Center = (min + max) / 2f, Radius = Math.Max((max - min).Length() / 2f, 0.001f) }
				: new Fit { Center = Vector3.Zero, Radius = 1f };
			_fitCache[key] = fit;
			return fit;
		}

		#endregion

		#region Rendering

		/// <summary>
		/// Which triangle winding is the front face. Instead of assuming a convention, compare each triangle's winding
		/// normal with its vertex normals (which point outwards) and take the majority.
		/// </summary>
		private float DetectWinding(List<Tri> tris)
		{
			if (_windingSign != 0f) return _windingSign;

			var votes = 0;
			var evidence = 0;
			foreach (var t in tris)
			{
				var fn = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0);
				var avg = t.N0 + t.N1 + t.N2;
				if (fn.LengthSquared() < 1e-9f || avg.LengthSquared() < 1e-6f) continue;
				votes += Vector3.Dot(fn, avg) > 0f ? 1 : -1;
				evidence++;
			}

			var sign = votes >= 0 ? 1f : -1f;
			if (evidence >= 3) _windingSign = sign; // keep it stable once there is enough evidence
			return sign;
		}

		private static bool IsBackFacing(Tri t, Vector3 dir, float windingSign)
		{
			var fn = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0);
			if (fn.LengthSquared() < 1e-12f) return true; // degenerate
			return Vector3.Dot(fn, dir) * windingSign > 0f;
		}

		/// <summary>
		/// Render the model. Returns null if the model has no geometry.
		/// </summary>
		public Bitmap Render(ModelRenderOptions o)
		{
			var tris = Collect(GetTransforms(o.Sequence, o.Frame, o.SubFrame), o.Skin, o.Body);
			if (tris.Count == 0) return null;

			var wire = o.Shading == ModelShading.Wireframe;
			var ss = wire ? 1 : Math.Max(1, o.SuperSample);
			var pw = Math.Max(1, o.Width) * ss;
			var ph = Math.Max(1, o.Height) * ss;

			// Camera basis (GoldSource: X forward, Y left, Z up). The camera orbits the origin looking at it.
			var az = o.Azimuth * (float) Math.PI / 180f;
			var el = Math.Max(-89f, Math.Min(89f, o.Elevation)) * (float) Math.PI / 180f;
			var camPos = new Vector3((float) (Math.Cos(el) * Math.Cos(az)), (float) (Math.Cos(el) * Math.Sin(az)), (float) Math.Sin(el));
			var dir = -camPos; // camera -> scene
			var right = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitZ));
			var up = Vector3.Cross(right, dir);
			var light = Vector3.Normalize(-dir + up * 0.6f - right * 0.4f);

			float scale, offX, offY;
			if (o.FitTight)
			{
				float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
				foreach (var t in tris)
				{
					Extend(t.P0, right, up, ref minX, ref minY, ref maxX, ref maxY);
					Extend(t.P1, right, up, ref minX, ref minY, ref maxX, ref maxY);
					Extend(t.P2, right, up, ref minX, ref minY, ref maxX, ref maxY);
				}

				var w = Math.Max(maxX - minX, 0.001f);
				var h = Math.Max(maxY - minY, 0.001f);
				var availW = pw - Margin * 2 * ss;
				var availH = ph - Margin * 2 * ss;
				scale = Math.Min(availW / w, availH / h);
				offX = (pw - w * scale) / 2f - minX * scale;
				offY = (ph - h * scale) / 2f - minY * scale;
			}
			else
			{
				var fit = GetFit(o.Sequence, o.Body);
				var avail = Math.Max(8, Math.Min(pw, ph) - Margin * 2 * ss);
				scale = avail / (2f * fit.Radius) * Math.Max(0.01f, o.Zoom);
				offX = pw / 2f + o.Pan.X * ss - Vector3.Dot(fit.Center, right) * scale;
				offY = ph / 2f + o.Pan.Y * ss + Vector3.Dot(fit.Center, up) * scale;
			}

			var windingSign = o.BackfaceCulling ? DetectWinding(tris) : 1f;
			if (wire) return RenderWireframe(tris, o, pw, ph, right, up, scale, offX, offY, dir, windingSign);

			var count = pw * ph;
			if (_colour == null || _colour.Length != count)
			{
				_colour = new int[count];
				_depth = new float[count];
			}
			Array.Fill(_colour, o.Background.ToArgb());
			Array.Fill(_depth, float.MaxValue);

			var textures = Textures;
			var solid = o.Shading == ModelShading.Solid;

			// Pass 0: opaque faces (depth written). Pass 1: additive faces, blended on top without writing depth.
			for (var pass = 0; pass < 2; pass++)
			{
				if (pass == 1 && !o.Additive) break;

				foreach (var t in tris)
				{
					var tex = !solid && t.Texture >= 0 && t.Texture < textures.Count ? textures[t.Texture] : null;
					var additive = o.Additive && tex != null && tex.Additive;
					if (additive != (pass == 1)) continue;

					if (o.BackfaceCulling && IsBackFacing(t, dir, windingSign)) continue;

					// Shading comes from the model's own texture flags unless a mode was forced
					var mode = o.Shading;
					if (mode == ModelShading.FromModel) mode = tex != null && tex.Flatshade ? ModelShading.Flat : ModelShading.Smooth;

					float s0 = 1f, s1 = 1f, s2 = 1f;
					var fullbright = mode == ModelShading.Unlit || (tex != null && tex.Fullbright);
					if (!fullbright)
					{
						var fn = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0);
						var hasFn = fn.LengthSquared() > 1e-9f;
						if (hasFn) fn = Vector3.Normalize(fn);

						if (mode == ModelShading.Flat)
						{
							s0 = s1 = s2 = hasFn ? Lit(fn, light) : 1f;
						}
						else
						{
							s0 = Lit(t.N0, fn, hasFn, light);
							s1 = Lit(t.N1, fn, hasFn, light);
							s2 = Lit(t.N2, fn, hasFn, light);
						}
					}

					var a = Project(t.P0, right, up, scale, offX, offY);
					var b = Project(t.P1, right, up, scale, offX, offY);
					var c = Project(t.P2, right, up, scale, offX, offY);
					var za = Vector3.Dot(t.P0, dir);
					var zb = Vector3.Dot(t.P1, dir);
					var zc = Vector3.Dot(t.P2, dir);

					Rasterise(_colour, _depth, pw, ph, a, b, c, za, zb, zc, t.T0, t.T1, t.T2, tex, s0, s1, s2, additive, Math.Max(0f, o.DepthEpsilon));
				}
			}

			var result = new Bitmap(o.Width, o.Height, PixelFormat.Format32bppArgb);
			if (ss == 1)
			{
				var bd = result.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				try { Marshal.Copy(_colour, 0, bd.Scan0, count); }
				finally { result.UnlockBits(bd); }
			}
			else
			{
				using (var big = new Bitmap(pw, ph, PixelFormat.Format32bppArgb))
				{
					var bd = big.LockBits(new Rectangle(0, 0, pw, ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
					try { Marshal.Copy(_colour, 0, bd.Scan0, count); }
					finally { big.UnlockBits(bd); }

					using (var g = Graphics.FromImage(result))
					{
						g.InterpolationMode = InterpolationMode.HighQualityBicubic;
						g.PixelOffsetMode = PixelOffsetMode.HighQuality;
						g.DrawImage(big, new Rectangle(0, 0, o.Width, o.Height));
					}
				}
			}
			return result;
		}

		private static Bitmap RenderWireframe(List<Tri> tris, ModelRenderOptions o, int pw, int ph, Vector3 right, Vector3 up, float scale, float offX, float offY, Vector3 dir, float windingSign)
		{
			var bmp = new Bitmap(pw, ph, PixelFormat.Format32bppArgb);
			using (var g = Graphics.FromImage(bmp))
			using (var pen = new Pen(Color.FromArgb(210, 150, 205, 255), 1f))
			{
				g.Clear(o.Background);
				g.SmoothingMode = SmoothingMode.AntiAlias;

				var pts = new PointF[4];
				foreach (var t in tris)
				{
					if (o.BackfaceCulling && IsBackFacing(t, dir, windingSign)) continue;

					var a = Project(t.P0, right, up, scale, offX, offY);
					var b = Project(t.P1, right, up, scale, offX, offY);
					var c = Project(t.P2, right, up, scale, offX, offY);
					if (!Sane(a) || !Sane(b) || !Sane(c)) continue; // GDI+ throws on huge coordinates

					pts[0] = new PointF(a.X, a.Y);
					pts[1] = new PointF(b.X, b.Y);
					pts[2] = new PointF(c.X, c.Y);
					pts[3] = pts[0];
					g.DrawLines(pen, pts);
				}
			}
			return bmp;
		}

		private static bool Sane(Vector2 v)
		{
			return Math.Abs(v.X) < 100000f && Math.Abs(v.Y) < 100000f;
		}

		private static Vector2 Project(Vector3 p, Vector3 right, Vector3 up, float scale, float offX, float offY)
		{
			return new Vector2(Vector3.Dot(p, right) * scale + offX, -Vector3.Dot(p, up) * scale + offY);
		}

		private static void Extend(Vector3 p, Vector3 right, Vector3 up, ref float minX, ref float minY, ref float maxX, ref float maxY)
		{
			var x = Vector3.Dot(p, right);
			var y = -Vector3.Dot(p, up);
			if (x < minX) minX = x;
			if (x > maxX) maxX = x;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}

		private static float Lit(Vector3 n, Vector3 light)
		{
			return 0.45f + 0.55f * Math.Abs(Vector3.Dot(n, light)); // two-sided
		}

		private static float Lit(Vector3 n, Vector3 faceNormal, bool hasFaceNormal, Vector3 light)
		{
			if (n.LengthSquared() < 0.5f)
			{
				if (!hasFaceNormal) return 1f;
				n = faceNormal;
			}
			return Lit(n, light);
		}

		private static void Rasterise(int[] colour, float[] depth, int pw, int ph, Vector2 a, Vector2 b, Vector2 c,
			float za, float zb, float zc, Vector2 ta, Vector2 tb, Vector2 tc, TextureBitmap tex, float sa, float sb, float sc, bool additive, float depthEps)
		{
			if (!Sane(a) || !Sane(b) || !Sane(c)) return;

			var minX = Math.Max(0, (int) Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
			var maxX = Math.Min(pw - 1, (int) Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
			var minY = Math.Max(0, (int) Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
			var maxY = Math.Min(ph - 1, (int) Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
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
					var idx = y * pw + x;
					// "Less or equal" with a small tolerance: coplanar / overlapping faces resolve to the one drawn last
					if (z > depth[idx] + depthEps) continue;

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

					var shade = w0 * sa + w1 * sb + w2 * sc;
					var r = (int) (((argb >> 16) & 0xFF) * shade);
					var g = (int) (((argb >> 8) & 0xFF) * shade);
					var bl = (int) ((argb & 0xFF) * shade);

					if (additive)
					{
						var dst = colour[idx];
						r += (dst >> 16) & 0xFF;
						g += (dst >> 8) & 0xFF;
						bl += dst & 0xFF;
						colour[idx] = unchecked((int) 0xFF000000) | (Clamp(r) << 16) | (Clamp(g) << 8) | Clamp(bl);
						continue; // additive faces never write depth
					}

					depth[idx] = Math.Min(z, depth[idx]);
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
					Fullbright = t.Header.Flags.HasFlag(TextureFlags.Fullbright),
					Additive = t.Header.Flags.HasFlag(TextureFlags.Additive),
					Flatshade = t.Header.Flags.HasFlag(TextureFlags.Flatshade)
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

		#endregion
	}
}
