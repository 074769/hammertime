using Sledge.BspEditor.Tools.DragSelect;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Mutation;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Viewport;
using Sledge.BspEditor.Tools.Draggable;
using Sledge.BspEditor.Tools.Properties;
using Sledge.BspEditor.Tools.Selection.TransformationHandles;
using Sledge.BspEditor.Tools.Vertex.Selection;
using Sledge.BspEditor.Tools.Vertex.Tools;
using Sledge.Common.Shell;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Providers.Texture;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;
using Sledge.Shell.Input;

namespace Sledge.BspEditor.Tools.Vertex
{
	[Export(typeof(ITool))]
	[Export(typeof(IInitialiseHook))]
	[Export]
	[OrderHint("P")]
	[AutoTranslate]
	[DefaultHotkey("Shift+V")]
	public class VertexTool : BaseDraggableTool, IInitialiseHook
	{
		public static readonly Capability VertexToolCapability = Capability.Create("VertexTool");

		private readonly IEnumerable<Lazy<VertexSubtool>> _subTools;

		private readonly VertexSelection _selection;

		[ImportingConstructor]
		public VertexTool(
			[ImportMany] IEnumerable<Lazy<VertexSubtool>> subTools
		)
		{
			_subTools = subTools;

			Usage = ToolUsage.Both;

			_selection = new VertexSelection();
		}
		public override Capability ToolCapability => VertexToolCapability;

		public Task OnInitialise()
		{
			foreach (var st in _subTools.OrderBy(x => x.Value.OrderHint))
			{
				st.Value.Selection = _selection;
				st.Value.Active = Children.Count == 0;
				Children.Add(st.Value);
			}

			return Task.FromResult(false);
		}
		public override Image GetIcon()
		{
			return IconLoader.Load(typeof(Resources), nameof(Resources.Tool_VM));
		}

		public override string GetName()
		{
			return "Vertex Manipulation Tool";
		}

		protected override IEnumerable<Subscription> Subscribe()
		{
			yield return Oy.Subscribe<IDocument>("MapDocument:SelectionChanged", async x =>
			{
				if (x == GetDocument()) await SelectionChanged();
			});
			// Undo/redo (or anything else) can change the solids under the tool: refresh the working copies so the tool never shows or writes back stale geometry
			yield return Oy.Subscribe<Change>("MapDocument:Changed", c =>
			{
				if (c.Document != GetDocument()) return;
				if (_selection.ResetUpdated(c.Updated))
				{
					CurrentSubTool?.Update();
					Invalidate();
				}
			});
			// Undo/redo: commit the working copies and deselect everything first, so the history change never runs under a live selection
			yield return Oy.Subscribe<MapDocument>("MapDocument:BeforeHistoryChange", async d => await ClearForHistoryChange(d));
			yield return Oy.Subscribe<Type>("VertexTool:SetSubTool", t =>
			{
				CurrentSubTool = _subTools.Select(x => x.Value).FirstOrDefault(x => x.GetType() == t);
			});
			yield return Oy.Subscribe<String>("VertexTool:Reset", async _ =>
			{
				var document = GetDocument();
				if (document != null) await _selection.Reset(document);
				CurrentSubTool?.Update();
				Invalidate();
			});
		}

		public override async Task ToolSelected()
		{
			await SelectionChanged();
			var ct = CurrentSubTool;
			if (ct != null) await ct.ToolSelected();
			await base.ToolSelected();
		}

		public override async Task ToolDeselected()
		{
			var ct = CurrentSubTool;
			await ct?.PreToolDeselect();
			var document = GetDocument();
			if (document != null)
			{
				await _selection.Commit(document);
				await _selection.Clear(document);
			}

			if (ct != null) await ct.ToolDeselected();
			await base.ToolDeselected();
		}

		private async Task ClearForHistoryChange(MapDocument document)
		{
			if (!Active || document == null || document != GetDocument()) return;

			await _selection.Commit(document);
			await _selection.Clear(document);
			await MapDocumentOperation.Bypass(document, new Deselect(document.Map.Root.FindAll()));

			var ct = CurrentSubTool;
			if (ct != null) await ct.SelectionChanged();
			Invalidate();
		}

		private async Task SelectionChanged()
		{
			var document = GetDocument();
			if (document != null)
			{
				await _selection.Commit(document);
				await _selection.Update(document);
			}

			var ct = CurrentSubTool;
			if (ct != null) await ct.SelectionChanged();
			Invalidate();
		}

		#region Tool switching

		internal VertexSubtool GetSubTool(System.Type type)
		{
			return Children.OfType<VertexSubtool>().FirstOrDefault(x => x.GetType() == type);
		}

		internal VertexSubtool CurrentSubTool
		{
			get { return Children.OfType<VertexSubtool>().FirstOrDefault(x => x.Active); }
			set
			{
				foreach (var tool in Children.Where(x => x != value && x.Active))
				{
					tool.ToolDeselected();
					tool.Active = false;
				}
				if (value != null)
				{
					value.Active = true;
					value.ToolSelected();
				}

				Oy.Publish("VertexTool:SubToolChanged", value?.GetType());
			}
		}

		#endregion

		private async Task SelectObject(MapDocument document, Solid closestObject)
		{
			// Nothing was clicked, don't change the selection
			if (closestObject == null) return;

			var operation = new Transaction();

			// Ctrl adds to the selection. Ctrl+clicking a selected solid deselects it.

			if (!KeyboardState.Ctrl)
			{
				// Ctrl isn't down, so we want to clear the selection
				operation.Add(new Deselect(document.Selection.Where(x => !ReferenceEquals(x, closestObject)).ToList()));
			}

			if (KeyboardState.Ctrl && closestObject.IsSelected)
			{
				// Ctrl+clicking a selected solid deselects it
				operation.Add(new Deselect(new List<IMapObject> { closestObject }));
			}
			else if (!closestObject.IsSelected)
			{
				// The clicked object isn't selected yet, select it.
				operation.Add(new Select(closestObject));
			}

			if (!operation.IsEmpty)
			{
				// Write back any working-copy edits before the selection moves, so the solid being left
				// is never reset while its edit is still in flight.
				await _selection.Commit(document);
				// Selection changes are not edits: keep them out of undo history.
				await MapDocumentOperation.Bypass(document, operation);
				// Apply the new selection straight away. Waiting for the selection-changed event leaves a frame
				// where the previous solid is still drawn and the clicked one isn't, which shows up as a flicker.
				await SelectionChanged();
			}
		}

		#region 3D interaction

		// Ctrl+drag paint state (3D)
		private bool _paintSelecting;
		private bool _paintAdds;
		private Solid _paintLast;

		protected override void MouseMove(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
		{
			if (_paintSelecting)
			{
				if (!KeyboardState.Ctrl) _paintSelecting = false;
				else PaintSolidAt(document, camera, e);
			}
			base.MouseMove(document, viewport, camera, e);
		}

		protected override void MouseUp(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
		{
			_paintSelecting = false;
			_paintLast = null;
			base.MouseUp(document, viewport, camera, e);
		}

		private void PaintSolidAt(MapDocument document, PerspectiveCamera camera, ViewportEvent e)
		{
			var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
			var ray = new Line(start, end);
			var hit = document.Map.Root.GetIntersectionsForVisibleObjects(ray).FirstOrDefault(x => x.Object is Solid);
			if (hit == null) return;

			// Hits near an edge are ignored so the drag doesn't flip between neighbouring solids
			if (FaceEdgeHitTest.IsOnObjectEdge(camera, hit.Object, hit.Intersection)) return;

			var solid = (Solid) hit.Object;
			if (ReferenceEquals(solid, _paintLast)) return;
			_paintLast = solid;
			_ = SetSolidSelected(document, solid, _paintAdds);
		}

		private async Task SetSolidSelected(MapDocument document, Solid solid, bool add)
		{
			var operation = new Transaction();
			if (add && !solid.IsSelected) operation.Add(new Select(solid));
			else if (!add && solid.IsSelected) operation.Add(new Deselect(new List<IMapObject> { solid }));
			if (operation.IsEmpty) return;

			await _selection.Commit(document);
			await MapDocumentOperation.Bypass(document, operation);
			await SelectionChanged();
		}

		/// <summary>
		/// When the mouse is pressed in the 3D view, we want to select the clicked object.
		/// </summary>
		/// <param name="document"></param>
		/// <param name="viewport">The viewport that was clicked</param>
		/// <param name="camera"></param>
		/// <param name="e">The click event</param>
		protected override void MouseDown(MapDocument document, MapViewport viewport, PerspectiveCamera camera, ViewportEvent e)
		{
			if (e.Button != MouseButtons.Left) return;
			// First, get the ray that is cast from the clicked point along the viewport frustrum
			var (start, end) = camera.CastRayFromScreen(new Vector3(e.X, e.Y, 0));
			var ray = new Line(start, end);

			// Grab the closest solid that intersects with the ray
			var hit = document.Map.Root.GetIntersectionsForVisibleObjects(ray)
				.FirstOrDefault(x => x.Object is Solid);
			var closestObject = hit?.Object as Solid;

			// Ctrl+press starts a paint drag: the first click toggles this solid, dragging does the same to each solid
			// the cursor passes over. A press on a solid's edge still clicks, but starts no drag.
			_paintSelecting = false;
			if (closestObject != null && KeyboardState.Ctrl)
			{
				_paintSelecting = !FaceEdgeHitTest.IsOnObjectEdge(camera, closestObject, hit.Intersection);
				_paintAdds = !closestObject.IsSelected;
				_paintLast = closestObject;
			}

			_ = SelectObject(document, closestObject);
		}

		#endregion

		#region 2D interaction

		private IEnumerable<IMapObject> GetLineIntersections(MapDocument document, Box box)
		{
			return document.Map.Root.Collect(
				x => x is Root || (x.BoundingBox != null && x.BoundingBox.IntersectsWith(box)),
				x => x.Hierarchy.Parent != null && !x.Hierarchy.HasChildren && x is Solid && x.GetPolygons().Any(p => p.GetLines().Any(box.IntersectsWith))
			);
		}

		private IMapObject SelectionTest(MapDocument document, OrthographicCamera camera, ViewportEvent e)
		{
			// Create a box to represent the click, with a tolerance level
			var unused = camera.GetUnusedCoordinate(new Vector3(100000, 100000, 100000));
			var tolerance = 4 / camera.Zoom; // Selection tolerance of four pixels
			var used = camera.Expand(new Vector3(tolerance, tolerance, 0));
			var add = used + unused;
			var click = camera.ScreenToWorld(e.X, e.Y);
			var box = new Box(click - add, click + add);
			return GetLineIntersections(document, box).FirstOrDefault();
		}

		protected override void MouseClick(MapDocument document, MapViewport viewport, OrthographicCamera camera, ViewportEvent e)
		{
			// Select the clicked object on mouse *click* rather than on mouse *down*.
			// Drawing a vertex selection box is a left-drag, so MouseClick never fires while a box is
			// being drawn and the object being edited keeps its selection. This only affects the vertex
			// tool, which is the only tool that reaches this handler.
			// Get the first element that intersects with the box, selecting or deselecting as needed
			var closestObject = SelectionTest(document, camera, e) as Solid;
			_ = SelectObject(document, closestObject);
		}

		#endregion

		protected override void Render(MapDocument document, BufferBuilder builder, ResourceCollector resourceCollector)
		{
			base.Render(document, builder, resourceCollector);

			// Force this work to happen on a new thread so waiting on it won't block the context
			Task.Run(async () =>
			{
				foreach (var obj in _selection)
				{
					await Convert(builder, document, obj.Copy, resourceCollector);
				}
			}).Wait();
		}

		public void Invalidate()
		{
			Oy.Publish("VertexTool:Updated", _selection);
		}

		private async Task Convert(BufferBuilder builder, MapDocument document, MutableSolid solid, ResourceCollector resourceCollector)
		{
			var displayFlags = document.Map.Data.GetOne<DisplayFlags>();
			var hideNull = displayFlags?.HideNullTextures == true;

			var faces = solid.Faces.Where(x => x.Vertices.Count > 2).ToList();

			// Pack the vertices like this [ f1v1 ... f1vn ] ... [ fnv1 ... fnvn ]
			var numVertices = (uint)faces.Sum(x => x.Vertices.Count);

			// Pack the indices like this [ solid1 ... solidn ] [ wireframe1 ... wireframe n ]
			var numSolidIndices = (uint)faces.Sum(x => (x.Vertices.Count - 2) * 3);
			var numWireframeIndices = numVertices * 2;

			var points = new VertexStandard[numVertices];
			var indices = new uint[numSolidIndices + numWireframeIndices];

			var tint = Color.FromArgb(128, 255, 128).ToVector4();

			var tc = await document.Environment.GetTextureCollection();

			var vi = 0u;
			var si = 0u;
			var wi = numSolidIndices;
			foreach (var face in faces)
			{
				var opacity = tc.GetOpacity(face.Texture.Name);
				var t = await tc.GetTextureItem(face.Texture.Name);
				var w = t?.Width ?? 0;
				var h = t?.Height ?? 0;

				var tintModifier = new Vector4(1, 1, 1, opacity);

				var offs = vi;
				var numFaceVerts = (uint)face.Vertices.Count;

				var textureCoords = face.GetTextureCoordinates(w, h).ToList();

				var normal = face.Plane.Normal;
				for (var i = 0; i < face.Vertices.Count; i++)
				{
					var v = face.Vertices[i];
					points[vi++] = new VertexStandard
					{
						Position = v.Position,
						Colour = Vector4.One,
						Normal = normal,
						Texture = new Vector2(textureCoords[i].Item2, textureCoords[i].Item3),
						Tint = tint * tintModifier,
						Flags = VertexFlags.None
					};
				}

				// Triangles - [0 1 2]  ... [0 n-1 n]
				for (uint i = 2; i < numFaceVerts; i++)
				{
					indices[si++] = offs;
					indices[si++] = offs + i - 1;
					indices[si++] = offs + i;
				}

				// Lines - [0 1] ... [n-1 n] [n 0]
				for (uint i = 0; i < numFaceVerts; i++)
				{
					indices[wi++] = offs + i;
					indices[wi++] = offs + (i == numFaceVerts - 1 ? 0 : i + 1);
				}
			}

			var groups = new List<BufferGroup>();

			uint texOffset = 0;
			foreach (var f in faces)
			{
				var texInd = (uint)(f.Vertices.Count - 2) * 3;

				if (hideNull && tc.IsNullTexture(f.Texture.Name))
				{
					texOffset += texInd;
					continue;
				}

				var opacity = tc.GetOpacity(f.Texture.Name);
				var t = await tc.GetTextureItem(f.Texture.Name);
				var transparent = opacity < 0.95f || t?.Flags.HasFlag(TextureFlags.Transparent) == true;

				var texture = t == null ? string.Empty : $"{document.Environment.ID}::{f.Texture.Name}";

				groups.Add(transparent
					? new BufferGroup(PipelineType.TexturedAlpha, CameraType.Perspective, f.Origin, texture, texOffset, texInd)
					: new BufferGroup(PipelineType.TexturedOpaque, CameraType.Perspective, texture, texOffset, texInd)
				);

				texOffset += texInd;

				if (t != null) resourceCollector.RequireTexture(t.Name);
			}

			groups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Both, numSolidIndices, numWireframeIndices));

			builder.Append(points, indices, groups);
		}
	}
}
