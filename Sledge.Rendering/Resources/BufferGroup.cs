using System;
using System.Numerics;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Renderables;

namespace Sledge.Rendering.Resources
{
    public class BufferGroup : ILocation
    {
        public PipelineType Pipeline { get; }
        public CameraType Camera { get; }
        public bool HasTransparency { get; }
        public Vector3 Location { get; }
        public string Binding { get; }
        public uint Offset { get; }
        public uint Count { get; }

        /// <summary>
        /// The selection slot of the object this group draws, or -1 when the group does not depend on selection.
        /// </summary>
        public int Slot { get; private set; } = -1;

        /// <summary>
        /// Which selection state the group is drawn in. Only meaningful when <see cref="Slot"/> is not -1.
        /// </summary>
        public SelectionFilter Filter { get; private set; }

        /// <summary>
        /// A copy of this group that is only drawn while the object in <paramref name="slot"/> is in the
        /// state described by <paramref name="filter"/>.
        /// </summary>
        public BufferGroup WithSelection(int slot, SelectionFilter filter)
        {
            return new BufferGroup(Pipeline, Camera, HasTransparency, Location, Binding, Offset, Count)
            {
                Slot = slot,
                Filter = filter
            };
        }

        public BufferGroup(PipelineType pipeline, CameraType camera, uint offset, uint count)
            : this(pipeline, camera, false, Vector3.Zero, string.Empty, offset, count)
        {
        }

        public BufferGroup(PipelineType pipeline, CameraType camera, string binding, uint offset, uint count)
            : this(pipeline, camera, false, Vector3.Zero, binding, offset, count)
        {
        }

        public BufferGroup(PipelineType pipeline, CameraType camera, Vector3 location, uint offset, uint count)
            : this(pipeline, camera, location, string.Empty, offset, count)
        {
        }

        public BufferGroup(PipelineType pipeline, CameraType camera, Vector3 location, string binding, uint offset, uint count)
            : this(pipeline, camera, true, location, binding, offset, count)
        {
        }

        public BufferGroup(PipelineType pipeline, CameraType camera, bool hasTransparency, Vector3 location,
            string binding, uint offset, uint count)
        {
            Pipeline = pipeline;
            Camera = camera;
            HasTransparency = hasTransparency;
            Location = location;
            Binding = binding;
            Offset = offset;
            Count = count;
        }
    }
}