using System;
using System.Collections.Generic;
using System.Linq;
using Sledge.BspEditor.Primitives.MapObjectData;

namespace Sledge.BspEditor.Modification.ChangeHandling
{
    /// <summary>
    /// Shared fast path for changes that only toggle per-object visibility metadata.
    /// Hiding/revealing thousands of objects at once (Ctrl+H / U) would otherwise make
    /// every change handler re-evaluate every touched object from scratch:
    /// visgroup membership, entity sprite/model/decal derivation, link snapshot diffs,
    /// etc. None of that state can change when the only object data touched is one of
    /// the well-known visibility flags, so handlers can return early.
    /// </summary>
    public static class VisibilityOnlyFastPath
    {
        /// <summary>
        /// Object-data types that never affect visgroup membership, entity model/sprite/
        /// decal derivation, or linked-object geometry snapshots. All shipped automatic
        /// visgroup predicates depend only on the object type, hierarchy, EntityData, or
        /// face textures; link property sync ignores these types (see
        /// LinkedObjects.IgnoredChildren, which contains all three *Hidden types), so
        /// propagating them to linked copies is a no-op and correctly skipped.
        /// </summary>
        private static readonly HashSet<Type> NeutralTypes = new HashSet<Type>
        {
            typeof(QuickHidden),
            typeof(VisgroupHidden),
            typeof(CordonHidden),
            typeof(ObjectColor),
        };

        /// <summary>
        /// True when the change only toggled neutral visibility metadata on existing
        /// objects: no added/removed objects (topology changes always need full
        /// handling) and every update carried object-data type information consisting
        /// solely of <see cref="NeutralTypes"/>.
        /// </summary>
        public static bool IsActive(Change change)
        {
            if (change == null) return false;
            if (change.Added.Any() || change.Removed.Any()) return false;
            if (!change.Updated.Any()) return false;
            if (!change.HasTrackedObjectUpdatesOnly) return false;
            return change.AffectedObjectDataTypes.All(t => NeutralTypes.Contains(t));
        }
    }
}
