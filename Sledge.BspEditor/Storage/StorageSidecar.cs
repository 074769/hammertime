using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;

namespace Sledge.BspEditor.Storage
{
    /// <summary>
    /// Saves and restores storage groups in the map's sidecar file (the "Storage" section).
    /// The stored objects are written in the native map format, base64 encoded.
    /// </summary>
    [Export(typeof(StorageSidecar))]
    public class StorageSidecar
    {
        private const string SectionName = "Storage";

        private readonly SerialisedObjectFormatter _formatter;
        private readonly MapElementFactory _factory;

        [ImportingConstructor]
        public StorageSidecar([Import] Lazy<SerialisedObjectFormatter> formatter, [Import] Lazy<MapElementFactory> factory)
        {
            _formatter = formatter.Value;
            _factory = factory.Value;
        }

        private class GroupData
        {
            public long ID { get; set; }
            public string Name { get; set; }
        }

        private class StorageData
        {
            public List<GroupData> Groups { get; set; } = new List<GroupData>();
            public string Objects { get; set; }
        }

        /// <summary>Write the storage of a document to its sidecar file. Does nothing for unsaved documents.</summary>
        public void Save(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;

            var groups = StorageRules.Groups(document).OrderBy(x => x.ID).ToList();
            if (groups.Count == 0)
            {
                HammertimeSidecar.Remove(fileName, SectionName);
                return;
            }

            var list = new List<SerialisedObject>();
            foreach (var group in groups)
            {
                foreach (var obj in group.Objects)
                {
                    var so = _factory.Serialise(obj);
                    if (so != null) list.Add(so);
                }
            }

            var data = new StorageData
            {
                Groups = groups.Select(x => new GroupData { ID = x.ID, Name = x.Name }).ToList()
            };

            using (var ms = new MemoryStream())
            {
                _formatter.Serialize(ms, list);
                data.Objects = Convert.ToBase64String(ms.ToArray());
            }

            HammertimeSidecar.Set(fileName, SectionName, data);
        }

        /// <summary>Read the storage of a document from its sidecar file. Stored objects stay out of the map until shown.</summary>
        public void Load(MapDocument document)
        {
            var fileName = document?.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return;
            if (StorageRules.Groups(document).Any()) return;

            var data = HammertimeSidecar.Get<StorageData>(fileName, SectionName);
            if (data == null) return;

            foreach (var g in data.Groups ?? new List<GroupData>())
            {
                document.Map.Data.Add(new StorageGroup { ID = g.ID, Name = g.Name ?? "Storage", Visible = false });
                document.Map.NumberGenerator.Seed("StorageGroup", g.ID);
            }

            if (string.IsNullOrEmpty(data.Objects)) return;

            var bytes = Convert.FromBase64String(data.Objects);
            using (var ms = new MemoryStream(bytes))
            {
                foreach (var so in _formatter.Deserialize(ms))
                {
                    var obj = _factory.Deserialise(so) as IMapObject;
                    if (obj == null) continue;

                    var id = obj.Data.GetOne<StorageID>();
                    var group = id == null ? null : StorageRules.GetGroup(document, id.GroupID);
                    if (group == null) continue;

                    group.Objects.Add(obj);
                    SeedIds(document, obj);
                }
            }
        }

        /// <summary>Make sure new objects never reuse an id that a stored object already has.</summary>
        private static void SeedIds(MapDocument document, IMapObject obj)
        {
            foreach (var o in obj.FindAll())
            {
                document.Map.NumberGenerator.Seed("MapObject", o.ID);
                foreach (var face in o.Data.OfType<Face>())
                {
                    document.Map.NumberGenerator.Seed("Face", face.ID);
                }
            }
        }
    }
}
