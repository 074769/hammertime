using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
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
