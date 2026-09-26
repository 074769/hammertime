using System.Numerics;

namespace Sledge.BspEditor.Providers
{
    /// <summary>
    /// GoldSrc ends up compiling an Oriented/ParallelOriented sprite's yaw differently to what
    /// was set in Hammertime. Confirmed by diffing a saved .map against the same map decompiled
    /// back out of the compiled result, entity-for-entity, by origin:
    ///
    ///   yaw set in Hammertime -> yaw once compiled
    ///          0 (360)        ->        180
    ///          90             ->        90
    ///          180            ->        0
    ///          270            ->        270
    ///
    /// i.e. compiled = 180 - yaw (0 and 180 swap with each other; 90 and 270 are untouched).
    /// Pitch and roll were 0 in every test and were not observed to change.
    ///
    /// The "angles" keyvalue itself is never touched by Hammertime; whatever the mapper types is
    /// exactly what gets saved to the .map, and it's the compile step that does this. This
    /// translator is applied only on the render side (see EntitySpriteChangeHandler.GetAngles),
    /// purely so the viewport's preview matches that compiled/in-game result 1:1.
    ///
    /// If an Oriented sprite ever needs a tilt or roll, verify that behaviour against a compiled
    /// map (the same way this was derived) before trusting this translator for it.
    /// </summary>
    public static class OrientedSpriteAngleTranslator
    {
        public static Vector3 Translate(Vector3 angles)
        {
            var yaw = (180f - angles.Y) % 360f;
            if (yaw < 0) yaw += 360f;
            return new Vector3(angles.X, yaw, angles.Z);
        }
    }
}
