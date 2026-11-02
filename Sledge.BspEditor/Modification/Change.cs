using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Modification
{
    /// <summary>
    /// Represents a change set as a result of an operation on a document.
    /// </summary>
    public class Change
    {
        /// <summary>
        /// The document modified by the change
        /// </summary>
        public MapDocument Document { get; }

        private readonly HashSet<IMapData> _affectedData;

        /// <summary>
        /// The runtime types of object data (<see cref="MapObjectData.IMapObjectData"/>)
        /// that were added, removed, or replaced during the change.
        /// </summary>
        private readonly HashSet<Type> _affectedObjectDataTypes;

        /// <summary>
        /// True if any object in the change was reported without object-data type
        /// information (e.g. a plain <see cref="Update(IMapObject)"/> call).
        /// When true, per-data-type optimisations must not assume anything.
        /// </summary>
        private bool _hasUntrackedObjectUpdates;

        private readonly HashSet<IMapObject> _added;
        private readonly HashSet<IMapObject> _updated;
        private readonly HashSet<IMapObject> _removed;

        /// <summary>
        /// The updated objects whose only change is being selected or deselected. Always a subset of the
        /// updated objects: an object that is also updated for any other reason is not in this set.
        /// </summary>
        private readonly HashSet<IMapObject> _selectionUpdated;

        /// <summary>
        /// The items that were added during the change
        /// </summary>
        public IEnumerable<IMapObject> Added => _added;

        /// <summary>
        /// The items that were updated during the change
        /// </summary>
        public IEnumerable<IMapObject> Updated => _updated;

        /// <summary>
        /// The items that were removed during the change
        /// </summary>
        public IEnumerable<IMapObject> Removed => _removed;

        /// <summary>
        /// The map data objects which were affected during the change
        /// </summary>
        public IEnumerable<IMapData> AffectedData => _affectedData;

        /// <summary>
        /// True if there are object changes in this change
        /// </summary>
        public bool HasObjectChanges => _added.Count + _updated.Count + _removed.Count > 0;

        /// <summary>
        /// True if there are map data changes in this change
        /// </summary>
        public bool HasDataChanges => _affectedData.Count > 0;

        /// <summary>
        /// The runtime types of object data that were added, removed, or replaced
        /// during the change.
        /// </summary>
        public IEnumerable<Type> AffectedObjectDataTypes => _affectedObjectDataTypes;

        /// <summary>
        /// True if every object update in the change carried object-data type
        /// information (i.e. no plain <see cref="Update(IMapObject)"/> calls).
        /// </summary>
        public bool HasTrackedObjectUpdatesOnly => !_hasUntrackedObjectUpdates;

        /// <summary>
        /// True when nothing changed except which objects are selected: no objects were added or removed, no
        /// object or map data changed, and every updated object was only selected or deselected.
        /// Handlers and the scene can use this to skip work that selection can't affect.
        /// </summary>
        public bool IsSelectionOnly =>
            _updated.Count > 0
            && _added.Count == 0
            && _removed.Count == 0
            && _selectionUpdated.Count == _updated.Count
            && _affectedData.Count == 0
            && _affectedObjectDataTypes.Count == 0;

        public Change(MapDocument document)
        {
            Document = document;

            _added = new HashSet<IMapObject>();
            _updated = new HashSet<IMapObject>();
            _removed = new HashSet<IMapObject>();
            _selectionUpdated = new HashSet<IMapObject>();

            _affectedData = new HashSet<IMapData>();
            _affectedObjectDataTypes = new HashSet<Type>();
        }

        public Change Add(IMapObject o)
        {
            _selectionUpdated.Remove(o);
            _added.Add(o);
            _removed.Remove(o);
            _updated.Remove(o);
            return this;
        }

        public Change AddRange(IEnumerable<IMapObject> objects)
        {
            var all = objects.ToList();
            _selectionUpdated.ExceptWith(all);
            _added.UnionWith(all);
            _removed.ExceptWith(all);
            _updated.ExceptWith(all);
            return this;
        }

        public Change Update(IMapObject o)
        {
            _selectionUpdated.Remove(o);
            _hasUntrackedObjectUpdates = true;
            if (_added.Contains(o)) return this;
            if (_removed.Contains(o)) return this;
            _updated.Add(o);
            return this;
        }

        /// <summary>
        /// Report an object update along with the object-data types that were
        /// added, removed, or replaced on it. Unlike <see cref="Update(IMapObject)"/>,
        /// this keeps per-data-type optimisation possible.
        /// </summary>
        public Change Update(IMapObject o, IEnumerable<Type> objectDataTypes)
        {
            _selectionUpdated.Remove(o);
            if (_added.Contains(o)) { TrackObjectDataTypes(objectDataTypes); return this; }
            if (_removed.Contains(o)) { TrackObjectDataTypes(objectDataTypes); return this; }
            _updated.Add(o);
            TrackObjectDataTypes(objectDataTypes);
            return this;
        }

        /// <summary>
        /// Report that an object's children changed. The object and every ancestor
        /// have a different bounding box now, so their visuals (entity boxes, names,
        /// sprite and model placement) must be rebuilt, which only happens for objects
        /// reported as updated.
        /// </summary>
        public Change UpdateWithAncestors(IMapObject o)
        {
            var p = o;
            while (p != null)
            {
                Update(p);
                p = p.Hierarchy.Parent;
            }
            return this;
        }

        /// <summary>
        /// Report object-data types that were added, removed, or replaced during
        /// the change, without adding any object updates.
        /// </summary>
        public Change UpdateObjectDataTypes(IEnumerable<Type> objectDataTypes)
        {
            TrackObjectDataTypes(objectDataTypes);
            return this;
        }

        private void TrackObjectDataTypes(IEnumerable<Type> objectDataTypes)
        {
            if (objectDataTypes == null) return;
            foreach (var t in objectDataTypes)
            {
                if (t != null) _affectedObjectDataTypes.Add(t);
            }
        }

        public Change UpdateRange(IEnumerable<IMapObject> objects)
        {
            var all = objects.ToList();
            _selectionUpdated.ExceptWith(all);
            return UpdateRangeCore(all);
        }

        /// <summary>
        /// Report objects whose only change is being selected or deselected. Unlike
        /// <see cref="UpdateRange"/>, this lets handlers and the scene tell the change apart from a real edit.
        /// An object that was already updated for another reason stays a real update.
        /// </summary>
        public Change UpdateSelectionRange(IEnumerable<IMapObject> objects)
        {
            var all = objects.ToList();

            // Objects already updated (not just selected) in this change, or added or removed by it, are not selection-only
            var selectionOnly = all
                .Where(o => !_added.Contains(o) && !_removed.Contains(o) && (!_updated.Contains(o) || _selectionUpdated.Contains(o)))
                .ToList();

            UpdateRangeCore(all);
            _selectionUpdated.UnionWith(selectionOnly);
            return this;
        }

        private Change UpdateRangeCore(List<IMapObject> all)
        {
            // No object-data type information here (transforms, etc.), so per-data-type fast paths must not apply
            if (all.Count > 0) _hasUntrackedObjectUpdates = true;
            _updated.UnionWith(all);
            _added.ExceptWith(all);
            _removed.ExceptWith(all);
            return this;
        }

        public Change Update(IMapData data)
        {
            _affectedData.Add(data);
            return this;
        }

        public Change Update(IEnumerable<IMapData> datas)
        {
            _affectedData.UnionWith(datas);
            return this;
        }

        public Change Remove(IMapObject o)
        {
            _selectionUpdated.Remove(o);
            _added.Remove(o);
            _updated.Remove(o);
            _removed.Add(o);
            return this;
        }

        public Change RemoveRange(IEnumerable<IMapObject> objects)
        {
            var all = objects.ToList();
            _selectionUpdated.ExceptWith(all);
            _added.ExceptWith(all);
            _updated.ExceptWith(all);
            _removed.UnionWith(all);
            return this;
        }

        public Change Merge(Change change)
        {
            // Objects with a real (non-selection) update in either change must stay real updates once merged
            var realUpdates = _updated.Where(o => !_selectionUpdated.Contains(o))
                .Concat(change._updated.Where(o => !change._selectionUpdated.Contains(o)))
                .ToList();

            _added.UnionWith(change._added);
            _removed.ExceptWith(change._added);
            _updated.ExceptWith(change._added);

            _added.ExceptWith(change._removed);
            _updated.ExceptWith(change._removed);
            _removed.UnionWith(change._removed);

            _updated.UnionWith(change._updated.Except(_added).Except(_removed));

            _affectedData.UnionWith(change._affectedData);
            _affectedObjectDataTypes.UnionWith(change._affectedObjectDataTypes);
            _hasUntrackedObjectUpdates |= change._hasUntrackedObjectUpdates;

            _selectionUpdated.UnionWith(change._selectionUpdated);
            _selectionUpdated.ExceptWith(realUpdates);
            _selectionUpdated.IntersectWith(_updated);

            return this;
        }
    }
}