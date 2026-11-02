using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

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

        private static SelectionSlotTable Table(MapDocument document)
        {
            return Tables.GetValue(document, _ => new SelectionSlotTable());
        }

        /// <summary>The slot table for a document, created on first use.</summary>
        public static SelectionSlotTable For(MapDocument document)
        {
            lock (Lock) return Table(document);
        }

        /// <summary>
        /// The slot an object is converted with, or -1 when the table is full. Objects with a slot are
        /// converted in an unselected and a selected look, so selecting them never needs a rebuild.
        /// Without a slot the converter bakes the selection into the geometry, as it always did.
        /// </summary>
        public static int SlotFor(MapDocument document, IMapObject obj)
        {
            lock (Lock) return Table(document).GetOrAssign(obj.ID);
        }

        /// <summary>
        /// For converters that still bake the selection into their geometry: selecting this object
        /// has to rebuild it.
        /// </summary>
        public static void MarkBakesSelection(MapDocument document, IMapObject obj)
        {
            lock (Lock) Table(document).MarkBakesSelection(obj.ID);
        }

        /// <summary>
        /// True when an object is drawn differently while selected, so it has to be converted with a slot
        /// to be selectable without a rebuild. Groups and the root draw nothing, so they never need one.
        /// </summary>
        public static bool DrawsSelection(IMapObject obj)
        {
            return obj.Data.OfType<Face>().Any() || obj is Solid || (obj is Entity && !obj.Hierarchy.HasChildren);
        }

        /// <summary>
        /// True when every one of these objects can be selected or deselected by switching looks, with
        /// no conversion at all.
        /// </summary>
        public static bool CanSkipRebuild(MapDocument document, IEnumerable<IMapObject> objects)
        {
            lock (Lock)
            {
                var table = Table(document);
                foreach (var obj in objects)
                {
                    if (!DrawsSelection(obj)) continue;
                    if (!table.CanSwitchLook(obj.ID)) return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Records the current selected state of these objects and returns the selection bits to publish.
        /// </summary>
        public static uint[] Update(MapDocument document, IEnumerable<IMapObject> objects)
        {
            lock (Lock)
            {
                var table = Table(document);
                foreach (var obj in objects) table.SetSelected(obj.ID, obj.IsSelected);
                return table.Snapshot();
            }
        }

        /// <summary>
        /// Reads the selection of every object in the map again and returns the selection bits to publish.
        /// Needed after objects were converted, since those may have been selected or deselected meanwhile.
        /// </summary>
        public static uint[] ResetSelection(MapDocument document)
        {
            var selectedIds = document.Map.Root.FindAll().Where(x => x.IsSelected).Select(x => x.ID).ToList();
            lock (Lock)
            {
                var table = Table(document);
                table.ResetSelection(selectedIds);
                return table.Snapshot();
            }
        }

        /// <summary>Frees the slots of objects that are no longer in the map.</summary>
        public static void Release(MapDocument document, IEnumerable<IMapObject> removed)
        {
            lock (Lock)
            {
                var table = Table(document);
                foreach (var obj in removed) table.Remove(obj.ID);
            }
        }

        /// <summary>Forgets every slot. Used when the whole map is about to be converted again.</summary>
        public static void Reset(MapDocument document)
        {
            lock (Lock) Table(document).Clear();
        }
    }
}
