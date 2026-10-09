using System.Drawing;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// Represents a per-entity-name colour override for the 2D viewport rendering.
    /// </summary>
    public class EntityColourOverride
    {
        /// <summary>
        /// The entity name to match (compares against the entity's <see cref="Entity.EntityData.Name"/>).
        /// </summary>
        public string EntityName { get; set; }

        /// <summary>
        /// The colour to use for the matched entity.
        /// </summary>
        public Color Colour { get; set; }

        public EntityColourOverride()
        {
            EntityName = "";
            Colour = Color.Magenta;
        }

        public EntityColourOverride(string entityName, Color colour)
        {
            EntityName = entityName;
            Colour = colour;
        }
    }
}