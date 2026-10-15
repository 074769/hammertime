using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Commands.Clipboard
{
    public static class GroupCleanup
    {
        /// <summary>
        /// Finds the groups that will have no children left once the given objects are removed
        /// (including groups that only contain such groups) and returns operations to detach them.
        /// Deepest groups come first, so the operations are safe to run in order and to undo in reverse.
        /// </summary>
        public static List<IOperation> DetatchEmptiedGroups(IEnumerable<IMapObject> removing)
        {
            var list = removing.ToList();
            var gone = new HashSet<IMapObject>(list.SelectMany(x => x.FindAll()));
            var result = new List<IMapObject>();
            var queue = new Queue<IMapObject>();

            foreach (var o in list) queue.Enqueue(o.Hierarchy.Parent);

            while (queue.Count > 0)
            {
                var g = queue.Dequeue();
                if (!(g is Group) || g.Hierarchy.Parent == null || gone.Contains(g)) continue;
                if (!g.Hierarchy.All(gone.Contains)) continue;

                gone.Add(g);
                result.Add(g);
                queue.Enqueue(g.Hierarchy.Parent);
            }

            return result.Select(g => (IOperation) new Detatch(g.Hierarchy.Parent.ID, g)).ToList();
        }
    }
}
