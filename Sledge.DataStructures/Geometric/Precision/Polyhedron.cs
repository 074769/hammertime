using System;
using System.Collections.Generic;
using System.Linq;

namespace Sledge.DataStructures.Geometric.Precision
{
	/// <summary>
	/// Represents a convex polyhedron with at least 4 sides. Uses high-precision value types.
	/// </summary>
	public class Polyhedron
	{
		public IReadOnlyList<Polygon> Polygons { get; }

		public Vector3 Origin => Polygons.Aggregate(Vector3.Zero, (x, y) => x + y.Origin) / Polygons.Count;

		/// <summary>
		/// Creates a polyhedron from a list of polygons which are assumed to be valid.
		/// </summary>
		public Polyhedron(IEnumerable<Polygon> polygons)
		{
			Polygons = polygons.ToList();
		}

		/// <summary>
		/// Creates a polyhedron by intersecting a set of at least 4 planes.
		/// </summary>
		public Polyhedron(IEnumerable<Plane> planes)
		{
			var polygons = new List<Polygon>();

			var list = planes.ToList();
			for (var i = 0; i < list.Count; i++)
			{
				// Split the polygon by all the other planes
				var poly = new Polygon(list[i]);
				for (var j = 0; j < list.Count && poly != null; j++)
				{
					if (i != j && poly.Split(list[j], out var back, out _))
					{
						// Near-coincident planes (e.g. thin faces of a many-sided cylinder) can clip a
						// polygon down to fewer than 3 vertices; that is not a real face, so drop it.
						poly = back != null && back.Vertices.Count >= 3 ? back : null;
					}
				}
				if (poly != null && poly.Vertices.Count >= 3)
					polygons.Add(poly);
			}

			var groupedPolygons = polygons.GroupBy(p => p.Plane).ToList();

			var mergedPolygons = new List<Polygon>();


			foreach (var group in groupedPolygons)
			{
				var polyList = group.ToList();
				while (polyList.Count > 0)
				{
					var basePolygon = polyList[0];
					polyList.RemoveAt(0);
					if (basePolygon.Plane.Normal == basePolygon.Plane.PointOnPlane && basePolygon.Plane.Normal == Vector3.Zero) continue;

					bool merged = true;
					while (merged)
					{
						merged = false;
						for (int i = polyList.Count - 1; i >= 0; i--)
						{
							if (basePolygon.TryMerge(polyList[i], out var mergedPolygon))
							{
								basePolygon = mergedPolygon;
								polyList.RemoveAt(i);
								merged = true;
							}
						}
					}
					mergedPolygons.Add(basePolygon);
				}
			}

			polygons = mergedPolygons;
			var origin = polygons.Aggregate(Vector3.Zero, (x, y) => x + y.Origin) / polygons.Count;
			for (var i = 0; i < polygons.Count; i++)
			{
				var face = polygons[i];
				if (face.Plane.OnPlane(origin) >= 0)
					polygons[i] = new Polygon(face.Vertices.Reverse());
			}

			Polygons = polygons;
		}

		/// <summary>
		/// Splits this polyhedron into two polyhedron by intersecting against a plane.
		/// </summary>
		/// <param name="plane">The splitting plane</param>
		/// <param name="back">The back side of the polyhedron</param>
		/// <param name="front">The front side of the polyhedron</param>
		/// <returns>True if the plane splits the polyhedron, false if the plane doesn't intersect</returns>
		public bool Split(Plane plane, out Polyhedron back, out Polyhedron front)
		{
			back = front = null;

			// Check that this solid actually spans the plane
			var classify = Polygons.Where(x => x.Vertices.Count >= 3).Select(x => x.ClassifyAgainstPlane(plane)).Distinct().ToList();
			if (classify.All(x => x != PlaneClassification.Spanning))
			{
				if (classify.Any(x => x == PlaneClassification.Back)) back = this;
				else if (classify.Any(x => x == PlaneClassification.Front)) front = this;
				return false;
			}

			var backPlanes = new List<Plane> { plane };
			var frontPlanes = new List<Plane> { new Plane(-plane.Normal, -plane.DistanceFromOrigin) };

			// Degenerate faces (fewer than 3 vertices) have no usable plane; ignore them
			foreach (var face in Polygons.Where(x => x.Vertices.Count >= 3))
			{
				var classification = face.ClassifyAgainstPlane(plane);
				if (classification != PlaneClassification.Back) frontPlanes.Add(face.Plane);
				if (classification != PlaneClassification.Front) backPlanes.Add(face.Plane);
			}

			back = new Polyhedron(backPlanes);
			front = new Polyhedron(frontPlanes);

			return true;
		}

		public Geometric.Polyhedron ToStandardPolyhedron()
		{
			return new Geometric.Polyhedron(Polygons.Select(x => x.ToStandardPolygon()));
		}
	}
}
