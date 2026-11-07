using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Editing.Components.Properties
{
    /// <summary>
    /// Lets the entity tabs of the object properties dialog work on grouped entities.
    /// A group has no entity data of its own, so with a group selected there used to be nothing to edit.
    /// </summary>
    public static class GroupedEntities
    {
        /// <summary>
        /// Replace any selected groups with the entities inside them (nested groups included), as long as
        /// every entity in the result is of the same class. Otherwise the objects are returned untouched.
        /// </summary>
        public static List<IMapObject> Resolve(List<IMapObject> objects)
        {
            if (objects == null || !objects.Any(x => x is Group)) return objects;

            var resolved = new List<IMapObject>();
            foreach (var obj in objects)
            {
                // FindAll includes the group's descendants. A brush entity's solids are skipped, the entity itself is what we want.
                if (obj is Group group) resolved.AddRange(group.FindAll().Where(x => x is Entity));
                else resolved.Add(obj);
            }

            // Only objects that carry entity data can be edited by the entity tabs
            resolved = resolved.Where(x => x.Data.GetOne<EntityData>() != null).Distinct().ToList();

            var classes = resolved
                .Select(x => x.Data.GetOne<EntityData>().Name)
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .Count();

            return classes == 1 ? resolved : objects;
        }
    }
}
