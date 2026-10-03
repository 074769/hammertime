using System;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Primitives.MapData
{
    /// <summary>
    /// The automatic visgroup that lists a link (or one instance of it) under "Linked Objects" in the visgroup panel.
    /// These are generated from the <see cref="LinkGroup"/> map data and are never saved themselves.
    /// </summary>
    public class LinkedObjectsVisgroup : AutomaticVisgroup
    {
        public long GroupID { get; }

        /// <summary>The instance this lists, or 0 for the whole link.</summary>
        public long Instance { get; }

        public LinkedObjectsVisgroup(long groupId, long instance = 0)
            : base(o => LinkedObjects.GetTopId(o) == groupId && (instance == 0 || LinkedObjects.GetInstance(o) == instance))
        {
            GroupID = groupId;
            Instance = instance;
        }

        public override IMapElement Clone()
        {
            return new LinkedObjectsVisgroup(GroupID, Instance)
            {
                Path = Path,
                Key = Key,
                Visible = Visible
            };
        }
    }
}
