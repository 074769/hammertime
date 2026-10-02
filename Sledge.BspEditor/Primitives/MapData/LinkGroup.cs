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

        public LinkGroup()
        {
        }

        public LinkGroup(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
            Name = obj.Get<string>("Name");
            Colour = obj.GetColor("Colour");
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
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ID", ID);
            info.AddValue("Name", Name);
            info.AddValue("Colour", Colour.ToArgb());
        }

        public IMapElement Clone()
        {
            return new LinkGroup
            {
                ID = ID,
                Name = Name,
                Colour = Colour
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
            return v;
        }
    }
}
