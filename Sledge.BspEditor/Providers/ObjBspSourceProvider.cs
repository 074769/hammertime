using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Environment;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.Geometric;
using Veldrid.MetalBindings;
using Plane = Sledge.DataStructures.Geometric.Plane;

namespace Sledge.BspEditor.Providers
{
	[Export(typeof(IBspSourceProvider))]
	public class ObjBspSourceProvider : IBspSourceProvider
	{
		private static readonly IEnumerable<Type> SupportedTypes = new List<Type>
		{
            // Sledge only supports solids in the OBJ format
            typeof(Solid),
		};

		public IEnumerable<Type> SupportedDataTypes => SupportedTypes;

		public IEnumerable<FileExtensionInfo> SupportedFileExtensions { get; } = new[]
		{
			new FileExtensionInfo("Wavefront model format", ".obj")
		};

		public bool CanSave => true;

		public Task<BspFileLoadResult> Load(Stream stream, IEnvironment environment)
		{
			return Task.Run(() =>
			{
				using (var reader = new StreamReader(stream, Encoding.ASCII, true, 1024, false))
				{
					var result = new BspFileLoadResult();

					var map = new Map();

					Read(map, reader);

					result.Map = map;
					return result;
				}
			});
		}

		public Task Save(Stream stream, Map map, MapDocument document = null)
		{
			return Save(stream, map, document, new ObjExportOptions());
		}

		public async Task Save(Stream stream, Map map, MapDocument document, ObjExportOptions options)
		{
			options = options ?? new ObjExportOptions();

			var solids = GetSolidsToExport(map, options);

			// Texture sizes are needed to produce the same UVs the viewport shows
			var sizes = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
			if (document?.Environment != null)
			{
				var tc = await document.Environment.GetTextureCollection();
				if (tc != null)
				{
					var names = solids.SelectMany(x => x.Faces).Select(x => x.Texture.Name).Where(x => !String.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase);
					foreach (var name in names)
					{
						var item = await tc.GetTextureItem(name);
						if (item != null && item.Width > 0 && item.Height > 0)
						{
							sizes[name] = (item.Width, item.Height);
						}
					}
				}
			}

			using (var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true))
			{
				Write(solids, sizes, options, writer);
			}
		}

		#region Reading

		private struct ObjFace
		{
			public string Group { get; }
			public List<int> Vertices { get; }

			public ObjFace(string group, IEnumerable<int> vertices) : this()
			{
				Group = group;
				Vertices = vertices.ToList();
			}
		}

		private string CleanLine(string line)
		{
			if (line == null) return null;
			return line.StartsWith("#") ? "" : line.Trim();
		}

		private void Read(Map map, StreamReader reader)
		{
			const NumberStyles ns = NumberStyles.Float;

			var points = new List<Vector3>();
			var faces = new List<ObjFace>();
			var currentGroup = "default";
			var scale = 100f;

			string line;
			while ((line = reader.ReadLine()) != null)
			{
				if (line.StartsWith("# Scale: "))
				{
					var num = line.Substring(9);
					if (float.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
					{
						scale = s;
					}
				}

				line = CleanLine(line);
				SplitLine(line, out var keyword, out var values);
				if (String.IsNullOrWhiteSpace(keyword)) continue;

				var vals = (values ?? "").Split(' ').Where(x => !String.IsNullOrWhiteSpace(x)).ToArray();
				switch (keyword.ToLower())
				{
					// Things I care about
					case "v": // geometric vertices
						var vec = NumericsExtensions.Parse(vals[0], vals[1], vals[2], ns, CultureInfo.InvariantCulture);
						points.Add(vec * scale);
						break;
					case "f": // face
						faces.Add(new ObjFace(currentGroup, vals.Select(x => ParseFaceIndex(points, x))));
						break;
					case "g": // group name
						currentGroup = (values ?? "").Trim();
						break;

					// Things I don't care about
					#region Not Implemented

					// Vertex data
					// "v"
					case "vt": // texture vertices
						break;
					case "vn": // vertex normals
						break;
					case "vp": // parameter space vertices
					case "cstype": // rational or non-rational forms of curve or surface type: basis matrix, Bezier, B-spline, Cardinal, Taylor
					case "degree": // degree
					case "bmat": // basis matrix
					case "step": // step size
								 // not supported
						break;

					// Elements
					// "f"
					case "p": // point
					case "l": // line
					case "curv": // curve
					case "curv2": // 2D curve
					case "surf": // surface
								 // not supported
						break;

					// Free-form curve/surface body statements
					case "parm": // parameter name
					case "trim": // outer trimming loop (trim)
					case "hole": // inner trimming loop (hole)
					case "scrv": // special curve (scrv)
					case "sp":  // special point (sp)
					case "end": // end statement (end)
								// not supported
						break;

					// Connectivity between free-form surfaces
					case "con": // connect
								// not supported
						break;

					// Grouping
					// "g"
					case "s": // smoothing group
						break;
					case "mg": // merging group
						break;
					case "o": // object name
							  // not supported
						break;

					// Display/render attributes
					case "mtllib": // material library
					case "usemtl": // material name
					case "usemap": // texture map name
					case "bevel": // bevel interpolation
					case "c_interp": // color interpolation
					case "d_interp": // dissolve interpolation
					case "lod": // level of detail
					case "shadow_obj": // shadow casting
					case "trace_obj": // ray tracing
					case "ctech": // curve approximation technique
					case "stech": // surface approximation technique
								  // not relevant
						break;

						#endregion
				}
			}

			var solids = new List<Solid>();

			// Try and see if we have a valid solid per-group
			foreach (var g in faces.GroupBy(x => x.Group))
			{
				solids.AddRange(CreateSolids(map, points, g));
			}

			foreach (var solid in solids)
			{
				foreach (var face in solid.Faces)
				{
					face.Texture.AlignToNormal(face.Plane.Normal);
				}

				solid.Hierarchy.Parent = map.Root;
			}

			map.Root.DescendantsChanged();
		}

		private IEnumerable<Solid> CreateSolids(Map map, List<Vector3> points, IEnumerable<ObjFace> objFaces)
		{
			var faces = objFaces.Select(x => CreateFace(map, points, x)).ToList();

			// See if the solid is valid
			var solid = new Solid(map.NumberGenerator.Next("MapObject"));
			solid.Data.Add(new ObjectColor(Colour.GetRandomBrushColour()));
			solid.Data.AddRange(faces);
			if (solid.IsValid())
			{
				// Do an additional check to ensure that all edges are shared
				var edges = solid.Faces.SelectMany(x => x.GetEdges()).ToList();
				if (edges.All(x => edges.Count(y => x.EquivalentTo(y)) == 2))
				{
					// Valid! let's get out of here!
					yield return solid;
					yield break;
				}
			}

			// Not a valid solid, decompose into tetrahedrons/etc
			foreach (var face in faces)
			{
				var polygon = face.ToPolygon();
				if (!polygon.IsValid() || !polygon.IsConvex())
				{
					// tetrahedrons
					foreach (var triangle in GetTriangles(face))
					{
						var tf = new Face(map.NumberGenerator.Next("Face"))
						{
							Plane = new Plane(triangle[0], triangle[1], triangle[2])
						};
						tf.Vertices.AddRange(triangle);
						yield return SolidifyFace(map, tf);
					}
				}
				else
				{
					// cone/pyramid/whatever
					yield return SolidifyFace(map, face);
				}
			}
		}

		private IEnumerable<Vector3[]> GetTriangles(Face face)
		{
			for (var i = 1; i < face.Vertices.Count - 1; i++)
			{
				yield return new[]
				{
					face.Vertices[0],
					face.Vertices[i],
					face.Vertices[i + 1]
				};
			}
		}

		private Solid SolidifyFace(Map map, Face face)
		{
			var solid = new Solid(map.NumberGenerator.Next("MapObject"));
			solid.Data.Add(new ObjectColor(Colour.GetRandomBrushColour()));
			solid.Data.Add(face);

			var center = face.Vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / face.Vertices.Count;
			var offset = center - face.Plane.Normal * 5;
			for (var i = 0; i < face.Vertices.Count; i++)
			{
				var v1 = face.Vertices[i];
				var v2 = face.Vertices[(i + 1) % face.Vertices.Count];

				var f = new Face(map.NumberGenerator.Next("Face"))
				{
					Plane = new Plane(v1, offset, v2)
				};

				f.Vertices.Add(offset);
				f.Vertices.Add(v2);
				f.Vertices.Add(v1);

				solid.Data.Add(f);
			}

			solid.DescendantsChanged();
			return solid;
		}


		private Face CreateFace(Map map, List<Vector3> points, ObjFace objFace)
		{
			var verts = objFace.Vertices.Select(x => points[x]).ToList();

			var f = new Face(map.NumberGenerator.Next("Face"))
			{
				Plane = new Plane(verts[2], verts[1], verts[0])
			};

			verts.Reverse();
			f.Vertices.AddRange(verts);

			return f;
		}

		private int ParseFaceIndex(List<Vector3> list, string index)
		{
			if (index.Contains('/')) index = index.Substring(0, index.IndexOf('/'));
			var idx = int.Parse(index);

			if (idx < 0)
			{
				idx = list.Count + idx;
			}
			else
			{
				idx -= 1;
			}
			//
			return idx;
		}

		private static void SplitLine(string line, out string keyword, out string arguments)
		{
			var idx = line.IndexOf(' ');
			if (idx < 0)
			{
				keyword = line;
				arguments = null;
				return;
			}

			keyword = line.Substring(0, idx);
			arguments = line.Substring(idx + 1);
		}

		#endregion

		#region Writing

		// Used when a texture's size can't be found (missing texture); keeps UVs sensible instead of collapsing to 0,0
		private const int FallbackTextureSize = 64;

		private static List<Solid> GetSolidsToExport(Map map, ObjExportOptions options)
		{
			var solids = map.Root.Find(x => x is Solid).OfType<Solid>();
			if (options.SelectedOnly)
			{
				// A solid counts as selected if it, or something it's inside of (entity/group), is selected
				solids = solids.Where(IsSelectedOrInsideSelected);
			}
			return solids.ToList();
		}

		private static bool IsSelectedOrInsideSelected(IMapObject obj)
		{
			for (var o = obj; o != null; o = o.Hierarchy.Parent)
			{
				if (o.IsSelected) return true;
			}
			return false;
		}

		/// <summary>
		/// Convert a point from editor space (Z-up) to the target application's axes.
		/// Both conversions are proper rotations (no mirroring), so face winding is unaffected.
		/// </summary>
		private static Vector3 ToExportSpace(Vector3 v, ObjAxisMode axis)
		{
			switch (axis)
			{
				case ObjAxisMode.Blender:
					// Z-up -> Y-up, -Z forward: (x, y, z) -> (x, z, -y)
					return new Vector3(v.X, v.Z, -v.Y);
				case ObjAxisMode.Max3ds:
				case ObjAxisMode.None:
				default:
					// 3ds Max is Z-up right-handed like the editor
					return v;
			}
		}

		private static string F(float value, string format)
		{
			return value.ToString(format, CultureInfo.InvariantCulture);
		}

		private static void Write(List<Solid> solids, Dictionary<string, (int Width, int Height)> sizes, ObjExportOptions options, StreamWriter writer)
		{
			writer.WriteLine("# Sledge Object Export");
			writer.WriteLine("# Scale: 1");
			writer.WriteLine("# Axis: " + options.Axis);
			writer.WriteLine();

			// Zero origin: shift so the centre of the exported geometry's bounding box is at 0,0,0.
			// This is applied to positions only; UVs are computed from the original positions below,
			// because texture coordinates depend on where the face is in the world.
			var offset = Vector3.Zero;
			if (options.ZeroOrigin)
			{
				var points = solids.SelectMany(x => x.Faces).SelectMany(x => x.Vertices).ToList();
				if (points.Count > 0)
				{
					var min = points[0];
					var max = points[0];
					foreach (var p in points)
					{
						min = Vector3.Min(min, p);
						max = Vector3.Max(max, p);
					}
					offset = (min + max) / 2f;
				}
			}

			// OBJ indices are 1-based and global to the file
			var vertexIndex = 1;
			var uvIndex = 1;
			var normalIndex = 1;

			foreach (var solid in solids)
			{
				writer.WriteLine("o solid_" + solid.ID);
				// The importer above rebuilds solids from groups, so keep one group per solid
				writer.WriteLine("g solid_" + solid.ID);

				foreach (var face in solid.Faces)
				{
					var count = face.Vertices.Count;
					if (count < 3) continue;

					var name = face.Texture.Name;
					var w = FallbackTextureSize;
					var h = FallbackTextureSize;
					if (!String.IsNullOrEmpty(name) && sizes.TryGetValue(name, out var size))
					{
						w = size.Width;
						h = size.Height;
					}

					// Same texture math the 3D viewport uses (UAxis/VAxis, scale, shift, texture size)
					var coords = face.GetTextureCoordinates(w, h).ToList();

					var material = String.IsNullOrWhiteSpace(name) ? "none" : String.Concat(name.Select(c => Char.IsWhiteSpace(c) ? '_' : c));
					writer.WriteLine("usemtl " + material);

					// The editor stores face vertices clockwise when viewed from outside;
					// OBJ wants counter-clockwise, so write them in reverse order.
					for (var i = count - 1; i >= 0; i--)
					{
						var v = ToExportSpace(face.Vertices[i] - offset, options.Axis);
						writer.WriteLine("v " + F(v.X, "0.0000") + " " + F(v.Y, "0.0000") + " " + F(v.Z, "0.0000"));
					}

					for (var i = count - 1; i >= 0; i--)
					{
						// The viewport's V runs top-down; OBJ's V runs bottom-up, so flip it
						writer.WriteLine("vt " + F(coords[i].Item2, "0.000000") + " " + F(1f - coords[i].Item3, "0.000000"));
					}

					var n = ToExportSpace(face.Plane.Normal, options.Axis);
					writer.WriteLine("vn " + F(n.X, "0.000000") + " " + F(n.Y, "0.000000") + " " + F(n.Z, "0.000000"));

					writer.Write("f");
					for (var i = 0; i < count; i++)
					{
						writer.Write(" " + (vertexIndex + i) + "/" + (uvIndex + i) + "/" + normalIndex);
					}
					writer.WriteLine();
					writer.WriteLine();

					vertexIndex += count;
					uvIndex += count;
					normalIndex++;
				}
			}
		}

		#endregion
	}
}
