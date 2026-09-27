using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Environment;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Textures
{
    /// <summary>
    /// Re-reads texture packages (e.g. WAD files) from disk for the active document's
    /// environment, discards any GPU-cached texture pixel data uploaded for it, and forces
    /// the viewport to reconvert and redraw everything. This lets the user edit a WAD file
    /// (add/replace/remove textures) and see the result in Hammertime without restarting it.
    /// </summary>
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("BspEditor:Textures:Reload")]
    public class ReloadTexturePackages : BaseCommand
    {
        private readonly Lazy<ResourceCollection> _resourceCollection;

        public override string Name { get; set; } = "Reload texture packages";
        public override string Details { get; set; } = "Re-read texture packages (WADs) from disk and refresh loaded textures.";

        [ImportingConstructor]
        public ReloadTexturePackages([Import] Lazy<ResourceCollection> resourceCollection)
        {
            _resourceCollection = resourceCollection;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            if (document?.Environment == null) return;

            // Re-scan disk for package contents (picks up textures added/changed/removed in a WAD).
            if (document.Environment is ITexturePackageManager packageManager)
            {
                packageManager.RefreshTexturePackages();
            }

            // Force the texture collection to actually be rebuilt now, rather than lazily on
            // next access, so the geometry reconversion below uses fresh data.
            await document.Environment.GetTextureCollection();

            // Discard any textures already uploaded to the GPU for this environment so they
            // get re-uploaded with the fresh pixel data next time they're needed.
            _resourceCollection.Value.RefreshTextures(document.Environment);

            // Force every object to reconvert and re-request its textures. This is not an
            // undoable edit - nothing about the map itself has changed.
            await MapDocumentOperation.Perform(document,
                new TrivialOperation(
                    x => { },
                    x => x.UpdateRange(x.Document.Map.Root.FindAll())
                )
            );
        }
    }
}
