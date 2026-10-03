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
    /// Make the instance of the selected object the origin of its link. The origin is the link's master copy:
    /// its shape wins if several matching objects are edited at once, "Add to link origin" adds to it,
    /// and it's marked in the viewports.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:LinkSetOrigin")]
    [MenuItem("Tools", "", "Link", "E")]
    public class LinkSetOrigin : BaseCommand
    {
        public override string Name { get; set; } = "Move link origin to selected";
        public override string Details { get; set; } = "Make the instance of the selected object the origin of its link.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && GetCandidates(document).Count > 0;
        }

        /// <summary>
        /// The selected objects whose instance isn't the origin of their link already.
        /// </summary>
        public static List<IMapObject> GetCandidates(MapDocument document)
        {
            var result = new List<IMapObject>();
            if (document.Selection.IsEmpty) return result;

            var index = LinkedObjects.BuildIndex(document);
            foreach (var o in document.Selection.GetSelectedParents())
            {
                var top = LinkedObjects.GetTopId(o);
                if (top == 0 || !index.Links.ContainsKey(top)) continue;
                if (LinkedObjects.GetInstance(o) != index.OriginInstance(top)) result.Add(o);
            }
            return result;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            // One origin per link: if the selection has several instances of one link, the first one wins
            var chosen = new Dictionary<long, long>();
            foreach (var o in GetCandidates(document))
            {
                var top = LinkedObjects.GetTopId(o);
                if (!chosen.ContainsKey(top)) chosen[top] = LinkedObjects.GetInstance(o);
            }
            if (chosen.Count == 0) return;

            var ops = chosen.Select(x => (IOperation) new SetOrigin(x.Key, x.Value)).ToList();
            await MapDocumentOperation.Perform(document, new Transaction(ops));
        }

        private class SetOrigin : IOperation
        {
            private readonly long _groupId;
            private readonly long _instance;
            private long _oldOrigin;

            public bool Trivial => false;

            public SetOrigin(long groupId, long instance)
            {
                _groupId = groupId;
                _instance = instance;
            }

            public Task<Change> Perform(MapDocument document)
            {
                var ch = new Change(document);
                var group = document.Map.Data.Get<LinkGroup>().FirstOrDefault(x => x.ID == _groupId);
                if (group != null)
                {
                    _oldOrigin = group.OriginInstance;
                    group.OriginInstance = _instance;
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
                    group.OriginInstance = _oldOrigin;
                    ch.Update(group);
                }
                return Task.FromResult(ch);
            }
        }
    }
}
