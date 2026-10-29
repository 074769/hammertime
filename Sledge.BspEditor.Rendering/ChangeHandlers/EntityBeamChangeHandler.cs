using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Environment;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.ChangeHandling;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Converters;
using Sledge.BspEditor.Rendering.Resources;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Rendering.ChangeHandlers
{
	/// <summary>
	/// Keeps the 3D beam preview of env_beam and env_laser entities up to date.
	///
	/// A beam's endpoints are other entities, so the endpoints are resolved again for every beam after every change:
	/// moving or renaming an endpoint must move the beam even though the beam entity itself did not change.
	/// Settings missing from the entity fall back to the FGD defaults.
	/// </summary>
	[Export(typeof(IMapDocumentChangeHandler))]
	public class EntityBeamChangeHandler : IMapDocumentChangeHandler
	{
		// Matches the FGD flag values
		private const int FlagRing = 8;
		private const int FlagShadeIn = 128;
		private const int FlagShadeOut = 256;

		// One beam is drawn for every start/end combination, but never more than this many per entity
		private const int MaxPairsPerBeam = 64;

		public string OrderHint => "N";

		private readonly Lazy<ResourceCollection> _resourceCollection;

		[ImportingConstructor]
		public EntityBeamChangeHandler([Import] Lazy<ResourceCollection> resourceCollection)
		{
			_resourceCollection = resourceCollection;
		}

		public async Task Changed(Change change)
		{
			var env = change.Document.Environment;

			// Dispose the renderables of removed beams
			foreach (var rem in change.Removed)
			{
				var removed = rem.Data.GetOne<EntityBeam>();
				if (removed == null) continue;
				_resourceCollection.Value.DestroyBeamRenderable(env, removed.Renderable);
				rem.Data.Remove(removed);
			}

			// Moving things around changes nothing about beams unless an endpoint is an entity that was shown or hidden
			// Visibility-only changes can still hide an endpoint, but the beam is drawn regardless (always show).
			if (VisibilityOnlyFastPath.IsActive(change)) return;

			var all = change.Document.Map.Root.FindAll().OfType<Entity>().ToList();
			var beams = all.Where(IsBeamEntity).ToList();
			if (beams.Count == 0) return;

			var tc = await env.GetTextureCollection();
			var byName = BuildNameIndex(all);

			foreach (var beam in beams)
			{
				var existing = beam.Data.GetOne<EntityBeam>();
				var textureName = (beam.EntityData.Get("texture", "sprites/laserbeam.spr") ?? "").Trim();

				if (string.IsNullOrEmpty(textureName) || !tc.HasTexture(textureName))
				{
					// No usable beam sprite, so no beam
					if (existing != null)
					{
						_resourceCollection.Value.DestroyBeamRenderable(env, existing.Renderable);
						beam.Data.Remove(existing);
						beam.DescendantsChanged();
					}
					continue;
				}

				if (existing == null || existing.Renderable == null || !string.Equals(existing.Name, textureName, StringComparison.InvariantCultureIgnoreCase))
				{
					var renderable = await _resourceCollection.Value.CreateBeamRenderable(env, textureName);
					if (existing != null) _resourceCollection.Value.DestroyBeamRenderable(env, existing.Renderable);

					if (renderable == null)
					{
						if (existing != null) beam.Data.Remove(existing);
						continue;
					}

					existing = new EntityBeam(textureName, renderable);
					beam.Data.Replace(existing);
					beam.DescendantsChanged();
				}

				Apply(beam, existing.Renderable, byName, env);
			}
		}

		private static bool IsBeamEntity(Entity e)
		{
			var name = e.EntityData?.Name;
			return string.Equals(name, "env_beam", StringComparison.InvariantCultureIgnoreCase)
			       || string.Equals(name, "env_laser", StringComparison.InvariantCultureIgnoreCase);
		}

		private static Dictionary<string, List<Entity>> BuildNameIndex(IEnumerable<Entity> entities)
		{
			// GoldSrc matches targetnames exactly (case sensitive)
			var index = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
			foreach (var e in entities)
			{
				var name = e.EntityData?.Get("targetname", "");
				if (string.IsNullOrWhiteSpace(name)) continue;
				if (!index.TryGetValue(name, out var list))
				{
					list = new List<Entity>();
					index[name] = list;
				}
				list.Add(e);
			}
			return index;
		}

		private static void Apply(Entity beam, BeamRenderable renderable, Dictionary<string, List<Entity>> byName, IEnvironment env)
		{
			var data = beam.EntityData;
			var isLaser = string.Equals(data.Name, "env_laser", StringComparison.InvariantCultureIgnoreCase);

			List<Vector3> starts;
			List<Vector3> ends;
			if (isLaser)
			{
				// A laser starts at itself and aims at its target
				starts = new List<Vector3> { GetPosition(beam) };
				ends = Resolve(byName, data.Get("LaserTarget", ""));
			}
			else
			{
				starts = Resolve(byName, data.Get("LightningStart", ""));
				ends = Resolve(byName, data.Get("LightningEnd", ""));
			}

			// A beam with a missing endpoint is hidden
			var pairs = new List<BeamPair>();
			foreach (var s in starts)
			{
				foreach (var e in ends)
				{
					if (pairs.Count >= MaxPairsPerBeam) break;
					pairs.Add(new BeamPair(s, e));
				}
			}
			renderable.SetPairs(pairs);

			var flags = (int) data.Get<float>("spawnflags", 0);
			renderable.Width = data.Get<float>("BoltWidth", 20);
			renderable.Noise = data.Get<float>("NoiseAmplitude", 0);
			renderable.ScrollRate = data.Get<float>("TextureScroll", 35);
			renderable.FrameRate = data.Get<float>("framerate", 0);
			renderable.FrameStart = data.Get<float>("framestart", 0);
			renderable.Brightness = Math.Max(0, Math.Min(255, data.Get<float>("renderamt", 100))) / 255f;
			renderable.Colour = RenderColorProfile.ResolveTint(data.GetVector3("rendercolor"), env);
			renderable.Ring = (flags & FlagRing) != 0;

			// The game applies only one of the two shade flags, start wins
			renderable.ShadeIn = (flags & FlagShadeIn) != 0;
			renderable.ShadeOut = !renderable.ShadeIn && (flags & FlagShadeOut) != 0;
		}

		private static List<Vector3> Resolve(Dictionary<string, List<Entity>> byName, string name)
		{
			var result = new List<Vector3>();
			if (string.IsNullOrWhiteSpace(name)) return result;
			if (!byName.TryGetValue(name.Trim(), out var matches)) return result;
			result.AddRange(matches.Select(GetPosition));
			return result;
		}

		/// <summary>
		/// Where a beam attaches to an entity. In the game that is the entity's origin, which for a brush entity is the
		/// centre of its origin brush. Without an origin brush this falls back to the centre of the entity's bounds.
		/// </summary>
		private static Vector3 GetPosition(Entity entity)
		{
			if (!entity.Hierarchy.HasChildren)
			{
				var origin = entity.Data.GetOne<Origin>();
				if (origin != null) return origin.Location;
			}

			var originBrush = entity.FindAll().OfType<Solid>()
				.FirstOrDefault(s => s.Faces.Any() && s.Faces.All(f => string.Equals(f.Texture.Name, "origin", StringComparison.InvariantCultureIgnoreCase)));
			if (originBrush?.BoundingBox != null) return originBrush.BoundingBox.Center;

			return entity.BoundingBox?.Center ?? Vector3.Zero;
		}
	}
}
