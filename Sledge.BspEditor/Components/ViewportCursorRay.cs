using System.Numerics;

namespace Sledge.BspEditor.Components
{
	/// <summary>
	/// A world-space ray through the mouse cursor's position in a viewport.
	/// Viewports publish this whenever the mouse moves so commands (like paste)
	/// can place objects "under the cursor": moving an object onto the ray puts it
	/// directly under the cursor while keeping its original depth in 2D views
	/// and its original distance from the camera in the 3D view.
	/// </summary>
	public class ViewportCursorRay
	{
		/// <summary>
		/// A world-space point on the ray
		/// </summary>
		public Vector3 Origin { get; }

		/// <summary>
		/// The normalised direction of the ray
		/// </summary>
		public Vector3 Direction { get; }

		public ViewportCursorRay(Vector3 origin, Vector3 direction)
		{
			Origin = origin;
			Direction = direction.LengthSquared() < 0.000001f ? Vector3.UnitZ : Vector3.Normalize(direction);
		}

		/// <summary>
		/// Get the point on this ray that is closest to the given point.
		/// Moving an object to this point puts it directly under the cursor
		/// without changing its depth (2D views) or its distance from the camera (3D view).
		/// </summary>
		/// <param name="point">The world-space point the object is currently centred on</param>
		/// <returns>The point on the ray closest to the given point</returns>
		public Vector3 ClosestPoint(Vector3 point)
		{
			var distance = Vector3.Dot(point - Origin, Direction);
			return Origin + Direction * distance;
		}
	}
}