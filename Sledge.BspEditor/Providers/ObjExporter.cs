using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Providers
{
	/// <summary>
	/// The single OBJ writer used by both the save dialog (.obj) and File > Export as OBJ...
	/// </summary>
	public static class ObjExporter
	{
		// Used when a texture's size can't be found (missing texture) so UVs don't collapse to 0,0
		private const int FallbackTextureSize = 64;

		public static string GetAxisLabel(ObjAxisPreset axis)
		{
			switch (axis)
			{
				case ObjAxisPreset.Blender: return "Blender (Y-up, -Z forward)";
				case ObjAxisPreset.Max: return "3ds Max (Z-up)";
				default: return "Source (Z-up, as-is)";
			}
		}

		/// <summary>
		/// Look up the pixel size of every texture used by the given solids, so UVs can be
		/// calculated exactly like the 3D viewport does.
		/// </summary>
		public static async Task<Dictionary<string, (int Width, int Height)>> GetTextureSizes(MapDocument document, IEnumerable<Solid> solids)
		{
			var sizes = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
			if (document?.Environment == null) return sizes;

			var tc = await document.Environment.GetTextureCollection();
			if (tc == null) return sizes;

			var names = solids.SelectMany(x => x.Faces)
				.Select(x => x.Texture?.Name)
				.Where(x => !string.IsNullOrEmpty(x))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			foreach (var name in names)
			{
				var item = await tc.GetTextureItem(name);
				if (item != null && item.Width > 0 && item.Height > 0)
				{
					sizes[name] = (item.Width, item.Height);
				}
			}

			return sizes;
		}

		public static void Write(IReadOnlyCollection<Solid> solids, ObjExportOptions options, TextWriter writer, IDictionary<string, (int Width, int Height)> textureSizes = null)
		{
			options = options ?? new ObjExportOptions();

			// "Zero out origin": move the centre of the exported geometry's bounding box to (0,0,0)
			var offset = Vector3.Zero;
			if (options.ZeroOrigin)
			{
				var all = solids.SelectMany(s => s.Faces).SelectMany(f => f.Vertices).ToList();
				if (all.Count > 0)
				{
					var min = new Vector3(all.Min(v => v.X), all.Min(v => v.Y), all.Min(v => v.Z));
					var max = new Vector3(all.Max(v => v.X), all.Max(v => v.Y), all.Max(v => v.Z));
					offset = (min + max) / 2;
				}
			}

			writer.WriteLine("# Hammertime OBJ export");
			writer.WriteLine("# Axis: " + GetAxisLabel(options.Axis));
			writer.WriteLine("# Origin zeroed: " + (options.ZeroOrigin ? "yes" : "no"));
			writer.WriteLine("# Scale: 1");
			writer.WriteLine();

			// OBJ indices are 1-based and absolute; keep running totals of what's been written
			var vCount = 0;
			var vtCount = 0;
			var vnCount = 0;

			foreach (var solid in solids)
			{
				writer.WriteLine("o solid_" + solid.ID.ToString(CultureInfo.InvariantCulture));

				// Weld the vertex positions shared between this solid's faces so the exported faces
				// reference common corners instead of each writing its own copy. Without this the
				// brush comes apart into disconnected planes once imported. UVs and normals stay
				// per-face (brushes are flat-shaded with per-face texture mapping), so only the
				// 'v' positions are deduplicated here. The dedup key is the exact text written for
				// the position, so corners weld at the file's precision (4 dp) and a rounding-bit
				// difference between two faces' copies of the same corner can't split it in two.
				var vertexIndex = new Dictionary<string, int>();

				string lastMaterial = null;

				foreach (var face in solid.Faces)
				{
					var verts = face.Vertices.ToList();
					if (verts.Count < 3) continue;

					var textureName = face.Texture?.Name;
					var material = SanitiseName(textureName);
					if (material != lastMaterial)
					{
						writer.WriteLine("usemtl " + material);
						lastMaterial = material;
					}

					var w = FallbackTextureSize;
					var h = FallbackTextureSize;
					if (textureSizes != null && !string.IsNullOrEmpty(textureName) && textureSizes.TryGetValue(textureName, out var size))
					{
						w = size.Width;
						h = size.Height;
					}

					// Same texture math the 3D viewport uses (UAxis/VAxis, scale, shift, texture size).
					// It works on the original (un-offset) positions so UVs don't slide when the origin is zeroed.
					var coords = face.GetTextureCoordinates(w, h).ToList();

					var normal = face.Plane.Normal;

					// OBJ indices are 1-based, so store index + 1 to write directly into the face.
					var faceIndices = new int[verts.Count];
					for (var i = 0; i < verts.Count; i++)
					{
						var p = ConvertAxis(verts[i] - offset, options.Axis);
						var key = Fmt(p.X) + " " + Fmt(p.Y) + " " + Fmt(p.Z);
						if (!vertexIndex.TryGetValue(key, out var idx))
						{
							writer.WriteLine("v " + key);
							idx = ++vCount;
							vertexIndex[key] = idx;
						}
						faceIndices[i] = idx;
					}

					foreach (var c in coords)
					{
						// The viewport's V runs top-down; OBJ's V runs bottom-up, so flip it
						writer.WriteLine("vt " + FmtUv(c.Item2) + " " + FmtUv(1f - c.Item3));
					}

					var n = ConvertAxis(normal, options.Axis);
					writer.WriteLine("vn " + FmtUv(n.X) + " " + FmtUv(n.Y) + " " + FmtUv(n.Z));

					// Editor faces are wound the opposite way to OBJ, so write the indices in reverse
					writer.Write("f");
					for (var i = verts.Count - 1; i >= 0; i--)
					{
						writer.Write(" " + faceIndices[i].ToString(CultureInfo.InvariantCulture)
						             + "/" + (vtCount + i + 1).ToString(CultureInfo.InvariantCulture)
						             + "/" + (vnCount + 1).ToString(CultureInfo.InvariantCulture));
					}
					writer.WriteLine();

					vtCount += verts.Count;
					vnCount += 1;
				}

				writer.WriteLine();
			}
		}

		/// <summary>
		/// Both conversions are proper rotations (determinant +1), so face winding is unaffected.
		/// </summary>
		private static Vector3 ConvertAxis(Vector3 v, ObjAxisPreset axis)
		{
			switch (axis)
			{
				case ObjAxisPreset.Blender:
					// Z-up -> Y-up, -Z forward: (x, y, z) -> (x, z, -y)
					return new Vector3(v.X, v.Z, -v.Y);
				default:
					// Source and 3ds Max are both Z-up, right-handed
					return v;
			}
		}

		private static string SanitiseName(string name)
		{
			if (string.IsNullOrWhiteSpace(name)) return "none";
			return string.Concat(name.Trim().Select(c => char.IsWhiteSpace(c) ? '_' : c));
		}

		private static string Fmt(float f)
		{
			if (Math.Abs(f) < 0.00005f) f = 0; // avoid "-0.0000"
			return f.ToString("0.0000", CultureInfo.InvariantCulture);
		}

		private static string FmtUv(float f)
		{
			if (Math.Abs(f) < 0.0000005f) f = 0;
			return f.ToString("0.000000", CultureInfo.InvariantCulture);
		}
	}
}
