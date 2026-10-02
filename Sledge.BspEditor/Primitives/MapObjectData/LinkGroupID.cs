using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Primitives.MapObjectData
{
    /// <summary>
    /// Marks a map object as a member of a link group.
    /// The link groups themselves are stored as map data (<see cref="MapData.LinkGroup"/>).
    /// </summary>
    public class LinkGroupID : IMapObjectData
    {
        public long ID { get; set; }

        public LinkGroupID(long id)
        {
            ID = id;
        }

        public LinkGroupID(SerialisedObject obj)
        {
            ID = obj.Get<long>("ID");
        }

        [Export(typeof(IMapElementFormatter))]
        public class LinkGroupIDFormatter : StandardMapElementFormatter<LinkGroupID> { }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ID", ID);
        }

        public IMapElement Clone()
        {
            return new LinkGroupID(ID);
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            // Copies (paste, duplicate) stay in the same link group
            return Clone();
        }

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject("LinkGroupID");
            so.Set("ID", ID);
            return so;
        }
    }
}
