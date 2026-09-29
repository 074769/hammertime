using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Translations;
using Sledge.DataStructures.GameData;

namespace Sledge.BspEditor.Editing.Problems
{
    [Export(typeof(IProblemCheck))]
    [AutoTranslate]
    public class InvalidKeyValues : IProblemCheck
    {
        public string Name { get; set; }
        public string Details { get; set; }
        public Uri Url => null;
        public bool CanFix => true;

        // 'angles' is commonly present (often "0 0 0") on entities whose FGD class doesn't declare it
        private static readonly string[] AllowedEverywhere = { "angles" };

        // Keys any func_* entity can use (zhlt_usemodel, sequence, framerate)
        private static readonly string[] AllowedOnFuncs = { "zhlt_usemodel", "sequence", "framerate" };

        public async Task<List<Problem>> Check(MapDocument document, Predicate<IMapObject> filter)
        {
            var gamedata = await document.Environment.GetGameData();
            return document.Map.Root.FindAll()
                .Where(x => filter(x))
                .Select(x => new { Object = x, EntityData = x.Data.GetOne<EntityData>() })
                .Where(x => x.EntityData != null && GetInvalidKeys(gamedata, x.EntityData).Any())
                .Select(x => new Problem().Add(x.Object))
                .ToList();
        }

        private IEnumerable<string> GetInvalidKeys(GameData gamedata, EntityData data)
        {
            // Multimanagers require invalid key/values to work, exclude them from the search
            // Ignore worldspawn, Sledge has own key 'wad' that is throws errors 'InvalidKeys'
            if (string.Equals(data.Name, "multi_manager", StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(data.Name, "worldspawn", StringComparison.CurrentCultureIgnoreCase)) return new string[0];

            var cls = gamedata.GetClass(data.Name);
            if (cls == null) return new string[0];

            // Keys that are valid for the compile tools but not declared by the FGD classes
            var allowed = new List<string>(AllowedEverywhere);
            if (data.Name.StartsWith("func_", StringComparison.InvariantCultureIgnoreCase)) allowed.AddRange(AllowedOnFuncs);
            if (string.Equals(data.Name, "cycler_sprite", StringComparison.InvariantCultureIgnoreCase)) allowed.Add("scale");

            return data.Properties.Select(x => x.Key)
                .Where(x => !allowed.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                .Except(cls.Properties.Select(x => x.Name), StringComparer.CurrentCultureIgnoreCase);
        }

        public async Task Fix(MapDocument document, Problem problem)
        {
            var gamedata = await document.Environment.GetGameData();

            var transaction = new Transaction();

            foreach (var obj in problem.Objects)
            {
                var data = obj.Data.GetOne<EntityData>();
                if (data == null) continue;

                var vals = new Dictionary<string, string>();

                foreach (var key in GetInvalidKeys(gamedata, data))
                {
                    vals[key] = null;
                }

                transaction.Add(new EditEntityDataProperties(obj.ID, vals));
            }

            await MapDocumentOperation.Perform(document, transaction);
        }
    }
}