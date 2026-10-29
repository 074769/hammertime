using Sledge.BspEditor.Documents;
using Sledge.DataStructures.Geometric;

namespace Sledge.BspEditor.Tools.DragSelect
{
    /// <summary>
    /// Remembers the bounding box of the last non-empty selection. A box drawn in an orthographic view
    /// stretches along its hidden axis to these bounds (the same way the Vertex tool does), so the box
    /// covers the object's full height instead of collapsing to one grid step on the centre plane.
    /// The memory survives deselecting, so the next box still lines up with the last object selected.
    /// </summary>
    public class LastSelectionBounds
    {
        private Box _bounds;

        /// <summary>Remembers the current selection's bounds, if anything is selected.</summary>
        public void Update(MapDocument document)
        {
            var box = document?.Selection?.GetSelectionBoundingBox();
            if (box != null && !box.IsEmpty()) _bounds = box;
        }

        /// <summary>The remembered bounds, or null if nothing has been selected yet.</summary>
        public Box Get() => _bounds;
    }
}
