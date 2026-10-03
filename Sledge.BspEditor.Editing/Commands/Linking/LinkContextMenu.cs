using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.Common.Shell.Commands;

namespace Sledge.BspEditor.Editing.Commands.Linking
{
    /// <summary>
    /// Adds the link commands to the viewport right-click menu.
    /// </summary>
    public static class LinkContextMenu
    {
        public static void Populate(RightClickMenuBuilder builder, MapDocument document)
        {
            if (document == null || document.Selection.IsEmpty) return;

            var selected = document.Selection.GetSelectedParents().ToList();
            if (selected.Count == 0) return;

            builder.AddCommand("BspEditor:Tools:LinkObjects");

            // Only offer groups that still have members
            var names = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First().Name);
            var groups = document.Map.Data.Get<LinkedObjectsVisgroup>()
                .Where(x => x.Objects.Count > 0 && names.ContainsKey(x.GroupID))
                .Select(x => new { Id = x.GroupID, Name = names[x.GroupID] })
                .OrderBy(x => x.Name)
                .ToList();

            if (groups.Count > 0)
            {
                AddGroupMenu(builder, "Add to link", "BspEditor:Tools:LinkAddTo", groups.Select(x => new KeyValuePair<long, string>(x.Id, x.Name)));
                AddGroupMenu(builder, "Add as sublink of", "BspEditor:Tools:LinkAddSublink", groups.Select(x => new KeyValuePair<long, string>(x.Id, x.Name)));
            }

            if (selected.Any(x => LinkedObjects.GetLinkId(x) != null))
            {
                if (LinkSetOrigin.GetCandidates(document).Count > 0)
                {
                    builder.AddCommand("BspEditor:Tools:LinkSetOrigin");
                }
                builder.AddCommand("BspEditor:Tools:LinkRemove");
            }

            builder.AddSeparator();
        }

        private static void AddGroupMenu(RightClickMenuBuilder builder, string title, string command, IEnumerable<KeyValuePair<long, string>> groups)
        {
            var menu = builder.AddGroup(title);
            foreach (var g in groups)
            {
                var id = g.Key;
                var item = new ToolStripMenuItem(g.Value);
                item.Click += (s, e) => Oy.Publish("Command:Run", new CommandMessage(command, new { GroupId = id }));
                menu.DropDownItems.Add(item);
            }
        }
    }
}
