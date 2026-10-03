using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    /// <summary>
    /// Published before "Snap to grid" runs. A tool that wants to handle the command itself
    /// (instead of snapping the selected objects) sets <see cref="Handled"/> to true.
    /// </summary>
    public class SnapToGridRequest
    {
        public bool Handled { get; set; }
    }

    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "", "Snap", "B")]
    [CommandID("BspEditor:Tools:SnapToGrid")]
    [DefaultHotkey("Ctrl+B")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_SnapSelection))]
    public class SnapToGrid : BaseCommand
    {
        public override string Name { get; set; } = "Snap to grid";
        public override string Details { get; set; } = "Snap selection to grid";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            // Give the active tool a chance to handle this first (e.g. the vertex tool
            // snaps the selected vertices instead of the whole selection).
            var request = new SnapToGridRequest();
            await Oy.Publish("BspEditor:SnapToGrid:Request", request);
            if (request.Handled) return;

            var selectedObjects = document.Selection.GetSelectedParents().ToList();
            if (selectedObjects.Count == 0) return;

            // Check if any selected objects are part of link groups
            var linkGroups = GetAffectedLinkGroups(document, selectedObjects);
            var useLinkOrigin = linkGroups.Any();

            Vector3 startPoint;
            if (useLinkOrigin)
            {
                // Use the origin of the first link group as the reference point
                var firstGroup = linkGroups.First();
                var originObject = LinkedObjects.GetOrigin(firstGroup,
                    LinkedObjects.GetMembers(document)[firstGroup.ID]);
                startPoint = originObject != null ? originObject.BoundingBox.Start : GetSelectionStartPoint(selectedObjects);
            }
            else
            {
                // Fall back to selection bounding box start point
                startPoint = GetSelectionStartPoint(selectedObjects);
            }

            var grid = document.Map.Data.GetOne<GridData>();
            if (grid == null) return;

            var snapped = grid.Grid.Snap(startPoint);
            var trans = snapped - startPoint;
            if (trans == Vector3.Zero) return;

            var tform = Matrix4x4.CreateTranslation(trans);

            var transaction = new Transaction();
            // Get all objects to transform: either all objects in affected link groups, or just selected objects
            var objectsToTransform = useLinkOrigin
                ? GetAllObjectsInLinkGroups(document, linkGroups).ToList()
                : selectedObjects;
            var transformOperation = new BspEditor.Modification.Operations.Mutation.Transform(tform, objectsToTransform);
            transaction.Add(transformOperation);

            // Check for texture transform
            var tl = document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags();
            if (tl.TextureLock) transaction.Add(new TransformTexturesUniform(tform, objectsToTransform));

            await MapDocumentOperation.Perform(document, transaction);
        }

        private IEnumerable<LinkGroup> GetAffectedLinkGroups(MapDocument document, IList<IMapObject> selectedObjects)
        {
            var groups = document.Map.Data.Get<LinkGroup>().GroupBy(x => x.ID).ToDictionary(x => x.Key, x => x.First());
            var affectedGroups = new HashSet<LinkGroup>();

            foreach (var obj in selectedObjects)
            {
                var linkId = LinkedObjects.GetLinkId(obj);
                if (linkId.HasValue && groups.TryGetValue(linkId.Value, out var group))
                {
                    affectedGroups.Add(group);
                }
            }

            return affectedGroups;
        }

        private IEnumerable<IMapObject> GetAllObjectsInLinkGroups(MapDocument document, IEnumerable<LinkGroup> linkGroups)
        {
            var members = LinkedObjects.GetMembers(document);
            var objects = new List<IMapObject>();

            foreach (var group in linkGroups)
            {
                if (members.TryGetValue(group.ID, out var groupMembers))
                {
                    objects.AddRange(groupMembers);
                }
            }

            return objects;
        }

        private Vector3 GetSelectionStartPoint(IList<IMapObject> objects)
        {
            if (objects.Count == 0) return new Vector3();

            // Calculate the start point of the bounding box of all objects (minimum X, Y, Z)
            var minX = objects.Min(o => o.BoundingBox.Start.X);
            var minY = objects.Min(o => o.BoundingBox.Start.Y);
            var minZ = objects.Min(o => o.BoundingBox.Start.Z);

            return new Vector3(minX, minY, minZ);
        }
    }
}
