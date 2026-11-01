using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Veldrid;

namespace Sledge.Rendering.Resources
{
    public class BufferBuilder : IDisposable
    {
        private static readonly Dictionary<BufferSize, uint> Sizes = new Dictionary<BufferSize, uint>
        {
            { BufferSize.Large, 1 * 1024 * 1024 }, // 1MB index / 4MB vert
            { BufferSize.Medium, 128 * 1024 }, // 128KB index / 1MB vert
            { BufferSize.Small, 32 * 1024 }, // 32KB index / 128KB vert
        };

        private readonly GraphicsDevice _device;

        private bool _inBuffer;

        private DeviceBuffer _currentVertexBuffer;
        private MappedResource _currentVertexMap;
        private uint _currentVertexOffset;

        private DeviceBuffer _currentIndexBuffer;
        private MappedResource _currentIndexMap;
        private uint _currentIndexOffset;

        private List<IndirectArgument> _currentIndirectArguments;

        public List<DeviceBuffer> VertexBuffers { get; }
        public List<DeviceBuffer> IndexBuffers { get; }
        public List<IndirectIndirectBuffer> IndirectBuffers { get; }
        public List<List<BufferGroup>> IndirectBufferGroups { get; }

        // Selection-dependent draws, one entry per indirect buffer (null when a buffer has none).
        private readonly List<SelectionDrawTable> _selectionTables = new List<SelectionDrawTable>();
        private readonly SelectionState _selection;
        private readonly object _selectionLock = new object();
        private bool _disposed;

        /// <summary>
        /// Debug information about buffer allocations.
        /// This will only contain data in debug mode.
        /// </summary>
        public List<BufferAllocation> AllocationInformation { get; }

        public int NumBuffers => VertexBuffers.Count;

        private readonly uint _vertexBufferSize;
        private readonly uint _indexBufferSize;

        internal BufferBuilder(GraphicsDevice device, BufferSize size, SelectionState selection = null)
        {
            _device = device;
            _selection = selection;
            VertexBuffers = new List<DeviceBuffer>();
            IndexBuffers = new List<DeviceBuffer>();
            IndirectBuffers = new List<IndirectIndirectBuffer>();
            IndirectBufferGroups = new List<List<BufferGroup>>();

            // Vertex buffer being about 4 times the size of the index buffer is about right.
            // A vertex is ~16 times the size of an index, but there's ~4 indexes per vertex so it evens out.
            _indexBufferSize = Sizes[size];
            _vertexBufferSize = _indexBufferSize * 4;

#if DEBUG
            AllocationInformation = new List<BufferAllocation>();
#endif
        }

        private void AllocateBuffer(uint vsize, uint isize)
        {
            if (_inBuffer)
            {
                if (_currentVertexOffset + vsize > _currentVertexBuffer.SizeInBytes ||
                    _currentIndexOffset + isize > _currentIndexBuffer.SizeInBytes)
                {
                    CommitCurrentBuffers();
                }
            }

            if (_inBuffer) return;

            // Cater for allocations that are larger than the buffer size
            var vSize = Math.Max(_vertexBufferSize, vsize);
            var iSize = Math.Max(_indexBufferSize, isize);

            _currentVertexBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(vSize, BufferUsage.Dynamic | BufferUsage.VertexBuffer));
            _currentIndexBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(iSize, BufferUsage.Dynamic | BufferUsage.IndexBuffer));
            _currentVertexMap = _device.Map(_currentVertexBuffer, MapMode.Write);
            _currentIndexMap = _device.Map(_currentIndexBuffer, MapMode.Write);
            _currentIndirectArguments = new List<IndirectArgument>();
            _inBuffer = true;
        }

        private void CommitCurrentBuffers()
        {
            if (!_inBuffer) return;

            _device.Unmap(_currentVertexBuffer);
            _device.Unmap(_currentIndexBuffer);

            var numInd = _currentIndirectArguments.Count;
            var indSize = Unsafe.SizeOf<IndirectDrawIndexedArguments>();

            if (numInd == 0)
            {
                // No indirect arguments, this buffer is empty.
                _currentVertexBuffer.Dispose();
                _currentIndexBuffer.Dispose();

                _currentVertexMap = _currentIndexMap = default(MappedResource);
                _currentIndexBuffer = _currentVertexBuffer = null;
                _currentVertexOffset = _currentIndexOffset = 0;
                _currentIndirectArguments = null;
                _inBuffer = false;
                return;
            }

            var bufferGroups = new List<BufferGroup>();
            var indirectBuffer = new IndirectIndirectBuffer(_device, numInd * indSize);
            SelectionDrawTable selectionTable = null;
            var selectionBits = _selection?.Bits;
            uint indirOffset = 0;
            foreach (var g in _currentIndirectArguments.GroupBy(x => new { x.Pipeline, x.Camera, x.HasTransparency, x.Binding }))
            {
                if (g.Key.HasTransparency)
                {
                    foreach (var ia in g)
                    {
                        var args = ia.Arguments;
                        args.IndexCount = StartingIndexCount(ref selectionTable, ia, (int) indirOffset, selectionBits);
                        indirectBuffer.Update(indirOffset * indSize, args);
                        var bg = new BufferGroup(ia.Pipeline, ia.Camera, ia.Location, ia.Binding, indirOffset, 1);
                        bufferGroups.Add(bg);
                        indirOffset += 1;
                    }
                }
                else
                {
                    var items = g.ToList();
                    var indir = new IndirectDrawIndexedArguments[items.Count];
                    for (var j = 0; j < items.Count; j++)
                    {
                        indir[j] = items[j].Arguments;
                        indir[j].IndexCount = StartingIndexCount(ref selectionTable, items[j], (int) indirOffset + j, selectionBits);
                    }
                    var indirCount = (uint) indir.Length;

                    indirectBuffer.Update(indirOffset * indSize, indir);

                    var bg = new BufferGroup(g.Key.Pipeline, g.Key.Camera, g.Key.Binding, indirOffset, indirCount);
                    bufferGroups.Add(bg);

                    indirOffset += indirCount;
                }
            }
            
            VertexBuffers.Add(_currentVertexBuffer);
            IndexBuffers.Add(_currentIndexBuffer);
            IndirectBuffers.Add(indirectBuffer);
            IndirectBufferGroups.Add(bufferGroups);
            _selectionTables.Add(selectionTable);

#if DEBUG
            AllocationInformation.Add(new BufferAllocation(_currentVertexBuffer.SizeInBytes, _currentVertexOffset, _currentIndexBuffer.SizeInBytes, _currentIndexOffset));
#endif

            _currentVertexMap = _currentIndexMap = default(MappedResource);
            _currentIndexBuffer = _currentVertexBuffer = null;
            _currentVertexOffset = _currentIndexOffset = 0;
            _currentIndirectArguments = null;
            _inBuffer = false;
        }

        public void Append<T>(IEnumerable<T> vertices, IEnumerable<uint> indices, IEnumerable<BufferGroup> groups) where T : struct
        {
            var verts = vertices.ToArray();
            var index = indices.ToArray();

            var structSize = Unsafe.SizeOf<T>();

            var vsize = (uint)(verts.Length * structSize);
            var isize = (uint)index.Length * sizeof(uint);

            AllocateBuffer(vsize, isize);

            CopyToBuffer(_currentVertexMap, _currentVertexOffset, verts, vsize);
            CopyToBuffer(_currentIndexMap, _currentIndexOffset, index, isize);

            foreach (var bg in groups)
            {
                _currentIndirectArguments.Add(new IndirectArgument(
                    bg.Pipeline, bg.Camera, bg.HasTransparency, bg.Location, bg.Binding, bg.Slot, bg.Filter,
                    new IndirectDrawIndexedArguments
                    {
                        IndexCount = bg.Count,
                        InstanceCount = 1,
                        FirstIndex = (_currentIndexOffset / 4) + bg.Offset,
                        VertexOffset = (int) (_currentVertexOffset / structSize),
                        FirstInstance = 0
                    }
                ));
            }

            _currentVertexOffset += vsize;
            _currentIndexOffset += isize;
        }

        /// <summary>
        /// The index count a draw starts with. Draws that depend on selection are registered in the table
        /// and start at zero when they are hidden for the current selection.
        /// </summary>
        private static uint StartingIndexCount(ref SelectionDrawTable table, IndirectArgument draw, int drawIndex, uint[] bits)
        {
            var fullCount = draw.Arguments.IndexCount;
            if (draw.Slot < 0 || draw.Filter == SelectionFilter.Always) return fullCount;

            if (table == null) table = new SelectionDrawTable();
            return table.AddDraw(draw.Slot, draw.Filter, drawIndex, fullCount, bits);
        }

        /// <summary>
        /// Shows or hides the selection-dependent draws to match the given selection, without rebuilding any
        /// geometry. Only draws whose state actually changed are rewritten. Call this once the builder is complete.
        /// </summary>
        public void ApplySelection(uint[] bits)
        {
            lock (_selectionLock)
            {
                if (_disposed) return;

                var count = Math.Min(_selectionTables.Count, IndirectBuffers.Count);
                for (var i = 0; i < count; i++)
                {
                    var table = _selectionTables[i];
                    if (table == null) continue;

                    var buffer = IndirectBuffers[i];
                    if (table.Apply(bits, (draw, indexCount) => buffer.SetIndexCount(draw, indexCount)) > 0)
                    {
                        buffer.FlushUpdates();
                    }
                }
            }
        }

        public void Complete()
        {
            CommitCurrentBuffers();
        }

		[SuppressMessage("Microsoft.Security", "CA2118:ReviewSuppressUnmanagedCodeSecurityUsage")]
		[DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, EntryPoint = "RtlMoveMemory", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
		[ResourceExposure(ResourceScope.None)]
		public static extern void CopyMemory(IntPtr destData, IntPtr srcData, uint size);
		private void CopyToBuffer(MappedResource map, uint offset, Array data, uint size)
        {
            var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                var source = handle.AddrOfPinnedObject();
                var destination = IntPtr.Add(map.Data, (int) offset);
                CopyMemory(destination, source, size);
            }
            finally
            {
                if (handle.IsAllocated) handle.Free();
            }
        }

        public void Dispose()
        {
            lock (_selectionLock)
            {
                _disposed = true;
            }
            VertexBuffers.ForEach(x => x.Dispose());
            IndexBuffers.ForEach(x => x.Dispose());
            IndirectBuffers.ForEach(x => x.Dispose());
        }

        private struct IndirectArgument
        {
            public PipelineType Pipeline;
            public CameraType Camera;
            public bool HasTransparency;
            public Vector3 Location;
            public string Binding;
            public int Slot;
            public SelectionFilter Filter;
            public IndirectDrawIndexedArguments Arguments;

            public IndirectArgument(PipelineType pipeline, CameraType camera, bool hasTransparency, Vector3 location, string binding, int slot, SelectionFilter filter, IndirectDrawIndexedArguments arguments)
            {
                Pipeline = pipeline;
                Camera = camera;
                HasTransparency = hasTransparency;
                Location = location;
                Binding = binding;
                Slot = slot;
                Filter = filter;
                Arguments = arguments;
            }
        }

        public struct BufferAllocation
        {
            public readonly long VertexBufferTotalSize;
            public readonly long VertexBufferAllocatedSize;
            public readonly long IndexBufferTotalSize;
            public readonly long IndexBufferAllocatedSize;

            public BufferAllocation(long vertexBufferTotalSize, long vertexBufferAllocatedSize, long indexBufferTotalSize, long indexBufferAllocatedSize)
            {
                VertexBufferTotalSize = vertexBufferTotalSize;
                VertexBufferAllocatedSize = vertexBufferAllocatedSize;
                IndexBufferTotalSize = indexBufferTotalSize;
                IndexBufferAllocatedSize = indexBufferAllocatedSize;
            }
        }
    }
}