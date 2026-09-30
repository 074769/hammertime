namespace Sledge.BspEditor.Providers
{
	/// <summary>
	/// Axis conventions the OBJ exporter can convert to.
	/// The editor itself is Z-up, right-handed.
	/// </summary>
	public enum ObjAxisMode
	{
		/// <summary>Keep the editor's axes (Z-up).</summary>
		None,

		/// <summary>Blender's default OBJ import convention: Y-up, -Z forward.</summary>
		Blender,

		/// <summary>3ds Max: Z-up, right-handed (import with "Flip ZY-axis" off).</summary>
		Max3ds
	}

	public class ObjExportOptions
	{
		/// <summary>Only export the solids that are selected (or inside a selected entity/group).</summary>
		public bool SelectedOnly { get; set; }

		/// <summary>Axis convention of the target application.</summary>
		public ObjAxisMode Axis { get; set; } = ObjAxisMode.None;

		/// <summary>Move the exported geometry so the centre of its bounding box sits at (0, 0, 0).</summary>
		public bool ZeroOrigin { get; set; }
	}
}
