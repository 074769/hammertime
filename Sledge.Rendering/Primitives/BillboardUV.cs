using System.Numerics;
using System.Runtime.InteropServices;

namespace Sledge.Rendering.Primitives
{
    // Layout is deliberately two 16-byte registers so it matches the Billboard.geom.hlsl
    // `UVBuffer` cbuffer exactly - don't reorder/resize fields without updating the shader.
    [StructLayout(LayoutKind.Sequential)]
    public struct BillboardUV
    {
        // Register 0
        public float FrameCount;
        public float CurrentFrame;

        /// <summary>The sprite's SpriteOrientation (Parallel/ParallelUpright/Oriented/
        /// ParallelOriented/FacingUpright), cast to float for the shader.</summary>
        public float Orientation;
        private float _reserved0;

        // Register 1
        /// <summary>Entity "angles" in radians as (pitch, yaw, roll). Only used by
        /// Oriented and ParallelOriented sprites.</summary>
        public Vector3 Angles;
        private float _reserved1;
    }
}
