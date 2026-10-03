using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Make the selected object the origin object of its link. The origin object is the link's reference:
    /// it's the one whose shape wins if several members are edited at once, and it's marked in the viewports.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkSetOrigin")]
    [MenuItem("Tools", "", "Link", "E")]
    public class LinkSetOrigin : BaseCommand
    {
        public override string Name { get; set; } = "Move link origin to selected";
        public override string Details { get; set; } = "Make the selected object the origin object of its link.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && GetCandidates(document).Count > 0;
        }

        /// <summary>
        /// The selected objects that are linked and aren't the origin of their link already.
        /// </summary>
        public static List<IMapObject> GetCandidates(MapDocument document)
        {
            var result = new List<IMapObject>();
            if (document.Selection.IsEmpty) return result;

            var members = LinkedObjects.GetMembers(document);
            var groups = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());

            foreach (var o in document.Selection.GetSelectedParents())
            {
                var id = LinkedObjects.GetLinkId(o);
                if (id == null || !members.TryGetValue(id.Value, out var list)) continue;
                groups.TryGetValue(id.Value, out var group);
                if (!ReferenceEquals(LinkedObjects.GetOrigin(group, list), o)) result.Add(o);
            }
            return result;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            // One origin per link: if the selection has several objects from one link, the first one wins
            var chosen = new Dictionary<long, long>();
            foreach (var o in GetCandidates(document))
            {
                var id = LinkedObjects.GetLinkId(o).Value;
                if (!chosen.ContainsKey(id)) chosen[id] = o.ID;
            }
            if (chosen.Count == 0) return;

            var ops = chosen.Select(x => (IOperation) new SetOrigin(x.Key, x.Value)).ToList();
            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }

        private class SetOrigin : IOperation
        {
            private readonly long _groupId;
            private readonly long _objectId;
            private long _oldOrigin;

            public bool Trivial => false;

            public SetOrigin(long groupId, long objectId)
            {
                _groupId = groupId;
                _objectId = objectId;
            }

            public Task<Change> Perform(MapDocument document)
            {
                var ch = new Change(document);
                var group = document.Map.Data.Get<LinkGroup>().FirstOrDefault(x => x.ID == _groupId);
                if (group != null)
                {
                    _oldOrigin = group.OriginID;
                    group.OriginID = _objectId;
                    ch.Update(group);
                }
                return Task.FromResult(ch);
            }

            public Task<Change> Reverse(MapDocument document)
            {
                var ch = new Change(document);
                var group = document.Map.Data.Get<LinkGroup>().FirstOrDefault(x => x.ID == _groupId);
                if (group != null)
                {
                    group.OriginID = _oldOrigin;
                    ch.Update(group);
                }
                return Task.FromResult(ch);
            }
        }
    }
}
