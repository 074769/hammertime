using System.Collections.Generic;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// A named storage group for one map. Its objects are never written into the map file; they are saved to
    /// the map's sidecar file instead. While the group is hidden its objects are not in the map at all.
    /// </summary>
    public class StorageGroup : IMapData
    {
        public bool AffectsRendering => false;

        public long ID { get; set; }
        public string Name { get; set; } = "Storage";
        public bool Visible { get; set; }

        /// <summary>The top-level objects in this group (hidden or visible)</summary>
        public List<IMapObject> Objects { get; } = new List<IMapObject>();

        public override string ToString() => $"{Name} ({Objects.Count})";

        public SerialisedObject ToSerialisedObject()
        {
            var so = new SerialisedObject("StorageGroup");
            so.Set("ID", ID);
            so.Set("Name", Name);
            so.Set("Visible", Visible);
            return so;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ID", ID);
            info.AddValue("Name", Name);
            info.AddValue("Visible", Visible);
        }

        public IMapElement Clone()
        {
            var g = new StorageGroup { ID = ID, Name = Name, Visible = Visible };
            g.Objects.AddRange(Objects);
            return g;
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            return new StorageGroup { ID = numberGenerator.Next("StorageGroup"), Name = Name, Visible = Visible };
        }
    }
}
