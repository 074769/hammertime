using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.ChangeHandlers;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.ModelRefresh
{
    /// <summary>
    /// Reloads models from disk, for both GoldSrc and Source models. With a "Model" parameter only that
    /// model is reloaded; without one, every model loaded for the document's environment is reloaded.
    /// Entities using the model(s) are rebuilt so they show the new geometry and textures.
    /// This is not an undoable edit, since the map itself does not change.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Models:Refresh")]
    public class RefreshModels : BaseCommand
    {
        private readonly Lazy<ResourceCollection> _resourceCollection;

        public override string Name { get; set; } = "Refresh models";
        public override string Details { get; set; } = "Reload models from disk, including their textures.";

        [ImportingConstructor]
        public RefreshModels([Import] Lazy<ResourceCollection> resourceCollection)
        {
            _resourceCollection = resourceCollection;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            if (document?.Environment == null) return;

            var only = parameters?.Get<string>("Model", null);
            Func<string, bool> matches = name =>
                only == null || string.Equals(name, only, StringComparison.InvariantCultureIgnoreCase);

            // Entities currently showing a model that is being refreshed
            var entities = document.Map.Root.FindAll().OfType<Entity>()
                .Where(e =>
                {
                    var name = e.Data.GetOne<EntityModel>()?.Name;
                    return !string.IsNullOrWhiteSpace(name) && matches(name);
                })
                .ToList();

            // Remove the entity-side models first: their renderables reference the model's GPU data
            foreach (var entity in entities)
            {
                var em = entity.Data.GetOne<EntityModel>();
                if (em?.Renderable != null) _resourceCollection.Value.DestroyModelRenderable(document.Environment, em.Renderable);
                entity.Data.Remove(x => x is EntityModel);
            }

            // Forget the loaded model(s) and release any textures they uploaded by name
            var sharedReleased = await _resourceCollection.Value.UnloadModelsAsync(document.Environment, matches);

            // Rebuild. The map itself doesn't change, so this isn't undoable.
            Action<MapDocument> noop = x => { };
            if (sharedReleased > 0)
            {
                // Brush faces that used a released texture still point at it, so rebuild the whole map
                await MapDocumentOperation.Perform(document,
                    new TrivialOperation(
                        noop,
                        x => x.UpdateRange(x.Document.Map.Root.FindAll())
                    )
                );
            }
            else
            {
                await MapDocumentOperation.Perform(document,
                    new TrivialOperation(
                        noop,
                        x => x.UpdateRange(entities)
                    )
                );
            }
        }
    }
}
