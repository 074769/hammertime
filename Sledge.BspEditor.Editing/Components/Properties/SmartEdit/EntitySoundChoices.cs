using System;
using System.Collections.Generic;
using System.IO;
using Sledge.BspEditor.Documents;
using Sledge.FileSystem;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
	/// <summary>
	/// Which entity keyvalues are "choices" that pick one of the engine's built-in sounds,
	/// and which sound each choice plays. The tables are taken from the Half-Life SDK
	/// (doors.cpp, plats.cpp, buttons.cpp). The keys are the numeric choice values.
	/// </summary>
	internal static class EntitySoundChoices
	{
		private static readonly HashSet<string> DoorClasses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
		{
			"func_door", "func_door_rotating", "func_water", "momentary_door"
		};

		private static readonly HashSet<string> PlatClasses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
		{
			"func_plat", "func_platrot", "func_trackchange", "func_trackautochange"
		};

		private static readonly HashSet<string> ButtonClasses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
		{
			"func_button", "func_rot_button", "momentary_rot_button"
		};

		// CBaseDoor: doors/doormove1.wav - doormove10.wav, doors/doorstop1.wav - doorstop8.wav
		private static readonly string[] DoorMove =
		{
			null,
			"doors/doormove1.wav", "doors/doormove2.wav", "doors/doormove3.wav", "doors/doormove4.wav", "doors/doormove5.wav",
			"doors/doormove6.wav", "doors/doormove7.wav", "doors/doormove8.wav", "doors/doormove9.wav", "doors/doormove10.wav"
		};

		private static readonly string[] DoorStop =
		{
			null,
			"doors/doorstop1.wav", "doors/doorstop2.wav", "doors/doorstop3.wav", "doors/doorstop4.wav",
			"doors/doorstop5.wav", "doors/doorstop6.wav", "doors/doorstop7.wav", "doors/doorstop8.wav"
		};

		// CBasePlatTrain (func_plat, func_platrot, func_trackchange)
		private static readonly string[] PlatMove =
		{
			null,
			"plats/bigmove1.wav", "plats/bigmove2.wav", "plats/elevmove1.wav", "plats/elevmove2.wav", "plats/elevmove3.wav",
			"plats/freightmove1.wav", "plats/freightmove2.wav", "plats/heavymove1.wav", "plats/rackmove1.wav",
			"plats/railmove1.wav", "plats/squeekmove1.wav", "plats/talkmove1.wav", "plats/talkmove2.wav"
		};

		private static readonly string[] PlatStop =
		{
			null,
			"plats/bigstop1.wav", "plats/bigstop2.wav", "plats/freightstop1.wav", "plats/heavystop2.wav",
			"plats/rackstop1.wav", "plats/railstop1.wav", "plats/squeekstop1.wav", "plats/talkstop1.wav"
		};

		public static bool IsAmbientPreset(string className, string key)
		{
			return Is(className, "ambient_generic") && Is(key, "preset");
		}

		/// <summary>True if this class/key is a sound choice we know how to preview.</summary>
		public static bool Supports(string className, string key)
		{
			if (String.IsNullOrEmpty(className) || String.IsNullOrEmpty(key)) return false;
			if (IsAmbientPreset(className, key)) return true;

			if (Is(key, "movesnd")) return DoorClasses.Contains(className) || PlatClasses.Contains(className);
			if (Is(key, "stopsnd")) return DoorClasses.Contains(className) || PlatClasses.Contains(className);
			if (Is(key, "locked_sound") || Is(key, "unlocked_sound")) return DoorClasses.Contains(className) || ButtonClasses.Contains(className);
			if (Is(key, "sounds")) return ButtonClasses.Contains(className);
			return false;
		}

		/// <summary>
		/// Get the path (relative to the sound folder) that a choice plays. Returns false for "no sound"
		/// or for choices that don't map to a sound.
		/// </summary>
		public static bool TryGetSound(string className, string key, string value, out string path)
		{
			path = null;
			int n;
			if (!Supports(className, key) || IsAmbientPreset(className, key)) return false;
			if (!Int32.TryParse((value ?? "").Trim(), out n) || n <= 0) return false;

			if (Is(key, "movesnd")) path = PlatClasses.Contains(className) ? Get(PlatMove, n) : Get(DoorMove, n);
			else if (Is(key, "stopsnd")) path = PlatClasses.Contains(className) ? Get(PlatStop, n) : Get(DoorStop, n);
			else path = ButtonSound(n); // sounds, locked_sound, unlocked_sound
			return path != null;
		}

		// ButtonSound() in buttons.cpp. Also used for the locked/unlocked sounds of touch-opened doors.
		private static string ButtonSound(int n)
		{
			switch (n)
			{
				case 1: case 2: case 3: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 11:
					return "buttons/button" + n + ".wav";
				case 12: return "buttons/latchlocked1.wav";
				case 13: return "buttons/latchunlocked1.wav";
				case 14: return "buttons/lightswitch2.wav";
				case 21: case 22: case 23: case 24: case 25:
					return "buttons/lever" + (n - 20) + ".wav";
				default: return "buttons/button9.wav";
			}
		}

		private static string Get(string[] table, int n)
		{
			return n > 0 && n < table.Length ? table[n] : null;
		}

		private static bool Is(string a, string b)
		{
			return String.Equals(a, b, StringComparison.InvariantCultureIgnoreCase);
		}
	}

	/// <summary>Reads sound files out of the game's file system (loose files and paks).</summary>
	internal static class SoundFileLoader
	{
		/// <param name="document">The current document, for its environment's file system</param>
		/// <param name="soundPath">Path relative to the sound folder, e.g. "doors/doormove1.wav"</param>
		public static byte[] Load(MapDocument document, string soundPath)
		{
			var root = document?.Environment?.Root;
			if (root == null) throw new FileNotFoundException("No game environment is loaded.");

			var p = (soundPath ?? "").Trim().Replace('\\', '/').Trim('/');
			if (p.Length == 0) throw new FileNotFoundException("No sound file is set.");
			if (p[0] == '*' || p[0] == '!' || p[0] == '#')
			{
				throw new NotSupportedException("Sentence names can't be previewed, only .wav files.");
			}
			if (!p.StartsWith("sound/", StringComparison.InvariantCultureIgnoreCase)) p = "sound/" + p;

			IFile file;
			try
			{
				file = root.TraversePath(p);
			}
			catch (Exception)
			{
				file = null;
			}
			if (file == null || !file.Exists) throw new FileNotFoundException("Couldn't find " + p + " in the game files.");

			using (var ms = new MemoryStream())
			{
				using (var stream = file.Open()) stream.CopyTo(ms);
				return ms.ToArray();
			}
		}
	}
}
