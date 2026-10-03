using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Primitives.MapObjectData
{
    /// <summary>
    /// Marks a map object as part of a link.
    ///
    /// A link is a group of objects. Copies of the group are the link's instances. Each object in an instance has
    /// a matching object in every other instance, and those matching objects form a "slot".
    /// The slots and the link are stored as map data (<see cref="MapData.LinkGroup"/>).
    /// </summary>
    public class LinkGroupID : IMapObjectData
    {
        /// <summary>The slot: the ID of the group of matching objects across the instances.</summary>
        public long ID { get; set; }

        /// <summary>The ID of the link this object's slot belongs to. 0 for links made before links had slots (the slot is the link).</summary>
        public long TopID { get; set; }

        /// <summary>Which instance (copy) of the link this object is in.</summary>
        public long Instance { get; set; }

        public LinkGroupID(long id) : this(id, 0, 0)
        {
        }

        public LinkGroupID(long id, long topId, long instance)
        {
            ID = id;
            TopID = topId;
            Instance = instance;
        }

        public LinkGroupID(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
            TopID = obj.Get<long>("TopID");
            Instance = obj.Get<long>("Instance");
        }

        [Export(typeof(IMapElementFormatter))]
        public class LinkGroupIDFormatter : StandardMapElementFormatter<LinkGroupID> { }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ID", ID);
            info.AddValue("TopID", TopID);
            info.AddValue("Instance", Instance);
        }

        public IMapElement Clone()
        {
            return new LinkGroupID(ID, TopID, Instance);
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            // Copies (paste, duplicate) keep their place in the link. If that place is taken, the copies become a new instance.
            return Clone();
        }

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject("LinkGroupID");
            so.Set("ID", ID);
            so.Set("TopID", TopID);
            so.Set("Instance", Instance);
            return so;
        }
    }
}
