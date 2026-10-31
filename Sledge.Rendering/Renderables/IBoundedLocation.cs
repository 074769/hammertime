namespace Sledge.Rendering.Renderables
{
    /// <summary>
    /// A location that knows how far its drawing reaches from its position, so the renderer can skip objects
    /// that cannot be on screen. Locations that do not implement this are never culled.
    /// </summary>
    public interface IBoundedLocation : ILocation
    {
        /// <summary>
        /// The distance from Location that contains everything this location draws.
        /// </summary>
        float Radius { get; }
    }
}
