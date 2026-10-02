using System;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapObjects;

namespace Sledge.BspEditor.Primitives.MapData
{
    /// <summary>
    /// The automatic visgroup that lists one link group under "Linked Objects" in the visgroup panel.
    /// These are generated from the <see cref="LinkGroup"/> map data and are never saved themselves.
    /// </summary>
    public class LinkedObjectsVisgroup : AutomaticVisgroup
    {
        public long GroupID { get; }

        public LinkedObjectsVisgroup(long groupId) : base(o => LinkedObjects.GetLinkId(o) == groupId)
        {
            GroupID = groupId;
        }

        public override IMapElement Clone()
        {
            return new LinkedObjectsVisgroup(GroupID)
            {
                Path = Path,
                Key = Key,
                Visible = Visible
            };
        }
    }
}
