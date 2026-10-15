using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Components;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Properties;
using Sledge.DataStructures.Geometric;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;

namespace Sledge.BspEditor.Commands.Clipboard
{
	[AutoTranslate]
	[Export(typeof(ICommand))]
	[CommandID("BspEditor:Edit:Paste")]
	[DefaultHotkey("Ctrl+V")]
	[MenuItem("Edit", "", "Clipboard", "F")]
	[MenuImage(typeof(Resources), nameof(Resources.Menu_Paste))]
	public class Paste : BaseCommand
	{
		private readonly Lazy<ClipboardManager> _clipboard;
		private readonly Random _random;

		public override string Name { get; set; } = "Paste";
		public override string Details { get; set; } = "Paste the current clipboard contents";
		private MapDocument _document;
		private ViewportCursorRay _cursorRay;

		[ImportingConstructor]
		public Paste([Import] Lazy<ClipboardManager> clipboard)
		{
			_clipboard = clipboard;
			_random = new Random();
			Oy.Subscribe<string>("BspEditor:Edit:PasteFromView", async (arg) => await PasteClipboard(arg));
			// Keep track of where the cursor last was in a viewport, so paste can drop objects under it
			Oy.Subscribe<ViewportCursorRay>("MapDocument:ViewportCursorRay:UpdateValue", ray => { _cursorRay = ray; });
		}

		protected override async Task Invoke(MapDocument document, CommandParameters parameters)
		{
			_document = document;
			var moveLock = parameters.Get<string>("AxisLock", null);
			await PasteClipboard(moveLock);
		}

		private ICollection<IMapObject> RetriveNonGroupedObjectsRecursively(IEnumerable<IMapObject> objects)
		{
			var newObjects = new List<IMapObject>();

			foreach (var d in objects)
			{
				if (d is Group group)
				{
					newObjects.AddRange(RetriveNonGroupedObjectsRecursively(group.Hierarchy));
				}
				else
				{
					newObjects.Add(d);
				}
			}
			return newObjects;
		}
		private async Task PasteClipboard(string arg)
		{
			if (_clipboard.Value.CanPaste())
			{
				// Work out the grid step size, used to nudge pasted objects
				// when there is no known cursor position to paste at
				var step = Vector3.One * 16;

				// If there's a grid, use the grid spacing instead of the box dimensions
				var grid = _document.Map.Data.GetOne<GridData>();
				if (grid?.Grid != null && grid.Grid.Spacing > 1)
				{
					step = grid.Grid.AddStep(Vector3.Zero, Vector3.One);
				}
				//(((document.Control as MapDocumentControlHost).ActiveControl as MapDocumentContainer).ActiveControl as  Sledge.Rendering.Viewports.Viewport)
				// Get the pasted values, moving objects that have an id already in the map
				//var content = _clipboard.Value.GetPastedContent(document, (d, o) => CopyAndMove(d, o, step)).ToList();
				//var moveLock = parameters.Get<string>("AxisLock", null);
				var moveLock = arg;

				var content = _clipboard.Value.GetPastedContent(_document, (d, o) => Copy(d, o), true).ToList();

				// Drop the pasted objects under the cursor when we know where it is,
				// otherwise fall back to nudging them by a random grid step
				var translation = Matrix4x4.CreateTranslation(GetPasteOffset(content, grid, step, moveLock));

				var newcontent = RetriveNonGroupedObjectsRecursively(content);

				var itemNames = _document.Map.Root
								.Find(x => x is Entity)
								.OfType<Entity>()
								.Select(x => (x.EntityData.Properties.ContainsKey("targetname") ? x.EntityData.Properties["targetname"] : null))
								.Where(x => !String.IsNullOrEmpty(x))
								.Distinct()
								.ToArray();

				var entities = newcontent.Select(x => x as Entity).Where(x => x != null);
				var newnames = new List<string>();

				foreach (var entity in entities)
				{
					if (entity.EntityData.Properties.ContainsKey("targetname") && !String.IsNullOrEmpty(entity.EntityData.Properties["targetname"]))
					{
						var originalName = entity.EntityData.Properties["targetname"];
						var itemNamesFiltered = itemNames.Where(name => !String.IsNullOrEmpty(name) && name.Contains(originalName));
						string newName = originalName;
						int i = 1;
						while (itemNamesFiltered.Contains(newName + $"_{i}"))
						{
							i++;
						}
						newName = newName + $"_{i}";
						newnames.Add(newName);
						entity.EntityData.Properties["targetname"] = newName;
					}
				}
				foreach (var entity in entities)
				{
					if (entity.EntityData.Properties.ContainsKey("target") && !String.IsNullOrEmpty(entity.EntityData.Properties["target"]))
					{

						var originalValue = entity.EntityData.Properties["target"];
						var newvalue = newnames.FirstOrDefault(x => x.Contains(originalValue));
						//We dont want to change target if there was no target copied
						if (!String.IsNullOrEmpty(newvalue))
							entity.EntityData.Properties["target"] = newvalue;
					}
				}

				// The copies come out with the selection state they were copied with, which doesn't match what is selected here: start them
				// unselected, then select them with everything inside them, the way a click on them would
				foreach (var o in content.SelectMany(x => x.FindAll())) o.IsSelected = false;

				var transaction = new Transaction(
				new Deselect(_document.Selection),
				new Attach(_document.Map.Root.ID, content),
				new Transform(translation, content),
				new TransformTexturesUniform(translation, content.SelectMany(x => x.FindAll())),
				new Select(content.SelectMany(x => x.FindAll()))
			);

				await MapDocumentOperation.Perform(_document, transaction);

				// Center the 2D views on the pasted objects so they are immediately visible.
				// The 3D camera stays where it is: pasting is placed relative to it, so it shouldn't jump.
				if (!_document.Selection.IsEmpty)
				{
					var selectionBox = _document.Selection.GetSelectionBoundingBox();
					await Oy.Publish("MapDocument:Viewport:Focus2D", selectionBox);
				}
			}
		}

		/// <summary>
		/// Work out how far the pasted objects should move. When the last known cursor
		/// position is available the objects drop under the cursor (keeping their original
		/// depth or distance from the camera); otherwise they get a random grid-sized
		/// nudge so they don't end up on top of the originals.
		/// </summary>
		private Vector3 GetPasteOffset(ICollection<IMapObject> content, GridData grid, Vector3 step, string moveLock)
		{
			var boxes = content.Select(x => x.BoundingBox).Where(x => x != null).ToList();
			Box contentBox = boxes.Count > 0 ? new Box(boxes) : null;

			// Pasting from the 3D view: rest the objects on a surface instead
			if (contentBox != null && _cursorRay?.CameraPosition != null && (moveLock == null || moveLock == "3D"))
			{
				return GetPasteOffset3D(contentBox, grid);
			}

			Vector3 offset;
			var dropPoint = _cursorRay != null && contentBox != null
				? _cursorRay.ClosestPoint(contentBox.Center)
				: (Vector3?) null;

			if (dropPoint != null)
			{
				// Snap the drop point to the grid so the pasted objects stay aligned
				if (grid?.Grid != null && grid.SnapToGrid) dropPoint = grid.Grid.Snap(dropPoint.Value);
				offset = dropPoint.Value - contentBox.Center;
			}
			else
			{
				// No cursor position known: offset by a random number of grid steps
				offset = new Vector3(_random.Next(-4, 5) * step.X, _random.Next(-4, 5) * step.Y, 0);
			}

			// Don't move along the view's depth axis when pasting from a 2D view
			if (moveLock == "X") offset.X = 0;
			else if (moveLock == "Y") offset.Y = 0;
			else if (moveLock == "Z") offset.Z = 0;

			return offset;
		}

		/// <summary>
		/// Pasting in the 3D view works like putting a teacup on a table:
		/// - If the cursor is over a brush, the objects rest on the face under the cursor, snapped to the grid along that face.
		/// - Otherwise, if a brush is selected, they rest on the face of the selected brush that the camera sees.
		/// - Otherwise they drop under the cursor at their original distance from the camera.
		/// Either way they are lifted out of any other brush they would end up inside.
		/// </summary>
		private Vector3 GetPasteOffset3D(Box contentBox, GridData grid)
		{
			const float rayLength = 100000f;

			var ray = _cursorRay;
			var camera = ray.CameraPosition.Value;
			var center = contentBox.Center;
			var snap = grid?.Grid != null && grid.SnapToGrid;

			var solids = _document.Map.Root.Find(x => x is Solid).OfType<Solid>().ToList();

			// 1. The brush under the cursor
			var hit = PasteSurface.Raycast(solids.Where(x => !x.IsHidden()), ray.Origin, ray.Direction, rayLength);

			// 2. The selected brush, as the camera sees it
			if (hit == null)
			{
				var selected = _document.Selection.SelectMany(x => x.FindAll()).OfType<Solid>()
					.Where(x => x.BoundingBox != null)
					.Distinct()
					.OrderBy(x => (x.BoundingBox.Center - camera).LengthSquared())
					.ToList();

				foreach (var solid in selected)
				{
					var toSolid = solid.BoundingBox.Center - camera;
					if (toSolid.LengthSquared() < 0.001f) continue;

					var length = toSolid.Length() + (solid.BoundingBox.End - solid.BoundingBox.Start).Length();
					hit = PasteSurface.Raycast(new[] { solid }, camera, Vector3.Normalize(toSolid), length);
					if (hit != null) break;
				}
			}

			Vector3 target;
			Vector3 up;
			Solid resting = null;

			if (hit != null)
			{
				var point = snap ? PasteSurface.SnapOnPlane(hit.Point, hit.Normal, grid.Grid.Snap) : hit.Point;
				target = point + hit.Normal * PasteSurface.RestingDistance(contentBox, hit.Normal);
				up = hit.Normal;
				resting = hit.Solid;
			}
			else
			{
				// Nothing to rest on: under the cursor, at the original distance from the camera
				target = ray.ClosestPoint(center);
				if (snap) target = grid.Grid.Snap(target);
				up = Vector3.UnitZ;
			}

			return PasteSurface.LiftClear(contentBox, target - center, up, solids, resting);
		}

		private IMapObject Copy(MapDocument document, IMapObject o)
		{
			return (IMapObject)o.Copy(document.Map.NumberGenerator);
		}


		private IMapObject CopyAndMove(MapDocument document, IMapObject o, Vector3 step)
		{
			var copy = Copy(document, o);
			//copy.Transform(Matrix4x4.CreateTranslation(_random.Next(-4, 5) * step.X, _random.Next(-4, 5) * step.Y, _random.Next(-4, 5) * step.Z));

			return copy;
		}
		private IMapObject CopyAndMove(MapDocument document, IMapObject o, Matrix4x4 translation)
		{
			var copy = Copy(document, o);
			//copy.Transform(translation);

			return copy;
		}
	}
}