using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;
using Sledge.DataStructures.Geometric;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace HammerTime.Source.Primitives.MapObjectData
{
    public class Connections : IMapObject
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
        private List<Connection> _connections = new List<Connection>();

        public long ID => throw new NotImplementedException();

        public bool IsSelected { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Box BoundingBox => throw new NotImplementedException();

        public MapObjectDataCollection Data => throw new NotImplementedException();

        public MapObjectHierarchy Hierarchy => throw new NotImplementedException();

        public Connections(List<Connection> connections)
        {
            _connections = connections;
        }

        public void Unclone(IMapObject obj)
        {
            throw new NotImplementedException();
        }

        public void DescendantsChanged()
        {
            throw new NotImplementedException();
        }

        public void Invalidate()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IMapObject> Decompose(IEnumerable<Type> allowedTypes)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Polygon> GetPolygons()
        {
            throw new NotImplementedException();
        }

        public bool Equals(IMapObject? other)
        {
            throw new NotImplementedException();
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

        public void Transform(Matrix4x4 matrix)
        {
            throw new NotImplementedException();
        }
    }
}
