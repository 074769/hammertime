using System;
using System.Numerics;
using Sledge.Rendering.Renderables;

namespace Sledge.Rendering.Engine
{
    /// <summary>
    /// Skips transparent objects that are entirely outside the view. Only locations that report their reach
    /// (IBoundedLocation) are tested, and the test only removes objects that are certainly off screen.
    /// Set the environment variable HAMMERTIME_NOCULL=1 to turn culling off for comparison.
    /// </summary>
    public static class ViewCulling
    {
        public static readonly bool Enabled = Environment.GetEnvironmentVariable("HAMMERTIME_NOCULL") != "1";

        /// <summary>
        /// The four side planes of the view. Built from the view and projection matrices, which use the row-vector
        /// convention of System.Numerics. Near and far are not tested, so nothing in front of or behind the view
        /// is removed by mistake.
        /// </summary>
        public sealed class Frustum
        {
            private readonly Vector4[] _planes;

            public Frustum(Matrix4x4 viewProjection)
            {
                var m = viewProjection;
                _planes = new[]
                {
                    Normalise(m.M11 + m.M14, m.M21 + m.M24, m.M31 + m.M34, m.M41 + m.M44), // left
                    Normalise(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41), // right
                    Normalise(m.M12 + m.M14, m.M22 + m.M24, m.M32 + m.M34, m.M42 + m.M44), // bottom
                    Normalise(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42), // top
                };
            }

            /// <summary>
            /// True only when the location's whole reach lies beyond one of the side planes.
            /// Locations without a reach are never reported as outside.
            /// </summary>
            public bool IsOutside(ILocation location)
            {
                if (!(location is IBoundedLocation bounded)) return false;

                var p = location.Location;
                foreach (var plane in _planes)
                {
                    var distance = plane.X * p.X + plane.Y * p.Y + plane.Z * p.Z + plane.W;
                    if (distance < -bounded.Radius) return true;
                }
                return false;
            }

            private static Vector4 Normalise(float a, float b, float c, float d)
            {
                var len = (float) Math.Sqrt(a * a + b * b + c * c);
                // A degenerate plane must never remove anything
                if (!(len > 1e-12f)) return new Vector4(0, 0, 0, 1);
                return new Vector4(a / len, b / len, c / len, d / len);
            }
        }
    }
}
