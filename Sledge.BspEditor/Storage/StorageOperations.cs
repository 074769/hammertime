using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// Moves world objects into a storage group. If the group is hidden the objects leave the map tree;
    /// if it is visible they stay where they are and are just tagged.
    /// </summary>
    public class MoveToStorage : IOperation
    {
        private readonly long _groupId;
        private readonly List<IMapObject> _objects;
        private readonly Dictionary<IMapObject, IMapObject> _parents = new Dictionary<IMapObject, IMapObject>();
        private readonly HashSet<IMapObject> _detached = new HashSet<IMapObject>();

        public bool Trivial => false;

        public MoveToStorage(long groupId, IEnumerable<IMapObject> objects)
        {
            _groupId = groupId;
            _objects = objects.ToList();
        }

        public async Task<Change> Perform(MapDocument document)
        {
            var ch = new Change(document);
            _detached.Clear();

            var group = StorageRules.GetGroup(document, _groupId);
            if (group == null) return ch;

            foreach (var o in _objects)
            {
                _parents[o] = o.Hierarchy.Parent;
                o.Data.Replace(new StorageID(_groupId));
                group.Objects.Add(o);

                if (group.Visible)
                {
                    ch.Update(o);
                }
                else
                {
                    ch.Remove(o);
                    ch.RemoveRange(o.FindAll());
                    o.Hierarchy.Parent = null;
                    _detached.Add(o);
                }
            }

            ch.Update(group);
            return ch;
        }

        public async Task<Change> Reverse(MapDocument document)
        {
            var ch = new Change(document);
            var group = StorageRules.GetGroup(document, _groupId);

            foreach (var o in _objects)
            {
                group?.Objects.Remove(o);
                o.Data.Remove(x => x is StorageID);

                if (_detached.Contains(o))
                {
                    o.Hierarchy.Parent = _parents[o];
                    ch.Add(o);
                    ch.AddRange(o.FindAll());
                }
                else
                {
                    ch.Update(o);
                }
            }

            _detached.Clear();
            if (group != null) ch.Update(group);
            return ch;
        }
    }

    /// <summary>
    /// Takes visible storage objects back out of their group, leaving them in the map.
    /// </summary>
    public class MoveOutOfStorage : IOperation
    {
        private readonly List<IMapObject> _objects;
        private readonly Dictionary<IMapObject, long> _groups = new Dictionary<IMapObject, long>();

        public bool Trivial => false;

        public MoveOutOfStorage(IEnumerable<IMapObject> objects)
        {
            _objects = objects.ToList();
        }

        public async Task<Change> Perform(MapDocument document)
        {
            var ch = new Change(document);
            _groups.Clear();

            foreach (var o in _objects)
            {
                var id = o.Data.GetOne<StorageID>();
                if (id == null) continue;

                _groups[o] = id.GroupID;
                var group = StorageRules.GetGroup(document, id.GroupID);
                group?.Objects.Remove(o);
                o.Data.Remove(x => x is StorageID);

                ch.Update(o);
                if (group != null) ch.Update(group);
            }
            return ch;
        }

        public async Task<Change> Reverse(MapDocument document)
        {
            var ch = new Change(document);

            foreach (var kv in _groups)
            {
                var group = StorageRules.GetGroup(document, kv.Value);
                kv.Key.Data.Replace(new StorageID(kv.Value));
                group?.Objects.Add(kv.Key);

                ch.Update(kv.Key);
                if (group != null) ch.Update(group);
            }
            return ch;
        }
    }

    /// <summary>
    /// Shows or hides a storage group. Showing puts its objects back in the map; hiding takes them out.
    /// </summary>
    public class SetStorageVisible : IOperation
    {
        private readonly long _groupId;
        private readonly bool _visible;

        public bool Trivial => false;

        public SetStorageVisible(long groupId, bool visible)
        {
            _groupId = groupId;
            _visible = visible;
        }

        public async Task<Change> Perform(MapDocument document)
        {
            return Apply(document, _groupId, _visible);
        }

        public async Task<Change> Reverse(MapDocument document)
        {
            return Apply(document, _groupId, !_visible);
        }

        private static Change Apply(MapDocument document, long groupId, bool visible)
        {
            var ch = new Change(document);
            var group = StorageRules.GetGroup(document, groupId);
            if (group == null) return ch;

            group.Visible = visible;
            ch.Update(group);

            var root = document.Map.Root;
            var visgroups = document.Map.Data.Get<Visgroup>().ToDictionary(x => x.ID, x => x);

            foreach (var o in group.Objects)
            {
                if (visible)
                {
                    if (o.Hierarchy.Parent != null) continue;

                    // Keep the object hidden if one of its visgroups is hidden
                    var hidden = o.Data.OfType<VisgroupID>().Any(id => visgroups.TryGetValue(id.ID, out var vg) && !vg.Visible);
                    o.Data.Replace(new VisgroupHidden(hidden));

                    o.Hierarchy.Parent = root;
                    ch.Add(o);
                    ch.AddRange(o.FindAll());
                }
                else
                {
                    if (o.Hierarchy.Parent == null) continue;

                    ch.Remove(o);
                    ch.RemoveRange(o.FindAll());
                    o.Hierarchy.Parent = null;
                }
            }
            return ch;
        }
    }

    /// <summary>
    /// Renames a storage group.
    /// </summary>
    public class RenameStorageGroup : IOperation
    {
        private readonly long _groupId;
        private readonly string _name;
        private string _previous;

        public bool Trivial => false;

        public RenameStorageGroup(long groupId, string name)
        {
            _groupId = groupId;
            _name = name;
        }

        public async Task<Change> Perform(MapDocument document)
        {
            var ch = new Change(document);
            var group = StorageRules.GetGroup(document, _groupId);
            if (group == null) return ch;

            _previous = group.Name;
            group.Name = _name;
            ch.Update(group);
            return ch;
        }

        public async Task<Change> Reverse(MapDocument document)
        {
            var ch = new Change(document);
            var group = StorageRules.GetGroup(document, _groupId);
            if (group == null) return ch;

            group.Name = _previous;
            ch.Update(group);
            return ch;
        }
    }
}
