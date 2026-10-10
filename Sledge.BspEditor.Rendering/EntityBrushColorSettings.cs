using System.Collections.Generic;
using System.ComponentModel.Composition;
using System;
using System.Drawing;
using System.Linq;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// Settings for the colour of point entities, world brushes and brush entities in the 2D viewport,
    /// plus per-entity-name colour overrides.
    /// </summary>
    [Export(typeof(ISettingsContainer))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class EntityBrushColorSettings : ISettingsContainer
    {
        /// <summary>
        /// The colour used for point entities (entities without solid geometry) in the 2D viewport.
        /// </summary>
        [Setting]
        public static Color PointEntityColour { get; set; } = Color.Green;

        /// <summary>
        /// The colour used for world brushes (map geometry) in the 2D viewport.
        /// </summary>
        [Setting]
        public static Color WorldBrushColour { get; set; } = Color.Green;

        /// <summary>
        /// The colour used for brush entities (entities with solid geometry) in the 2D viewport.
        /// </summary>
        [Setting]
        public static Color BrushEntityColour { get; set; } = Color.Yellow;

        /// <summary>
        /// Per-entity-name colour overrides.
        /// </summary>
        [Setting]
        public static List<EntityColourOverride> CustomEntityColours { get; set; } = new List<EntityColourOverride>();

        /// <summary>
        /// Resolves the wireframe colour for a map object from these settings: a per-class override first,
        /// then the point entity, brush entity or world brush colour. Returns <paramref name="fallback"/>
        /// for objects these settings don't cover (groups, etc).
        /// </summary>
        public static Color Resolve(IMapObject obj, Color fallback)
        {
            var entity = obj as Entity ?? obj.FindClosestParent(x => x is Entity) as Entity;
            if (entity != null)
            {
                var name = entity.EntityData?.Name?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    var match = CustomEntityColours?.FirstOrDefault(o => o != null
                        && !string.IsNullOrWhiteSpace(o.EntityName)
                        && string.Equals(o.EntityName.Trim(), name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return Opaque(match.Colour);
                }
                return Opaque(entity.Hierarchy.HasChildren ? BrushEntityColour : PointEntityColour);
            }
            return obj is Solid ? Opaque(WorldBrushColour) : fallback;
        }

        private static Color Opaque(Color c)
        {
            return Color.FromArgb(255, c.R, c.G, c.B);
        }

        /// <summary>
        /// The unique name of the settings container.
        /// </summary>
        public string Name => "Sledge.BspEditor.Rendering.EntityBrushColor";

        /// <summary>
        /// True once values have been loaded into the container.
        /// </summary>
        public bool ValuesLoaded { get; private set; }

        public IEnumerable<SettingKey> GetKeys()
        {
            yield return new SettingKey("Rendering/EntityBrushColor", "PointEntityColour", typeof(Color));
            yield return new SettingKey("Rendering/EntityBrushColor", "WorldBrushColour", typeof(Color));
            yield return new SettingKey("Rendering/EntityBrushColor", "BrushEntityColour", typeof(Color));
            yield return new SettingKey("Rendering/EntityBrushColor", "CustomEntityColours", typeof(List<EntityColourOverride>)) { EditorType = "EntityBrushColorEditor" };
        }

        public void LoadValues(ISettingsStore store)
        {
            store.LoadInstance(this);
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.StoreInstance(this);
        }
    }
}
