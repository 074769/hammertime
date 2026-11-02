using System;
using System.Collections.Generic;

namespace Sledge.BspEditor.Rendering.Selection
{
    /// <summary>
    /// Maps map object IDs to small, dense slot numbers. Each slot is one bit in the selection set
    /// uploaded to the renderer, so selecting an object only changes that bit and never needs its
    /// geometry rebuilt. Objects keep their slot until they are removed or the table is cleared.
    /// </summary>
    public sealed class SelectionSlotTable
    {
        /// <summary>Maximum number of objects that can have a slot. Objects beyond this keep the baked selection.</summary>
        public const int Capacity = 16384;

        /// <summary>Number of 32-bit words in a selection set.</summary>
        public const int WordCount = Capacity / 32;

        private readonly Dictionary<long, int> _slots = new Dictionary<long, int>();

        // The live selection, one bit per slot. Published to the renderer as a copy (see Snapshot).
        private readonly uint[] _selected = new uint[WordCount];

        // Objects that still bake their selection into their geometry, so selecting them needs a rebuild.
        private readonly HashSet<long> _bakesSelection = new HashSet<long>();
        private readonly Stack<int> _free = new Stack<int>();
        private int _next;

        /// <summary>Number of objects that currently have a slot.</summary>
        public int Count => _slots.Count;

        /// <summary>
        /// Returns the slot for an object, assigning one the first time it is seen.
        /// Returns -1 when the table is full.
        /// </summary>
        public int GetOrAssign(long id)
        {
            if (_slots.TryGetValue(id, out var slot)) return slot;

            if (_free.Count > 0) slot = _free.Pop();
            else if (_next < Capacity) slot = _next++;
            else return -1;

            _slots[id] = slot;
            return slot;
        }

        /// <summary>Returns the slot for an object if it has one.</summary>
        public bool TryGet(long id, out int slot)
        {
            return _slots.TryGetValue(id, out slot);
        }

        /// <summary>Frees the slot of a removed object so it can be reused.</summary>
        public void Remove(long id)
        {
            _bakesSelection.Remove(id);
            if (!_slots.TryGetValue(id, out var slot)) return;
            _selected[slot >> 5] &= ~(1u << (slot & 31));
            _slots.Remove(id);
            _free.Push(slot);
        }

        /// <summary>Forgets every slot. Only safe when every object is about to be converted again.</summary>
        public void Clear()
        {
            _slots.Clear();
            _free.Clear();
            _bakesSelection.Clear();
            Array.Clear(_selected, 0, _selected.Length);
            _next = 0;
        }

        /// <summary>Marks an object as baking its selection into its geometry, so selecting it must rebuild it.</summary>
        public void MarkBakesSelection(long id)
        {
            _bakesSelection.Add(id);
        }

        /// <summary>True when selecting or deselecting the object can be done without rebuilding it.</summary>
        public bool CanSwitchLook(long id)
        {
            return _slots.ContainsKey(id) && !_bakesSelection.Contains(id);
        }

        /// <summary>Sets or clears the selected bit of an object. Objects without a slot are ignored.</summary>
        public void SetSelected(long id, bool selected)
        {
            if (!_slots.TryGetValue(id, out var slot)) return;
            if (selected) _selected[slot >> 5] |= 1u << (slot & 31);
            else _selected[slot >> 5] &= ~(1u << (slot & 31));
        }

        /// <summary>Replaces the whole selection with exactly these objects.</summary>
        public void ResetSelection(IEnumerable<long> selectedIds)
        {
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            Array.Clear(_selected, 0, _selected.Length);
            foreach (var id in selectedIds) SetSelected(id, true);
        }

        /// <summary>A copy of the current selection bits, safe to hand to the renderer.</summary>
        public uint[] Snapshot()
        {
            return (uint[]) _selected.Clone();
        }

        /// <summary>
        /// Builds the selection set: bit N is set when the object in slot N is selected.
        /// Selected IDs without a slot are ignored.
        /// </summary>
        public uint[] BuildBits(IEnumerable<long> selectedIds)
        {
            if (selectedIds == null) throw new ArgumentNullException(nameof(selectedIds));

            var bits = new uint[WordCount];
            foreach (var id in selectedIds)
            {
                if (!_slots.TryGetValue(id, out var slot)) continue;
                bits[slot >> 5] |= 1u << (slot & 31);
            }
            return bits;
        }
    }
}
