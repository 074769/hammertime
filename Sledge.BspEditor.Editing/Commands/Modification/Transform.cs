using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Linking;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Editing.Commands.Modification
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [MenuItem("Tools", "", "Transform", "D")]
    [CommandID("BspEditor:Tools:Transform")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_Transform))]
    [DefaultHotkey("Ctrl+M")]
    public class Transform : BaseCommand
    {
        [Import] private Lazy<ITranslationStringProvider> _translator;

        public override string Name { get; set; } = "Transform";
        public override string Details { get; set; } = "Transform the current selection";

        public string ErrorCannotScaleByZeroTitle { get; set; } = "Cannot scale by zero";
        public string ErrorCannotScaleByZeroMessage { get; set; } = "Please enter a non-zero value for all axes when scaling.";

        protected override bool IsInContext(IContext context, MapDocument document)
        {
            return base.IsInContext(context, document) && !document.Selection.IsEmpty;
        }

        protected override async Task Invoke(MapDocument document, CommandParameters parameters)
        {
            var selectedObjects = document.Selection.GetSelectedParents().ToList();
            if (selectedObjects.Count == 0) return;

            var selectionBox = document.Selection.GetSelectionBoundingBox();

            // Check if any selected objects are part of link groups
            var linkGroups = GetAffectedLinkGroups(document, selectedObjects);
            var useLinkOrigin = linkGroups.Any();

            // Determine the center point to use for the transformation dialog
            Vector3 dialogCenter;
            if (useLinkOrigin)
            {
                // Use the origin of the first link group as the center for the dialog
                var firstGroup = linkGroups.First();
                var originObject = LinkedObjects.GetOrigin(firstGroup,
                    LinkedObjects.GetMembers(document)[firstGroup.ID]);
                dialogCenter = originObject != null ? originObject.BoundingBox.Center : GetSelectionCenter(selectedObjects);
            }
            else
            {
                // Fall back to selection bounding box center
                dialogCenter = GetSelectionCenter(selectedObjects);
            }

            using (var dialog = new TransformDialog(selectionBox))
            {
                // Shift the dialog to be centered around our chosen center point
                // This is a hack - we'll adjust the transformation afterward
                _translator.Value.Translate(dialog);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        var transaction = new Transaction();

                        // Get the base transformation from the dialog (centered on selectionBox)
                        var baseTransform = dialog.GetTransformation(selectionBox);

                        // If we're using link origin, we need to adjust the transformation
                        // to be centered around the link origin instead of selection center
                        Matrix4x4 finalTransform;
                        if (useLinkOrigin)
                        {
                            // Calculate the adjustment to move from selection center to link origin
                            Vector3 selectionCenter = GetSelectionCenter(selectedObjects);
                            Vector3 linkOrigin = dialogCenter; // This is what we set above

                            // The dialog gives us a transform centered on selectionCenter
                            // We want it centered on linkOrigin instead
                            // T_desired = Translate(-linkOrigin) * R * Translate(linkOrigin)
                            // where R is the rotation/scale part of the base transform

                            // Extract the rotation/scale part from the base transform
                            // by removing the translation components that center on selectionCenter
                            var translationToOrigin = Matrix4x4.CreateTranslation(-selectionCenter);
                            var translationBackFromOrigin = Matrix4x4.CreateTranslation(selectionCenter);
                            var rsPart = translationBackFromOrigin * baseTransform * translationToOrigin;

                            // Now build the desired transform centered on linkOrigin
                            var translationToLinkOrigin = Matrix4x4.CreateTranslation(-linkOrigin);
                            var translationBackFromLinkOrigin = Matrix4x4.CreateTranslation(linkOrigin);
                            finalTransform = translationBackFromLinkOrigin * rsPart * translationToLinkOrigin;
                        }
                        else
                        {
                            finalTransform = baseTransform;
                        }

                        // Get all objects to transform: either all objects in affected link groups, or just selected objects
                        var objectsToTransform = useLinkOrigin
                            ? GetAllObjectsInLinkGroups(document, linkGroups).ToList()
                            : selectedObjects;

                        // Add the operation
                        var transformOperation = new BspEditor.Modification.Operations.Mutation.Transform(finalTransform, objectsToTransform);
                        transaction.Add(transformOperation);

                        // Check for texture transform
                        var tl = document.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags();
                        if (dialog.Type == TransformDialog.TransformType.Rotate || dialog.Type == TransformDialog.TransformType.Translate)
                        {
                            if (tl.TextureLock) transaction.Add(new TransformTexturesUniform(finalTransform, objectsToTransform.SelectMany(x => x.FindAll())));
                        }
                        else if (dialog.Type == TransformDialog.TransformType.Scale)
                        {
                            if (tl.TextureScaleLock) transaction.Add(new TransformTexturesScale(finalTransform, objectsToTransform.SelectMany(x => x.FindAll())));
                        }

                        await MapDocumentOperation.Perform(document, transaction);
                    }
                    catch (TransformDialog.CannotScaleByZeroException)
                    {
                        MessageBox.Show(ErrorCannotScaleByZeroMessage, ErrorCannotScaleByZeroTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
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