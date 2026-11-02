using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Resources;

namespace Sledge.BspEditor.Rendering.Converters
{
    /// <summary>
    /// Builds the two looks of a selectable object. The object is converted once, as it looks unselected;
    /// the selected look is made from a copy of that geometry. Every draw is tagged with the object's slot,
    /// and the renderer only draws the look that matches the object's current selection, so selecting an
    /// object never has to convert it again.
    /// </summary>
    internal static class SelectionVariants
    {
        /// <summary>The colour of selected objects.</summary>
        internal static readonly Vector4 SelectedColour = new Vector4(1, 0, 0, 1);

        /// <summary>Selected solids also tint their textures towards red.</summary>
        internal static readonly Vector4 SolidTintScale = new Vector4(1, 0.5f, 0.5f, 1);

        /// <summary>
        /// The selected look of some vertices: red, tinted by <paramref name="tintScale"/>, and moved by
        /// the selective transform while the selection is being dragged.
        /// </summary>
        internal static VertexStandard[] SelectedLook(VertexStandard[] points, Vector4 tintScale)
        {
            var result = new VertexStandard[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                p.Colour = SelectedColour;
                p.Tint *= tintScale;
                p.Flags |= VertexFlags.SelectiveTransformed;
                result[i] = p;
            }
            return result;
        }

        /// <summary>The same vertices, but never moved by the selective transform (the outline of the original position).</summary>
        internal static VertexStandard[] Untransformed(VertexStandard[] points)
        {
            var result = new VertexStandard[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                p.Flags = VertexFlags.None;
                result[i] = p;
            }
            return result;
        }

        /// <summary>The same vertices, flagged to be drawn as wireframe.</summary>
        internal static VertexStandard[] Wireframed(VertexStandard[] points)
        {
            var result = new VertexStandard[points.Length];
            for (var i = 0; i < points.Length; i++)
            {
                var p = points[i];
                p.Flags |= VertexFlags.Wireframed;
                result[i] = p;
            }
            return result;
        }

        /// <summary>Copies of the groups that are only drawn in the given selection state.</summary>
        internal static List<BufferGroup> Tag(IEnumerable<BufferGroup> groups, int slot, SelectionFilter filter)
        {
            return groups.Select(g => g.WithSelection(slot, filter)).ToList();
        }

        /// <summary>
        /// Appends a solid-like object in both looks.
        /// </summary>
        /// <param name="slot">The object's selection slot.</param>
        /// <param name="unselected">The vertices as they look when the object is not selected.</param>
        /// <param name="indices">Solid indices followed by wireframe indices.</param>
        /// <param name="shared">Groups drawn in both looks (the faces).</param>
        /// <param name="wireStart">Where the wireframe indices start.</param>
        /// <param name="wireCount">How many wireframe indices there are.</param>
        /// <param name="selectedTintScale">How the selected look tints the vertices.</param>
        /// <returns>The vertices of the selected look.</returns>
        internal static VertexStandard[] AppendBoth(
            BufferBuilder builder, int slot,
            VertexStandard[] unselected, uint[] indices, IReadOnlyList<BufferGroup> shared,
            uint wireStart, uint wireCount, Vector4 selectedTintScale)
        {
            // Unselected: the wireframe is only drawn in the 2D views
            var unselectedGroups = Tag(shared, slot, SelectionFilter.WhenUnselected);
            unselectedGroups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Orthographic, wireStart, wireCount)
                .WithSelection(slot, SelectionFilter.WhenUnselected));
            builder.Append(unselected, indices, unselectedGroups);

            // Selected: the 3D view uses the normal (depth tested) wireframe, the 2D views the on-top selection pipeline
            var selected = SelectedLook(unselected, selectedTintScale);
            var selectedGroups = Tag(shared, slot, SelectionFilter.WhenSelected);
            selectedGroups.Add(new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, wireStart, wireCount)
                .WithSelection(slot, SelectionFilter.WhenSelected));
            selectedGroups.Add(new BufferGroup(PipelineType.WireframeSelected, CameraType.Orthographic, wireStart, wireCount)
                .WithSelection(slot, SelectionFilter.WhenSelected));
            builder.Append(selected, indices, selectedGroups);

            // Selected: also the outline of where the object was, which stays put while the selection is dragged
            builder.Append(Untransformed(selected), indices.Skip((int) wireStart), new[]
            {
                new BufferGroup(PipelineType.Wireframe, CameraType.Perspective, 0, wireCount).WithSelection(slot, SelectionFilter.WhenSelected),
                new BufferGroup(PipelineType.WireframeSelected, CameraType.Orthographic, 0, wireCount).WithSelection(slot, SelectionFilter.WhenSelected)
            });

            return selected;
        }
    }
}
