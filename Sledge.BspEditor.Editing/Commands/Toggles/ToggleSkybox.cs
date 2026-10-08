using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Toggles
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Map:ToggleSkybox")]
    [MenuItem("View", "", "Rendering", "Z")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_Skybox))]
    public class ToggleSkybox : BaseCommand, IMenuItemExtendedProperties
    {
        public override string Name { get; set; } = "Skybox preview";
        public override string Details { get; set; } = "Toggle the skybox preview. When it is on, sky textures are hidden and the skybox is drawn in their place.";

        /// <summary>
        /// Marks the menu and toolbar items of this command as a switch with an on/off state.
        /// </summary>
        public bool IsToggle => true;

        public bool GetToggleState(IContext context)
        {
            if (!context.TryGet("ActiveDocument", out MapDocument document)) return false;
            var flags = document.Map.Data.GetOne<DisplayFlags>() ?? new DisplayFlags();
            return flags.ToggleSkybox;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var flags = document.Map.Data.GetOne<DisplayFlags>() ?? new DisplayFlags();
            var displayData = document.Map.Data.GetOne<DisplayData>() ?? new DisplayData();

            var data = document.Map.Root.Data.Get<EntityData>().FirstOrDefault();
            var skyname = data?.Get<string>("skyname", null);
            var sky = document.Environment.GetSkyboxes().FirstOrDefault(x => x.Name == skyname);

            if (flags.ToggleSkybox)
            {
                // Turning the preview off always works.
                flags.ToggleSkybox = false;
            }
            else if (sky != null)
            {
                flags.ToggleSkybox = true;
                displayData.SkyboxName = skyname;
            }
            else
            {
                // Without a skybox texture there is nothing to preview.
                await Oy.Publish("Status:Information", "This map has no skybox texture to preview.");
                return;
            }

            await MapDocumentOperation.Perform(document,
                new TrivialOperation(
                    x =>
                    {
                        x.Map.Data.Replace(flags);
                        x.Map.Data.Replace(displayData);
                    },
                    x =>
                    {
                        x.Update(flags);
                        x.Update(displayData);
                    }));
        }
    }
}
