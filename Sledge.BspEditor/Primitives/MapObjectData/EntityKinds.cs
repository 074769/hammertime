using System;

namespace Sledge.BspEditor.Primitives.MapObjectData
{
    /// <summary>
    /// Entity kinds that never move or rotate in the map, so the movement helpers
    /// (2D rotation arrow, 3D motion widget, 3D move arrow) should skip them.
    /// </summary>
    public static class EntityKinds
    {
        /// <summary>
        /// True for sprite entities: env_sprite and cycler_sprite, or any entity whose
        /// model is a .spr sprite file.
        /// </summary>
        public static bool IsSprite(EntityData data)
        {
            if (data == null) return false;
            if (string.Equals(data.Name, "env_sprite", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(data.Name, "cycler_sprite", StringComparison.OrdinalIgnoreCase)) return true;

            var model = data.Get<string>("model");
            return model != null && model.Trim().EndsWith(".spr", StringComparison.OrdinalIgnoreCase);
        }
    }
}
