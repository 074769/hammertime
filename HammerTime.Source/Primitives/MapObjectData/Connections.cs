using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace HammerTime.Source.Primitives.MapObjectData
{
    public class Connections : IMapObjectData
    {
        public struct Connection
        {
            public string Name;
            public string TargetEntity;
            public string TargetAction;
            public string Parameter;
            public float Delay;
            public bool Once;
        }
        public List<Connection> EntityConnections { get; private set; } = new List<Connection>();
        public Connections(List<Connection> connections)
        {
            EntityConnections = connections;
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            throw new NotImplementedException();
        }

        public SerialisedObject ToSerialisedObject()
        {
            throw new NotImplementedException();
        }

        public IMapElement Copy(UniqueNumberGenerator numberGenerator)
        {
            throw new NotImplementedException();
        }

        public IMapElement Clone()
        {
            throw new NotImplementedException();
        }
    }
}
