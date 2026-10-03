using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
	[Export(typeof(ICommand))]
    [CommandID("BspEditor:Tools:Rotate")]
	[DefaultHotkey("R")]
	public class RotateSelection : BaseCommand
    {
        public override string Name { get; set; } = "Rotate";
        public override string Details { get; set; } = "Rotate";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }
        
        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            if(parameters.Count == 0)
            {
                await Oy.Publish("SelectTool:TransformationModeChanged", "Rotate");
                return;
            }

            var selectedObjects = document.Selection.GetSelectedParents().ToList();
            if (selectedObjects.Count == 0) return;

            // Check if any selected objects are part of link groups
            var linkGroups = GetAffectedLinkGroups(document, selectedObjects);
            var useLinkOrigin = linkGroups.Any();

            Vector3 center;
            if (useLinkOrigin)
            {
                // Use the origin of the first link group as the center
                var firstGroup = linkGroups.First();
                var originObject = LinkedObjects.GetOrigin(firstGroup,
                    LinkedObjects.GetMembers(document)[firstGroup.ID]);
                center = originObject != null ? originObject.BoundingBox.Center : GetSelectionCenter(selectedObjects);
            }
            else
            {
                // Fall back to selection bounding box center
                center = GetSelectionCenter(selectedObjects);
            }

            var axis = parameters.Get<Vector3>("Axis");
            var amount = parameters.Get<float>("Angle");
            var radians = (float) MathHelper.DegreesToRadians(amount);

            var tl = document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags();

            var transaction = new Transaction();

            var tform = Matrix4x4.CreateTranslation(-center)
                        * Matrix4x4.CreateFromAxisAngle(axis, radians)
                        * Matrix4x4.CreateTranslation(center);

            // Get all objects to transform: either all objects in affected link groups, or just selected objects
            var objectsToTransform = useLinkOrigin
                ? GetAllObjectsInLinkGroups(document, linkGroups).ToList()
                : selectedObjects;

            var transformOperation = new BspEditor.Modification.Operations.Mutation.Transform(tform, objectsToTransform);
            transaction.Add(transformOperation);

            // Check for texture transform
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

        private Vector3 GetSelectionCenter(IList<IMapObject> objects)
        {
            if (objects.Count == 0) return Vector3.Zero;

            // Calculate the center of the bounding box of all objects
            var minX = objects.Min(o => o.BoundingBox.Start.X);
            var minY = objects.Min(o => o.BoundingBox.Start.Y);
            var minZ = objects.Min(o => o.BoundingBox.Start.Z);
            var maxX = objects.Max(o => o.BoundingBox.End.X);
            var maxY = objects.Max(o => o.BoundingBox.End.Y);
            var maxZ = objects.Max(o => o.BoundingBox.End.Z);

            return new Vector3(
                (minX + maxX) / 2,
                (minY + maxY) / 2,
                (minZ + maxZ) / 2
            );
        }
    }
}
