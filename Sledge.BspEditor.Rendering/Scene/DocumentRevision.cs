using System.Runtime.CompilerServices;
using System.Threading;
using Sledge.BspEditor.Documents;

namespace Sledge.BspEditor.Rendering.Scene
{
    /// <summary>
    /// A counter per map document that goes up whenever the map changes in any way except for which objects
    /// are selected. Things that are expensive to work out and only depend on the map and the camera, such as
    /// the 2D overlays, remember the revision they were built for and only rebuild when it moves.
    /// Moving an object with the mouse doesn't change the map until the mouse is released, so it doesn't count.
    /// </summary>
    public static class DocumentRevision
    {
        private sealed class Counter
        {
            public long Value;
        }

        private static readonly ConditionalWeakTable<MapDocument, Counter> Counters
            = new ConditionalWeakTable<MapDocument, Counter>();

        /// <summary>The current revision of a document.</summary>
        public static long Get(MapDocument document)
        {
            return Interlocked.Read(ref Counters.GetOrCreateValue(document).Value);
        }

        /// <summary>Marks the document as changed.</summary>
        public static void Bump(MapDocument document)
        {
            Interlocked.Increment(ref Counters.GetOrCreateValue(document).Value);
        }
    }
}
