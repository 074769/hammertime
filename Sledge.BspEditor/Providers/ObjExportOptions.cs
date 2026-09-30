namespace Sledge.BspEditor.Providers
{
	/// <summary>
	/// Axis conventions the OBJ exporter can convert to.
	/// The editor itself is Z-up, right-handed.
	/// </summary>
	public enum ObjAxisPreset
	{
		/// <summary>No conversion: Z-up, right-handed (the editor's own space).</summary>
		Source,

		/// <summary>Y-up, -Z forward. Imports upright with Blender's default OBJ importer settings.</summary>
		Blender,

		/// <summary>Z-up, right-handed. Same as the editor's space, so no rotation is needed.</summary>
		Max
	}

	public class ObjExportOptions
	{
		/// <summary>Only export the solids that are selected (or inside a selected entity/group).</summary>
		public bool SelectedOnly { get; set; }

		/// <summary>Move the exported geometry so the centre of its bounding box sits at (0, 0, 0).</summary>
		public bool ZeroOrigin { get; set; }

		/// <summary>Axis convention of the target application.</summary>
		public ObjAxisPreset Axis { get; set; } = ObjAxisPreset.Source;
	}
}
