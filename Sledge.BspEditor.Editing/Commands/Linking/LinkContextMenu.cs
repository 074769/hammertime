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

            // Only offer links that still have objects
            var links = document.Map.Data.Get<LinkedObjectsVisgroup>()
                .Where(x => x.Instance == 0 && x.Objects.Count > 0)
                .Select(x => x.GroupID)
                .Distinct()
                .ToList();
            var names = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First().Name);

            var choices = links.Where(names.ContainsKey).Select(x => new KeyValuePair<long, string>(x, names[x])).OrderBy(x => x.Value).ToList();
            if (choices.Count > 0)
            {
                var add = builder.AddGroup("Add to link");
                foreach (var g in choices)
                {
                    var id = g.Key;
                    var linkMenu = new ToolStripMenuItem(g.Value);

                    var asInstance = new ToolStripMenuItem("As new instance");
                    asInstance.Click += (s, e) => Oy.Publish("Command:Run", new CommandMessage("BspEditor:Tools:LinkAddTo", new { GroupId = id }));
                    linkMenu.DropDownItems.Add(asInstance);

                    var toOrigin = new ToolStripMenuItem("To origin (updates every instance)");
                    toOrigin.Click += (s, e) => Oy.Publish("Command:Run", new CommandMessage("BspEditor:Tools:LinkAddToOrigin", new { GroupId = id }));
                    linkMenu.DropDownItems.Add(toOrigin);

                    add.DropDownItems.Add(linkMenu);
                }
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
    }
}
