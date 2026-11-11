using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Editing.CompileErrors
{
    /// <summary>
    /// Maps compile-order indices from the log onto editor objects. Entity 0 is the world; entity N
    /// (N >= 1) is the N-th non-world entity in the map, and brush N is the N-th solid of that entity.
    /// </summary>
    public static class CompileErrorsLookup
    {
        private const float FocusPadding = 16f;

        public static IMapObject FindObject(MapDocument doc, CompileErrorsEntry e)
        {
            if (e.Brush.HasValue)
            {
                if (!e.Entity.HasValue) return null;

                IMapObject parent = e.Entity.Value == 0 ? (IMapObject) doc.Map.Root : FindEntity(doc, e);
                if (parent == null) return null;

                var solids = new List<Solid>();
                Collect(solids, parent);
                return e.Brush.Value < solids.Count ? solids[e.Brush.Value] : null;
            }

            return FindEntity(doc, e);
        }

        public static Box FocusBox(CompileErrorsEntry e, IMapObject obj)
        {
            var pad = Vector3.One * FocusPadding;

            if (e.Box != null) return new Box(e.Box.Start - pad, e.Box.End + pad);

            if (e.Points.Count > 0)
            {
                var min = e.Points[0];
                var max = e.Points[0];
                foreach (var p in e.Points)
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                return new Box(min - pad, max + pad);
            }

            return obj?.BoundingBox;
        }

        private static IMapObject FindEntity(MapDocument doc, CompileErrorsEntry e)
        {
            if (e.Entity.HasValue)
            {
                if (e.Entity.Value == 0) return null; // the world has no entity object to select
                var list = Entities(doc);
                var index = e.Entity.Value - 1;
                return index >= 0 && index < list.Count ? list[index] : null;
            }

            if (e.EntityTargetname != null)
            {
                return Entities(doc).FirstOrDefault(x =>
                    Property(x, "targetname") == e.EntityTargetname &&
                    (e.EntityClassname == null || x.EntityData?.Name == e.EntityClassname));
            }

            return null;
        }

        private static List<Entity> Entities(MapDocument doc)
        {
            var list = new List<Entity>();
            Collect(list, doc.Map.Root);
            return list;
        }

        private static string Property(Entity entity, string key)
        {
            if (entity.EntityData == null) return null;
            return entity.EntityData.Properties.TryGetValue(key, out var value) ? value : null;
        }

        private static void Collect<T>(List<T> list, IMapObject parent) where T : IMapObject
        {
            foreach (var o in parent.Hierarchy)
            {
                if (o is T t) list.Add(t);
                else if (o is Group) Collect(list, o);
            }
        }
    }
}
