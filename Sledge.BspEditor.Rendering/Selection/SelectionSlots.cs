using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;

namespace Sledge.BspEditor.Rendering.Selection
{
    /// <summary>
    /// Keeps one <see cref="SelectionSlotTable"/> per open map document.
    /// Converters run on background threads, so every access takes the same lock.
    /// </summary>
    public static class SelectionSlots
    {
        private static readonly ConditionalWeakTable<MapDocument, SelectionSlotTable> Tables
            = new ConditionalWeakTable<MapDocument, SelectionSlotTable>();

        private static readonly object Lock = new object();

        /// <summary>The slot table for a document, created on first use.</summary>
        public static SelectionSlotTable For(MapDocument document)
        {
            lock (Lock)
            {
                return Tables.GetValue(document, _ => new SelectionSlotTable());
            }
        }

        /// <summary>Builds the selection set for a document from the objects currently marked selected.</summary>
        public static uint[] BuildBits(MapDocument document)
        {
            var selectedIds = document.Map.Root.FindAll().Where(x => x.IsSelected).Select(x => x.ID).ToList();
            lock (Lock)
            {
                return Tables.GetValue(document, _ => new SelectionSlotTable()).BuildBits(selectedIds);
            }
        }
    }
}
