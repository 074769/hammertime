using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.BspEditor.Rendering.Selection;
using Sledge.Common.Shell.Settings;
using Sledge.DataStructures.Geometric;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
	[Export(typeof(IMapObjectGroupSceneConverter))]
	[Export(typeof(ISettingsContainer))]
	public class CenterHandlesConverter : IMapObjectGroupSceneConverter, ISettingsContainer
	{
		private readonly EngineInterface _engine;

		[ImportingConstructor]
		public CenterHandlesConverter([Import] Lazy<EngineInterface> engine)
		{
			_engine = engine.Value;
			_engine.UploadTexture(CenterHandleTextureDataSource.Name, HandleDataSource.Width, HandleDataSource.Height, HandleDataSource.Data, HandleDataSource.SampleType);
		}

		// Settings

		[Setting("DrawCenterHandles")] private bool _drawCenterHandles = true;
		// [Setting("CenterHandlesActiveViewportOnly")] private bool _centerHandlesActiveViewportOnly = false;

		/// <summary>
		/// The unique name of the settings container.
		/// </summary>
		public const string ContainerName = "Sledge.BspEditor.Rendering.Converters.CenterHandlesConverter";

		string ISettingsContainer.Name => ContainerName;
		public bool ValuesLoaded { get; private set; } = false;

		IEnumerable<SettingKey> ISettingsContainer.GetKeys()
		{
			yield return new SettingKey("Rendering", "DrawCenterHandles", typeof(bool));
			//yield return new SettingKey("Rendering", "CenterHandlesActiveViewportOnly", typeof(bool));
		}

		void ISettingsContainer.LoadValues(ISettingsStore store)
		{
			store.LoadInstance(this);
			ValuesLoaded = true;
		}

		void ISettingsContainer.StoreValues(ISettingsStore store)
		{
			store.StoreInstance(this);
		}

		// Converter

		public MapObjectSceneConverterPriority Priority => MapObjectSceneConverterPriority.DefaultMedium;

		public Task Convert(BufferBuilder builder, MapDocument document, IEnumerable<IMapObject> objects, ResourceCollector resourceCollector)
		{
			if (!_drawCenterHandles) return Task.CompletedTask;

			var objs = (
				from mo in objects
				where mo is Solid || (mo is Entity && !mo.Hierarchy.HasChildren)
				where !mo.Data.OfType<IObjectVisibility>().Any(x => x.IsHidden)
				select mo
			).ToList();

			// Objects with a slot get a handle for each selection look and never need rebuilding for selection.
			// Objects without a slot (the slot table is full) bake their selection into their handle, as before.
			var slottedVerts = new List<VertexStandard>();
			var slottedGroups = new List<BufferGroup>();
			var bakedObjs = new List<IMapObject>();
			foreach (var mo in objs)
			{
				var slot = SelectionSlots.SlotFor(document, mo);
				if (slot < 0)
				{
					bakedObjs.Add(mo);
					continue;
				}

				var centre = mo.BoundingBox.Center;
				var normalColour = (mo.Data.GetOne<ObjectColor>()?.Color ?? Color.White).ToVector4();
				var red = Color.Red.ToVector4();
				AddHandle(slottedVerts, slottedGroups, slot, SelectionFilter.WhenUnselected, centre, normalColour, VertexFlags.None);
				AddHandle(slottedVerts, slottedGroups, slot, SelectionFilter.WhenSelected, centre, red, VertexFlags.None);
				AddHandle(slottedVerts, slottedGroups, slot, SelectionFilter.WhenSelected, centre, red, VertexFlags.SelectiveTransformed);
			}

			if (slottedVerts.Count > 0)
			{
				builder.Append(slottedVerts, Enumerable.Range(0, slottedVerts.Count).Select(x => (uint) x), slottedGroups);
			}

			objs = bakedObjs;
			if (objs.Count == 0) return Task.CompletedTask;

			var verts = (
				// Current centers
				from mo in objs
				let color = mo.IsSelected ? Color.Red : mo.Data.GetOne<ObjectColor>()?.Color ?? Color.White
				select new VertexStandard
				{
					Position = mo.BoundingBox.Center,
					Normal = new Vector3(9, 9, 0),
					Colour = color.ToVector4(),
					Tint = Vector4.One
				}
			).Union(
				// Selective transformed centers
				from mo in objs
				where mo.IsSelected
				let color = Color.Red
				select new VertexStandard
				{
					Position = mo.BoundingBox.Center,
					Normal = new Vector3(9, 9, 0),
					Colour = color.ToVector4(),
					Tint = Vector4.One,
					Flags = VertexFlags.SelectiveTransformed
				}
			).ToList();

			builder.Append(verts, Enumerable.Range(0, verts.Count).Select(x => (uint)x), new[]
			{
				new BufferGroup(PipelineType.BillboardOpaque, CameraType.Orthographic, CenterHandleTextureDataSource.Name, 0, (uint) verts.Count)
			});

			return Task.CompletedTask;
		}

		private static void AddHandle(List<VertexStandard> verts, List<BufferGroup> groups, int slot, SelectionFilter filter, Vector3 centre, Vector4 colour, VertexFlags flags)
		{
			verts.Add(new VertexStandard
			{
				Position = centre,
				Normal = new Vector3(9, 9, 0),
				Colour = colour,
				Tint = Vector4.One,
				Flags = flags
			});
			groups.Add(new BufferGroup(PipelineType.BillboardOpaque, CameraType.Orthographic, CenterHandleTextureDataSource.Name, (uint) (verts.Count - 1), 1)
				.WithSelection(slot, filter));
		}

		private static readonly CenterHandleTextureDataSource HandleDataSource = new CenterHandleTextureDataSource();
		private class CenterHandleTextureDataSource
		{
			public const string Name = "DefaultSolidConverter::CenterHandle::X";
			public byte[] Data { get; }
			public TextureSampleType SampleType => TextureSampleType.Point;
			public int Width => 9;
			public int Height => 9;

			public CenterHandleTextureDataSource()
			{
				using (var img = new Bitmap(Width, Height))
				{
					using (var g = Graphics.FromImage(img))
					{
						g.FillRectangle(Brushes.Transparent, 0, 0, img.Width, img.Height);
						g.DrawLine(Pens.White, 1, 1, img.Width - 2, img.Height - 2);
						g.DrawLine(Pens.White, img.Width - 2, 1, 1, img.Height - 2);
					}
					var lb = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
					Data = new byte[lb.Stride * lb.Height];
					Marshal.Copy(lb.Scan0, Data, 0, Data.Length);
					img.UnlockBits(lb);
				}
			}
		}
	}
}