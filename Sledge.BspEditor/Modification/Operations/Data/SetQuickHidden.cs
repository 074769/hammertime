using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification.Operations.Data
{
    /// <summary>
    /// Hides and/or reveals many objects in a single operation by adding/removing
    /// <see cref="QuickHidden"/> data. This replaces transactions containing thousands
    /// of individual <see cref="AddMapObjectData"/>/<see cref="RemoveMapObjectData"/>
    /// operations (used by quick hide/reveal), which were very slow because every single
    /// operation called <see cref="IMapObject.DescendantsChanged"/>, bubbling all the way
    /// up to the root and recomputing the whole-map bounding box once per object (O(N^2)).
    /// Visibility flags never participate in bounding-box computation (all
    /// <c>GetBoundingBox</c> implementations only use hierarchy/geometry, and
    /// <c>OnDescendantsChanged</c> has no overrides), so no refresh is needed here at all.
    /// </summary>
    public class SetQuickHidden : IOperation
    {
        private readonly List<long> _hide;
        private readonly List<long> _show;

        // Instances touched by the last Perform, kept so Reverse undoes exactly what was done.
        private readonly List<KeyValuePair<long, IMapObjectData>> _added = new List<KeyValuePair<long, IMapObjectData>>();
        private readonly List<KeyValuePair<long, IMapObjectData>> _removed = new List<KeyValuePair<long, IMapObjectData>>();

        private static readonly Type[] QuickHiddenType = { typeof(QuickHidden) };

        public bool Trivial => _hide.Count == 0 && _show.Count == 0 && _added.Count == 0 && _removed.Count == 0;

        public SetQuickHidden(IEnumerable<long> hide, IEnumerable<long> show)
        {
            _hide = hide != null ? hide.ToList() : new List<long>();
            _show = show != null ? show.ToList() : new List<long>();
        }

        public async Task<Change> Perform(MapDocument document)
        {
            var ch = new Change(document);
            _added.Clear();
            _removed.Clear();

            foreach (var id in _hide)
            {
                var obj = document.Map.Root.FindByID(id);
                if (obj == null) continue;
                // Skip objects that are already hidden: adding a second QuickHidden instance
                // makes them impossible to reveal with a single unhide.
                if (obj.Data.GetOne<QuickHidden>() != null) continue;
                var inst = new QuickHidden();
                obj.Data.Add(inst);
                _added.Add(new KeyValuePair<long, IMapObjectData>(id, inst));
                ch.Update(obj, QuickHiddenType);
            }

            foreach (var id in _show)
            {
                var obj = document.Map.Root.FindByID(id);
                if (obj == null) continue;
                // Remove every QuickHidden instance so objects hidden twice by the old
                // per-object implementation are healed as well.
                foreach (var inst in obj.Data.Get<QuickHidden>().ToList())
                {
                    obj.Data.Remove(inst);
                    _removed.Add(new KeyValuePair<long, IMapObjectData>(id, inst));
                    ch.Update(obj, QuickHiddenType);
                }
            }

            return ch;
        }

        public async Task<Change> Reverse(MapDocument document)
        {
            var ch = new Change(document);

            foreach (var kv in _added)
            {
                var obj = document.Map.Root.FindByID(kv.Key);
                if (obj == null) continue;
                obj.Data.Remove(kv.Value);
                ch.Update(obj, QuickHiddenType);
            }

            foreach (var kv in _removed)
            {
                var obj = document.Map.Root.FindByID(kv.Key);
                if (obj == null) continue;
                if (obj.Data.GetOne<QuickHidden>() == null)
                {
                    obj.Data.Add(kv.Value);
                }
                ch.Update(obj, QuickHiddenType);
            }

            _added.Clear();
            _removed.Clear();

            return ch;
        }
    }
}
