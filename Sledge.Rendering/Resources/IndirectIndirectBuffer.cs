using System;
using System.Runtime.CompilerServices;
using Sledge.Rendering.Engine;
using Veldrid;

namespace Sledge.Rendering.Resources
{
    /// <summary>
    /// A wrapper around an indirect buffer which works when running DirectX10 by falling back to a loop over an array
    /// </summary>
    public class IndirectIndirectBuffer : IDisposable
    {
        public static bool UseFallback => !Features.IndirectBuffers;
        private static readonly int ArgSize = Unsafe.SizeOf<IndirectDrawIndexedArguments>();

        private readonly GraphicsDevice _device;

        private readonly DeviceBuffer _deviceBuffer;
        private readonly IndirectDrawIndexedArguments[] _arguments;

        // A CPU copy of every draw's arguments, so a single draw can be changed without reading the
        // GPU buffer back. In fallback mode the argument array is itself that copy.
        private readonly IndirectDrawIndexedArguments[] _shadow;
        private int _dirtyMin = int.MaxValue;
        private int _dirtyMax = -1;

        public IndirectIndirectBuffer(GraphicsDevice device, int sizeInBytes)
        {
            _device = device;

            if (UseFallback)
            {
                _arguments = new IndirectDrawIndexedArguments[sizeInBytes / ArgSize];
                _shadow = _arguments;
            }
            else
            {
                _deviceBuffer = _device.ResourceFactory.CreateBuffer(new BufferDescription((uint) sizeInBytes, BufferUsage.IndirectBuffer));
                _shadow = new IndirectDrawIndexedArguments[sizeInBytes / ArgSize];
            }
        }

        public void Update(long offset, IndirectDrawIndexedArguments arguments)
        {
            if (UseFallback)
            {
                _arguments[offset / ArgSize] = arguments;
            }
            else
            {
                _shadow[offset / ArgSize] = arguments;
                _device.UpdateBuffer(_deviceBuffer, (uint) offset, arguments);
            }
        }

        public void Update(long offset, IndirectDrawIndexedArguments[] arguments)
        {
            if (UseFallback)
            {
                Array.Copy(arguments, 0, _arguments, offset / ArgSize, arguments.LongLength);
            }
            else
            {
                Array.Copy(arguments, 0, _shadow, offset / ArgSize, arguments.LongLength);
                _device.UpdateBuffer(_deviceBuffer, (uint) offset, arguments);
            }
        }

        /// <summary>
        /// Changes the index count of one draw. A count of zero makes the draw do nothing.
        /// The change reaches the GPU when <see cref="FlushUpdates"/> is called.
        /// </summary>
        public void SetIndexCount(int drawIndex, uint count)
        {
            if (drawIndex < 0 || drawIndex >= _shadow.Length) return;
            if (_shadow[drawIndex].IndexCount == count) return;

            _shadow[drawIndex].IndexCount = count;

            if (UseFallback) return;
            if (drawIndex < _dirtyMin) _dirtyMin = drawIndex;
            if (drawIndex > _dirtyMax) _dirtyMax = drawIndex;
        }

        /// <summary>
        /// Uploads the draws changed by <see cref="SetIndexCount"/> in one write covering the changed range.
        /// </summary>
        public unsafe void FlushUpdates()
        {
            if (UseFallback || _dirtyMax < 0) return;

            var first = _dirtyMin;
            var last = _dirtyMax;
            _dirtyMin = int.MaxValue;
            _dirtyMax = -1;

            fixed (IndirectDrawIndexedArguments* source = &_shadow[first])
            {
                _device.UpdateBuffer(_deviceBuffer, (uint) (first * ArgSize), (IntPtr) source, (uint) ((last - first + 1) * ArgSize));
            }
        }

        public void DrawIndexed(CommandList commandList, uint offset, uint count, uint stride)
        {
            if (UseFallback)
            {
                var offs = offset / ArgSize;
                for (var i = 0; i < count; i++)
                {
                    var arg = _arguments[i + offs];
                    commandList.DrawIndexed(arg.IndexCount, arg.InstanceCount, arg.FirstIndex, arg.VertexOffset, arg.FirstInstance);
                }
            }
            else
            {
                commandList.DrawIndexedIndirect(_deviceBuffer, offset, count, stride);
            }
        }

        public void Dispose()
        {
            _deviceBuffer?.Dispose();
        }
    }
}