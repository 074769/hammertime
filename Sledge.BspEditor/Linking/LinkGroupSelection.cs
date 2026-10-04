using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Linking
{
    /// <summary>
    /// Remembers that a whole instance of a link was selected as a group (by clicking its label), so that moving, rotating
    /// or flipping that selection is known to be moving the group, even if the instance only has one object in it.
    /// The memory lasts until the selection stops being exactly that instance.
    /// </summary>
    public static class LinkGroupSelection
    {
        private class Holder
        {
            public long Top;
            public long Instance;
            public HashSet<IMapObject> Objects;
        }

        private static readonly ConditionalWeakTable<MapDocument, Holder> Store = new ConditionalWeakTable<MapDocument, Holder>();

        public static void Set(MapDocument document, long top, long instance, IEnumerable<IMapObject> objects)
        {
            Clear(document);
            Store.Add(document, new Holder { Top = top, Instance = instance, Objects = new HashSet<IMapObject>(objects) });
        }

        public static void Clear(MapDocument document)
        {
            Store.Remove(document);
        }

        /// <summary>True if the visible objects given (all of one instance) are what was selected as a group, and still are selected.</summary>
        public static bool Covers(MapDocument document, long top, long instance, IList<IMapObject> visible)
        {
            if (visible.Count == 0 || !Store.TryGetValue(document, out var h)) return false;
            if (h.Top != top || h.Instance != instance) return false;
            return visible.All(x => h.Objects.Contains(x) && x.IsSelected);
        }

        private static bool InHolder(Holder h, IMapObject o)
        {
            for (var p = o; p != null; p = p.Hierarchy.Parent)
            {
                if (h.Objects.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>Forget the group selection once anything else is selected, or part of it is deselected.</summary>
        public static void Update(Change change)
        {
            if (!Store.TryGetValue(change.Document, out var h)) return;

            foreach (var o in change.Added.Concat(change.Updated))
            {
                if (o.IsSelected && !InHolder(h, o)) { Clear(change.Document); return; }
                if (!o.IsSelected && h.Objects.Contains(o)) { Clear(change.Document); return; }
            }
        }
    }
}
