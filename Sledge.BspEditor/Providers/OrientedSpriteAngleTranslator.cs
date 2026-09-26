using System.Numerics;

namespace Sledge.BspEditor.Providers
{
    /// <summary>
    /// GoldSrc's real SPR_ORIENTED renderer (confirmed against the compiled/in-game result, and
    /// against the open-source Xash3D-FWGS reimplementation of AngleVectors/R_DrawSpriteModel)
    /// builds its "right" vector from an entity's yaw the mirror image of what plain AngleVectors
    /// math - the math Hammertime's own 3D viewport used to use - would suggest.
    ///
    /// The "angles" keyvalue itself is never touched; whatever the mapper types is exactly what
    /// gets saved to the .map and exactly what GoldSrc reads at compile time. This translator is
    /// applied only on the render side (see EntitySpriteChangeHandler.GetAngles), purely to make
    /// the viewport's preview match that compiled/in-game result 1:1.
    ///
    /// This was derived from a test with pitch = 0 and roll = 0 (yaw 0/90/180/270 only), which
    /// showed the compiled sprite's facing was consistently the negation of the yaw that was set.
    /// Pitch and roll are passed through unchanged. If an Oriented sprite ever needs a tilt or
    /// roll, verify that behaviour in a compiled map before trusting this translator for it.
    /// </summary>
    public static class OrientedSpriteAngleTranslator
    {
        public static Vector3 Translate(Vector3 angles)
        {
            var yaw = -angles.Y % 360f;
            if (yaw < 0) yaw += 360f;
            return new Vector3(angles.X, yaw, angles.Z);
        }
    }
}
