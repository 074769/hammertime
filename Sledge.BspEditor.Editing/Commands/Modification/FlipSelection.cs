using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    public abstract class FlipSelection : BaseCommand
    {
        public override string Name { get; set; } = "Flip";
        public override string Details { get; set; } = "Flip";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected abstract Vector3 GetScale();

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var selectedObjects = document.Selection.GetSelectedParents().ToList();
            if (selectedObjects.Count == 0) return;

            // Check if any selected objects are part of link groups
            var linkGroups = GetAffectedLinkGroups(document, selectedObjects);
            var useLinkOrigin = linkGroups.Any();

            Vector3 center;
            if (useLinkOrigin)
            {
                // Use the origin of the first link group as the center
                // For multiple link groups, we could use a combined center, but for simplicity
                // we'll use the first group's origin (this matches typical expectation)
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

            var tl = document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags();

            var transaction = new Transaction();

            var tform = Matrix4x4.CreateTranslation(-center)
                        * Matrix4x4.CreateScale(GetScale())
                        * Matrix4x4.CreateTranslation(center);

            // Get all objects to transform: either all objects in affected link groups, or just selected objects
            var objectsToTransform = useLinkOrigin
                ? GetAllObjectsInLinkGroups(document, linkGroups).ToList()
                : selectedObjects;

            var transformOperation = new BspEditor.Modification.Operations.Mutation.Transform(tform, objectsToTransform);
            transaction.Add(transformOperation);

            // For face flipping, we need to flip faces on the transformed objects
            transaction.Add(new FlipFaces(objectsToTransform));

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

        private class FlipFaces : IOperation
        {
            private readonly List<long> _idsToTransform;

            public bool Trivial => false;

            public FlipFaces(IEnumerable<IMapObject> objectsToTransform)
            {
                _idsToTransform = objectsToTransform.Select(x => x.ID).ToList();
            }

            public Task<Change> Perform(MapDocument document)
            {
                var ch = new Change(document);

                var objects = _idsToTransform.Select(x => document.Map.Root.FindByID(x)).Where(x => x != null).ToList();

                foreach (var o in objects)
                {
                    foreach (var it in o.Data.OfType<Face>())
                    {
                        it.Vertices.Flip();
                        ch.Update(o);
                    }
                }

                return Task.FromResult(ch);
            }

            public Task<Change> Reverse(MapDocument document)
            {
                return Perform(document); // Reversing this operation means just performing it again
            }
        }
    }

    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "Flip", "FlipAlign", "B")]
    [CommandID("BspEditor:Tools:FlipX")]
    public class FlipSelectionX : FlipSelection
    {
        protected override Vector3 GetScale()
        {
            return new Vector3(-1, 1, 1);
        }
    }

    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "Flip", "FlipAlign", "D")]
    [CommandID("BspEditor:Tools:FlipY")]
    public class FlipSelectionY : FlipSelection
    {
        protected override Vector3 GetScale()
        {
            return new Vector3(1, -1, 1);
        }
    }

    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "Flip", "FlipAlign", "F")]
    [CommandID("BspEditor:Tools:FlipZ")]
    public class FlipSelectionZ : FlipSelection
    {
        protected override Vector3 GetScale()
        {
            return new Vector3(1, 1, -1);
        }
    }
}
