using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.ChangeHandlers;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
	/// <summary>
	/// Adds the beam renderable of env_beam / env_laser entities to the scene. The entity's normal box is still drawn,
	/// so this does not stop other converters.
	/// </summary>
	[Export(typeof(IMapObjectSceneConverter))]
	public class EntityBeamConverter : IMapObjectSceneConverter
	{
		public MapObjectSceneConverterPriority Priority => MapObjectSceneConverterPriority.DefaultLow;

		public bool ShouldStopProcessing(MapDocument document, IMapObject obj)
		{
			return false;
		}

		public bool Supports(IMapObject obj)
		{
			return obj is Entity e && e.Data.GetOne<EntityBeam>()?.Renderable != null;
		}

		public Task Convert(BufferBuilder builder, MapDocument document, IMapObject obj, ResourceCollector resourceCollector)
		{
			if (obj is Entity entity)
			{
				var beam = entity.Data.GetOne<EntityBeam>();
				if (beam?.Renderable != null)
				{
					resourceCollector.AddRenderables(new[] { beam.Renderable });
				}
			}
			return Task.CompletedTask;
		}
	}
}
