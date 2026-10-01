using System.Numerics;
using Sledge.BspEditor.Environment;

namespace Sledge.BspEditor.Rendering.Converters
{
	/// <summary>
	/// Engine-profile-aware interpretation of a Color255 FX colour (rendercolor, sprite colour).
	/// GoldSrc: the default / unset value is 0 0 0, and the engine draws that as plain white,
	///          so a zeroed or missing colour must render white, not black.
	/// Source:  the default is 255 255 255, and 0 0 0 is a real black tint, so it is drawn as black.
	/// </summary>
	public static class RenderColorProfile
	{
		public static bool IsGoldsource(IEnvironment environment)
		{
			return environment != null && string.Equals(environment.Engine, "Goldsource", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Converts a raw 0-255 colour keyvalue into a 0-1 tint for the given environment.
		/// </summary>
		/// <param name="raw255">The raw keyvalue (0-255 range), or null if unset</param>
		public static Vector3 ResolveTint(Vector3? raw255, IEnvironment environment)
		{
			if (!raw255.HasValue) return Vector3.One;
			if (IsGoldsource(environment) && raw255.Value == Vector3.Zero) return Vector3.One;
			return raw255.Value / 255f;
		}
	}
}
