using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Primitives.MapData
{
    /// <summary>
    /// Either a link (a group of objects, copied as instances) or one of its slots (the matching objects across the instances).
    /// Objects are marked with a <see cref="MapObjectData.LinkGroupID"/>.
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

        /// <summary>For a link: the instance that is the origin (the master copy). 0 means the lowest numbered instance.</summary>
        public long OriginInstance { get; set; }

        /// <summary>For a slot: the ID of the link it belongs to. 0 for a link itself.</summary>
        public long ParentID { get; set; }

        public LinkGroup()
        {
        }

        public LinkGroup(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
            Name = obj.Get<string>("Name");
            Colour = obj.GetColor("Colour");
            OriginInstance = obj.Get<long>("OriginInstance");
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
            OriginInstance = TryGetLong(info, "OriginInstance");
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
            info.AddValue("OriginInstance", OriginInstance);
            info.AddValue("ParentID", ParentID);
        }

        public IMapElement Clone()
        {
            return new LinkGroup
            {
                ID = ID,
                Name = Name,
                Colour = Colour,
                OriginInstance = OriginInstance,
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
            v.Set("OriginInstance", OriginInstance);
            v.Set("ParentID", ParentID);
            return v;
        }
    }
}
