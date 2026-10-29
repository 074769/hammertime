using System.ComponentModel.Composition;
using System.Runtime.Serialization;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Transport;
using Sledge.DataStructures.Geometric;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Rendering.ChangeHandlers
{
	/// <summary>
	/// Marks an env_beam / env_laser entity as having a beam preview, and holds the renderable that draws it.
	/// The renderable's endpoints and settings are refreshed by <see cref="EntityBeamChangeHandler"/> whenever the map changes.
	/// </summary>
	public class EntityBeam : IMapObjectData
	{
		/// <summary>The sprite used as the beam texture.</summary>
		public string Name { get; }

		public BeamRenderable Renderable { get; }

		public EntityBeam(string name, BeamRenderable renderable)
		{
			Name = name;
			Renderable = renderable;
		}

		public EntityBeam(SerialisedObject obj)
		{
			Name = obj.Get<string>("Name");
			Renderable = null;
		}

		[Export(typeof(IMapElementFormatter))]
		public class EntityBeamFormatter : StandardMapElementFormatter<EntityBeam> { }

		public void GetObjectData(SerializationInfo info, StreamingContext context)
		{
			info.AddValue("Name", Name);
		}

		public IMapElement Copy(UniqueNumberGenerator numberGenerator)
		{
			return Clone();
		}

		public IMapElement Clone()
		{
			// The renderable is not shared; the change handler creates a new one for the copy.
			return new EntityBeam(Name, null);
		}

		public SerialisedObject ToSerialisedObject()
		{
			var so = new SerialisedObject(nameof(EntityBeam));
			so.Set(nameof(Name), Name);
			return so;
		}
	}
}
