using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Primitives.MapData
{
    /// <summary>
    /// A group of linked objects. Editing any member of the group edits all the members.
    /// The members themselves are marked with a <see cref="MapObjectData.LinkGroupID"/>.
    /// </summary>
    [Serializable]
    public class LinkGroup : IMapData
    {
        public bool AffectsRendering => false;

        /// <summary>The unique ID of this link group</summary>
        public long ID { get; set; }

        /// <summary>The display name of this link group</summary>
        public string Name { get; set; }

        /// <summary>The colour used to draw this group's outline and label in the viewports</summary>
        public Color Colour { get; set; } = Color.Orange;

        /// <summary>The ID of the origin object: the reference member of the group. 0 means "the lowest ID member".</summary>
        public long OriginID { get; set; }

        /// <summary>The ID of the link group this is a sublink of. 0 means this is a top level link group.</summary>
        public long ParentID { get; set; }

        public LinkGroup()
        {
        }

        public LinkGroup(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
            Name = obj.Get<string>("Name");
            Colour = obj.GetColor("Colour");
            OriginID = obj.Get<long>("OriginID");
            ParentID = obj.Get<long>("ParentID");
        }

        [Export(typeof(IMapElementFormatter))]
        public class LinkGroupFormatter : StandardMapElementFormatter<LinkGroup>
        {
        }

        protected LinkGroup(SerializationInfo info, StreamingContext context)
        {
            ID = info.GetInt64("ID");
            Name = info.GetString("Name");
            Colour = Color.FromArgb(info.GetInt32("Colour"));
            OriginID = TryGetLong(info, "OriginID");
            ParentID = TryGetLong(info, "ParentID");
        }

        private static long TryGetLong(SerializationInfo info, string name)
        {
            // Maps saved before sublinks and origins existed don't have these values
            foreach (var e in info)
            {
                if (e.Name == name) return Convert.ToInt64(e.Value);
            }
            return 0;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ID", ID);
            info.AddValue("Name", Name);
            info.AddValue("Colour", Colour.ToArgb());
            info.AddValue("OriginID", OriginID);
            info.AddValue("ParentID", ParentID);
        }

        public IMapElement Clone()
        {
            return new LinkGroup
            {
                ID = ID,
                Name = Name,
                Colour = Colour,
                OriginID = OriginID,
                ParentID = ParentID
            };
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            return Clone();
        }

        public SerialisedObject ToSerialisedObject()
        {
            var v = new SerialisedObject("LinkGroup");
            v.Set("ID", ID);
            v.Set("Name", Name);
            v.SetColor("Colour", Colour);
            v.Set("OriginID", OriginID);
            v.Set("ParentID", ParentID);
            return v;
        }
    }
}
