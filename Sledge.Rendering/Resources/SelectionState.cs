using System.Threading;

namespace Sledge.Rendering.Resources
{
    /// <summary>
    /// The set of selected object slots, one bit per slot. The array is replaced as a whole and never
    /// modified in place, so the render thread can read it while the editor publishes a new one.
    /// </summary>
    public sealed class SelectionState
    {
        private uint[] _bits = new uint[0];

        /// <summary>Bit N is set when the object in slot N is selected.</summary>
        public uint[] Bits
        {
            get => Volatile.Read(ref _bits);
            set => Volatile.Write(ref _bits, value ?? new uint[0]);
        }

        /// <summary>True when the slot's bit is set. Negative slots and slots past the end are not selected.</summary>
        public static bool IsSelected(uint[] bits, int slot)
        {
            if (bits == null || slot < 0) return false;
            var word = slot >> 5;
            if (word >= bits.Length) return false;
            return (bits[word] & (1u << (slot & 31))) != 0;
        }

        /// <summary>
        /// True when a draw with this filter should be drawn. Draws with no slot, or no filter, are always drawn.
        /// </summary>
        public static bool IsVisible(SelectionFilter filter, int slot, uint[] bits)
        {
            if (filter == SelectionFilter.Always || slot < 0) return true;
            var selected = IsSelected(bits, slot);
            return filter == SelectionFilter.WhenSelected ? selected : !selected;
        }
    }
}
