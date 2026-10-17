using System.Drawing;
using System.Numerics;
using Sledge.Providers.Model.Mdl10.Format;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser
{
	/// <summary>
	/// Static helpers for the browser: small tightly-fitted thumbnails (grid view) and model bounds (info panel).
	/// The interactive preview uses <see cref="ModelScene"/> directly through <see cref="ModelViewerControl"/>.
	/// </summary>
	public static class ModelThumbnailRenderer
	{
		/// <summary>
		/// Render a square thumbnail (sequence 0, frame 0, first model of every bodypart, skin 0). Returns null if the model has no geometry.
		/// </summary>
		public static Bitmap RenderThumbnail(MdlFile mdl, int size, Color background)
		{
			var scene = new ModelScene(mdl);
			return scene.Render(new ModelRenderOptions
			{
				Width = size,
				Height = size,
				Background = background,
				FitTight = true,
				SuperSample = 2,
				Shading = ModelShading.Smooth
			});
		}

		/// <summary>
		/// Axis-aligned bounds of the geometry (sequence 0, frame 0, first model of each bodypart), or null if there is none.
		/// </summary>
		public static (Vector3 min, Vector3 max, int triangles)? GetBounds(MdlFile mdl)
		{
			return new ModelScene(mdl).GetBounds();
		}
	}
}
