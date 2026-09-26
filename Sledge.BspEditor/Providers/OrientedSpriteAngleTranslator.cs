using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sledge.BspEditor.Environment;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.DataStructures.GameData;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Providers
{
    /// <summary>
    /// GoldSrc's real SPR_ORIENTED renderer (confirmed against the compiled/in-game result, and
    /// against the open-source Xash3D-FWGS reimplementation of AngleVectors/R_DrawSpriteModel)
    /// builds its "right" vector from the entity's yaw the mirror image of what Hammertime's own
    /// 3D viewport shows for the same yaw value. The viewport's math already matches AngleVectors
    /// exactly - the mismatch only appears once the map is compiled and run.
    ///
    /// Rather than making mappers guess the "actual" yaw for every Oriented/ParallelOriented
    /// sprite, this negates the yaw only at the .map file boundary (MapBspSourceProvider), so:
    ///  - The in-memory document and viewport always show the angle the mapper actually set/sees.
    ///  - The .map file always contains the angle GoldSrc needs to reproduce that same facing.
    ///
    /// Negation is its own inverse (translate(translate(x)) == x), so the same method is used
    /// both when writing angles out and when reading them back in.
    ///
    /// This was derived from a test with pitch = 0 and roll = 0 (yaw 0/90/180/270 only). Pitch
    /// and roll are passed through unchanged. If an Oriented sprite ever needs a tilt or roll,
    /// verify that behaviour in a compiled map before trusting this translator for it.
    /// </summary>
    public static class OrientedSpriteAngleTranslator
    {
        public static Vector3 Translate(Vector3 angles)
        {
            var yaw = -angles.Y % 360f;
            if (yaw < 0) yaw += 360f;
            return new Vector3(angles.X, yaw, angles.Z);
        }

        /// <summary>
        /// True if this entity's assigned sprite is Oriented or ParallelOriented - the only two
        /// modes that use the entity's "angles" keyvalue to fix the sprite's facing in the world
        /// (see SpriteOrientation.cs). Every other mode always faces the camera, so its angles
        /// don't need any translation.
        /// </summary>
        public static async Task<bool> IsOrientedSprite(Entity entity, GameData gd, TextureCollection tc)
        {
            if (tc == null) return false;

            var name = GetSpriteName(entity, gd);
            if (string.IsNullOrWhiteSpace(name) || !tc.HasTexture(name)) return false;

            var texture = await tc.GetTextureItem(name);
            return texture?.Orientation == SpriteOrientation.Oriented
                   || texture?.Orientation == SpriteOrientation.ParallelOriented;
        }

        /// <summary>
        /// Mirrors EntitySpriteChangeHandler.GetSpriteData's name resolution (sprite/iconsprite
        /// behaviour, then a "sprite"-typed or named property) without pulling in the rendering
        /// project just to answer "what sprite does this entity use".
        /// </summary>
        private static string GetSpriteName(Entity entity, GameData gd)
        {
            if (entity.Hierarchy.HasChildren || string.IsNullOrWhiteSpace(entity.EntityData.Name)) return null;
            var cls = gd?.GetClass(entity.EntityData.Name);
            if (cls == null) return null;

            var spr = cls.Behaviours.FirstOrDefault(x => string.Equals(x.Name, "sprite", StringComparison.InvariantCultureIgnoreCase))
                      ?? cls.Behaviours.FirstOrDefault(x => string.Equals(x.Name, "iconsprite", StringComparison.InvariantCultureIgnoreCase));
            if (spr == null) return null;

            if (spr.Values.Count == 1 && !string.IsNullOrWhiteSpace(spr.Values[0]))
            {
                return spr.Values[0].Trim();
            }

            var prop = cls.Properties.FirstOrDefault(x => x.VariableType == VariableType.Sprite) ??
                       cls.Properties.FirstOrDefault(x => string.Equals(x.Name, "sprite", StringComparison.InvariantCultureIgnoreCase));
            if (prop != null)
            {
                var val = entity.EntityData.Get(prop.Name, prop.DefaultValue);
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }

            return null;
        }
    }
}
