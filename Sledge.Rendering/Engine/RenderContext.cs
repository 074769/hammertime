using System.Numerics;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Resources;
using Veldrid;

namespace Sledge.Rendering.Engine
{
    public class RenderContext : IUpdateable
    {
        public ResourceLoader ResourceLoader { get; }
        public GraphicsDevice Device { get; }
        public Matrix4x4 SelectiveTransform { get; set; } = Matrix4x4.Identity;

        /// <summary>The selected object slots, shared with every buffer builder created by the engine.</summary>
        public SelectionState Selection { get; } = new SelectionState();

        public RenderContext(GraphicsDevice device)
        {
            Device = device;
            ResourceLoader = new ResourceLoader(this);
        }

        public void Update(long frame)
        {

        }
    }
}
