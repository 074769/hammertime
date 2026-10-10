using System;
using System.Drawing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        [JsonConverter(typeof(HexColourJsonConverter))]
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

    /// <summary>
    /// Writes colours as "#RRGGBB". System.Drawing.Color has no settable members, so the default
    /// JSON serialiser can't read it back (imports/settings came back with no colour).
    /// Also reads the older forms ("255, 128, 0", colour names, {"R":..,"G":..,"B":..}) so existing exports and settings still load.
    /// </summary>
    public class HexColourJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Color) || objectType == typeof(Color?);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var c = (Color) value;
            writer.WriteValue($"#{c.R:X2}{c.G:X2}{c.B:X2}");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            try
            {
                if (token.Type == JTokenType.String)
                {
                    var text = token.Value<string>().Trim();
                    Color c;
                    try
                    {
                        // Handles "#RRGGBB", names ("Red") and the "255, 128, 0" form that older exports and settings used
                        c = (Color) new ColorConverter().ConvertFromInvariantString(text);
                    }
                    catch
                    {
                        c = ColorTranslator.FromHtml(text);
                    }
                    return Color.FromArgb(255, c.R, c.G, c.B);
                }
                if (token is JObject o && o["R"] != null && o["G"] != null && o["B"] != null)
                {
                    return Color.FromArgb(255, o.Value<int>("R"), o.Value<int>("G"), o.Value<int>("B"));
                }
            }
            catch
            {
                // fall through to the default
            }
            return Color.Magenta;
        }
    }
}