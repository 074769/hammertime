using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// Shared rules for storage: which objects can be stored, and how visible storage is kept out of the saved map.
    /// </summary>
    public static class StorageRules
    {
        public static IEnumerable<StorageGroup> Groups(MapDocument document)
        {
            return document.Map.Data.Get<StorageGroup>();
        }

        public static StorageGroup GetGroup(MapDocument document, long id)
        {
            return Groups(document).FirstOrDefault(x => x.ID == id);
        }

        public static bool IsStored(IMapObject obj)
        {
            return obj.Data.GetOne<StorageID>() != null;
        }

        /// <summary>True if the object, or anything inside it, is part of a link. Linked objects can't be stored.</summary>
        public static bool IsLinked(IMapObject obj)
        {
            return LinkedObjects.GetLinkId(obj) != null || obj.FindAll().Any(x => LinkedObjects.GetLinkId(x) != null);
        }

        public static bool SelectionHasStored(MapDocument document)
        {
            return document.Selection.GetSelectedParents().Any(IsStored);
        }

        /// <summary>
        /// Takes the objects of visible groups out of the map tree so that a save writes none of them.
        /// Returns what is needed to put them back with <see cref="Reattach"/>.
        /// </summary>
        public static List<Tuple<IMapObject, IMapObject>> DetachVisible(MapDocument document)
        {
            var detached = new List<Tuple<IMapObject, IMapObject>>();
            foreach (var group in Groups(document).Where(x => x.Visible))
            {
                foreach (var obj in group.Objects)
                {
                    var parent = obj.Hierarchy.Parent;
                    if (parent == null) continue;
                    detached.Add(Tuple.Create(obj, parent));
                    obj.Hierarchy.Parent = null;
                }
            }
            return detached;
        }

        public static void Reattach(List<Tuple<IMapObject, IMapObject>> detached)
        {
            foreach (var d in detached) d.Item1.Hierarchy.Parent = d.Item2;
        }
    }
}
