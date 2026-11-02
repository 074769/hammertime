using System;
using System.Collections.Generic;

namespace Sledge.Rendering.Resources
{
    /// <summary>
    /// The draws in one indirect buffer that depend on selection. When the selection changes, only the draws
    /// whose visibility changed are rewritten (index count 0 hides a draw, the full count shows it), so no
    /// geometry has to be converted or uploaded again.
    /// </summary>
    internal sealed class SelectionDrawTable
    {
        private struct Entry
        {
            public int Slot;
            public SelectionFilter Filter;
            public int Draw;
            public uint FullCount;
            public bool Visible;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        public int Count => _entries.Count;

        /// <summary>
        /// How many "when selected" draws are currently visible. When this is zero nothing in the buffer is
        /// selected, so the renderer can skip the selected-state groups entirely.
        /// </summary>
        public int VisibleSelectedCount { get; private set; }

        /// <summary>
        /// Registers a draw and returns the index count it should start with for the given selection:
        /// the full count when it is visible, otherwise zero.
        /// </summary>
        public uint AddDraw(int slot, SelectionFilter filter, int drawIndex, uint fullCount, uint[] bits)
        {
            var visible = SelectionState.IsVisible(filter, slot, bits);
            if (visible && filter == SelectionFilter.WhenSelected) VisibleSelectedCount++;
            _entries.Add(new Entry
            {
                Slot = slot,
                Filter = filter,
                Draw = drawIndex,
                FullCount = fullCount,
                Visible = visible
            });
            return visible ? fullCount : 0u;
        }

        /// <summary>
        /// Re-evaluates every registered draw against the selection and calls <paramref name="setCount"/>
        /// (draw index, new index count) for each one whose visibility changed.
        /// Returns the number of draws that changed.
        /// </summary>
        public int Apply(uint[] bits, Action<int, uint> setCount)
        {
            var changed = 0;
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                var visible = SelectionState.IsVisible(e.Filter, e.Slot, bits);
                if (visible == e.Visible) continue;

                e.Visible = visible;
                _entries[i] = e;
                if (e.Filter == SelectionFilter.WhenSelected) VisibleSelectedCount += visible ? 1 : -1;
                setCount(e.Draw, visible ? e.FullCount : 0u);
                changed++;
            }
            return changed;
        }
    }
}
