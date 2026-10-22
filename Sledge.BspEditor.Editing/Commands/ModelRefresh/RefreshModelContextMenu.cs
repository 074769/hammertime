using System;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.ChangeHandlers;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.Common.Shell.Commands;

namespace Sledge.BspEditor.Editing.Commands.ModelRefresh
{
    /// <summary>
    /// Adds a "Refresh model" submenu to the viewport right-click menu, listing the models
    /// used by the selected entities, plus an entry to refresh every loaded model.
    /// </summary>
    public static class RefreshModelContextMenu
    {
        public static void Populate(RightClickMenuBuilder builder, MapDocument document)
        {
            if (document?.Environment == null || document.Selection.IsEmpty) return;

            var names = document.Selection.GetSelectedParents()
                .OfType<Entity>()
                .Select(e => e.Data.GetOne<EntityModel>()?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                .OrderBy(n => n, StringComparer.InvariantCultureIgnoreCase)
                .ToList();
            if (names.Count == 0) return;

            var group = builder.AddGroup("Refresh model");
            foreach (var name in names)
            {
                var model = name;
                var item = new ToolStripMenuItem(model);
                item.Click += (s, e) => Oy.Publish("Command:Run",
                    new CommandMessage("BspEditor:Models:Refresh", new { Model = model }));
                group.DropDownItems.Add(item);
            }

            var all = new ToolStripMenuItem("All loaded models");
            all.Click += (s, e) => Oy.Publish("Command:Run",
                new CommandMessage("BspEditor:Models:Refresh", new { }));
            group.DropDownItems.Add(all);
        }
    }
}
