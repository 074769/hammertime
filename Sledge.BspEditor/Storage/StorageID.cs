using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// Marks a top-level map object as belonging to a storage group.
    /// </summary>
    public class StorageID : IMapObjectData
    {
        public long GroupID { get; set; }

        public StorageID(long groupId)
        {
            GroupID = groupId;
        }

        public StorageID(SerialisedObject obj)
        {
            GroupID = obj.Get<long>("GroupID");
        }

        [Export(typeof(IMapElementFormatter))]
        public class StorageIDFormatter : StandardMapElementFormatter<StorageID> { }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("GroupID", GroupID);
        }

        public IMapElement Clone()
        {
            return new StorageID(GroupID);
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            return Clone();
        }

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject("StorageID");
            so.Set("GroupID", GroupID);
            return so;
        }
    }
}
