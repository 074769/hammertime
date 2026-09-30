using System;
using System.Collections.Generic;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
	/// <summary>
	/// Simulates the runtime pitch/volume modulation that GoldSource applies to an ambient_generic
	/// when a "Dynamic Preset" is selected. This is a port of CAmbientGeneric::InitModulationParms,
	/// ToggleUse (toggle off) and RampThink (5Hz) from the Half-Life SDK (dlls/sound.cpp), including
	/// the 27-entry rgdpvpreset table, so the preview matches what the engine does.
	/// The sound is assumed to be looping, triggered on at t=0 and triggered off later.
	/// </summary>
	internal static class AmbientPresetSimulator
	{
		public const int PresetCount = 27;
		private const int PitchNorm = 100;
		private const double ThinkInterval = 0.2;
		private const double FirstThink = 0.1;
		private const double MaxDuration = 30;

		private const int ChangePitch = 1;
		private const int ChangeVol = 2;

		// pitchrun, pitchstart, spinup, spindown, volrun, volstart, fadein, fadeout,
		// lfotype, lforate, lfomodpitch, lfomodvol, cspinup
		private static readonly int[][] Presets =
		{
			new[] { 255, 75, 95, 95, 10, 1, 50, 95, 0, 0, 0, 0, 0 }, // 1
			new[] { 255, 85, 70, 88, 10, 1, 20, 88, 0, 0, 0, 0, 0 }, // 2
			new[] { 255, 100, 50, 75, 10, 1, 10, 75, 0, 0, 0, 0, 0 }, // 3
			new[] { 100, 100, 0, 0, 10, 1, 90, 90, 0, 0, 0, 0, 0 }, // 4
			new[] { 100, 100, 0, 0, 10, 1, 80, 80, 0, 0, 0, 0, 0 }, // 5
			new[] { 100, 100, 0, 0, 10, 1, 50, 70, 0, 0, 0, 0, 0 }, // 6
			new[] { 100, 100, 0, 0, 5, 1, 40, 50, 1, 50, 0, 10, 0 }, // 7
			new[] { 100, 100, 0, 0, 5, 1, 40, 50, 1, 150, 0, 10, 0 }, // 8
			new[] { 100, 100, 0, 0, 5, 1, 40, 50, 1, 750, 0, 10, 0 }, // 9
			new[] { 128, 100, 50, 75, 10, 1, 30, 40, 2, 8, 20, 0, 0 }, // 10
			new[] { 128, 100, 50, 75, 10, 1, 30, 40, 2, 25, 20, 0, 0 }, // 11
			new[] { 128, 100, 50, 75, 10, 1, 30, 40, 2, 70, 20, 0, 0 }, // 12
			new[] { 50, 50, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 13
			new[] { 70, 70, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 14
			new[] { 90, 90, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 15
			new[] { 120, 120, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 16
			new[] { 180, 180, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 17
			new[] { 255, 255, 0, 0, 10, 1, 20, 50, 0, 0, 0, 0, 0 }, // 18
			new[] { 200, 75, 90, 90, 10, 1, 50, 90, 2, 100, 20, 0, 0 }, // 19
			new[] { 255, 75, 97, 90, 10, 1, 50, 90, 1, 40, 50, 0, 0 }, // 20
			new[] { 100, 100, 0, 0, 10, 1, 30, 50, 3, 15, 20, 0, 0 }, // 21
			new[] { 160, 160, 0, 0, 10, 1, 50, 50, 3, 500, 25, 0, 0 }, // 22
			new[] { 255, 75, 88, 0, 10, 1, 40, 0, 0, 0, 0, 0, 5 }, // 23
			new[] { 200, 20, 95, 70, 10, 1, 70, 70, 3, 20, 50, 0, 0 }, // 24
			new[] { 180, 100, 50, 60, 10, 1, 40, 60, 2, 90, 100, 100, 0 }, // 25
			new[] { 60, 60, 0, 0, 10, 1, 40, 70, 3, 80, 20, 50, 0 }, // 26
			new[] { 128, 90, 10, 10, 10, 1, 20, 40, 1, 5, 10, 20, 0 }, // 27
		};

		public struct Sample
		{
			public double Time;
			/// <summary>Playback rate multiplier (engine pitch / 100)</summary>
			public double Pitch;
			/// <summary>Volume, 0 - 1</summary>
			public double Volume;
		}

		public class Result
		{
			public List<Sample> Samples = new List<Sample>();
			/// <summary>Seconds of audio to render</summary>
			public double Duration;
			/// <summary>Time the engine stopped thinking (settled), or -1 if it never settles (LFO)</summary>
			public double SettleTime = -1;

			/// <summary>Piecewise-linear pitch (rate) and volume at time t</summary>
			public void Evaluate(double t, out double pitch, out double volume)
			{
				var s = Samples;
				if (t <= s[0].Time) { pitch = s[0].Pitch; volume = s[0].Volume; return; }
				var last = s[s.Count - 1];
				if (t >= last.Time) { pitch = last.Pitch; volume = last.Volume; return; }

				int lo = 0, hi = s.Count - 1;
				while (hi - lo > 1)
				{
					var mid = (lo + hi) / 2;
					if (s[mid].Time <= t) lo = mid; else hi = mid;
				}
				var a = s[lo];
				var b = s[hi];
				var f = b.Time > a.Time ? (t - a.Time) / (b.Time - a.Time) : 0;
				pitch = a.Pitch + (b.Pitch - a.Pitch) * f;
				volume = a.Volume + (b.Volume - a.Volume) * f;
			}
		}

		private class Dpv
		{
			public int PitchRun, PitchStart, SpinUp, SpinDown, VolRun, VolStart, FadeIn, FadeOut;
			public int LfoType, LfoRate, LfoModPitch, LfoModVol, CSpinUp, CSpinCount;
			public int Pitch, SpinUpSav, SpinDownSav, PitchFrac, Vol, FadeInSav, FadeOutSav, VolFrac, LfoFrac, LfoMult;

			public bool Ramping => SpinUp != 0 || SpinDown != 0 || FadeIn != 0 || FadeOut != 0;
		}

		/// <summary>
		/// Simulate a preset (1 - 27). Returns null for preset 0 / unknown presets.
		/// </summary>
		public static Result Simulate(int preset, Random rng = null)
		{
			if (preset < 1 || preset > PresetCount) return null;
			rng = rng ?? new Random();

			// First pass finds out when the preset settles, so we know when to trigger it off
			var probe = Run(preset, new double[0], rng);
			double off;
			if (probe.SettleTime >= 0) off = Math.Min(12, Math.Max(6, probe.SettleTime + 1.5));
			else off = 6;

			// Presets with cspinup spin up a bit more on every trigger instead of switching off
			double[] toggles = Presets[preset - 1][12] != 0
				? new[] { 3.0, 5.0, 7.0 }
				: new[] { off };

			return Run(preset, toggles, rng);
		}

		private static Result Run(int preset, double[] toggles, Random rng)
		{
			var row = Presets[preset - 1];
			var d = new Dpv
			{
				PitchRun = row[0], PitchStart = row[1], SpinUp = row[2], SpinDown = row[3],
				VolRun = row[4], VolStart = row[5], FadeIn = row[6], FadeOut = row[7],
				LfoType = row[8], LfoRate = row[9], LfoModPitch = row[10], LfoModVol = row[11], CSpinUp = row[12]
			};

			// InitModulationParms: fixups applied to preset values
			if (d.SpinDown > 0) d.SpinDown = (101 - d.SpinDown) * 64;
			if (d.SpinUp > 0) d.SpinUp = (101 - d.SpinUp) * 64;
			d.VolStart *= 10;
			d.VolRun *= 10;
			if (d.FadeIn > 0) d.FadeIn = (101 - d.FadeIn) * 64;
			if (d.FadeOut > 0) d.FadeOut = (101 - d.FadeOut) * 64;
			d.LfoRate *= 256;
			d.FadeInSav = d.FadeIn;
			d.FadeOutSav = d.FadeOut;
			d.SpinUpSav = d.SpinUp;
			d.SpinDownSav = d.SpinDown;

			d.FadeIn = d.FadeInSav;
			d.FadeOut = 0;
			d.Vol = d.FadeIn != 0 ? d.VolStart : d.VolRun;
			d.SpinUp = d.SpinUpSav;
			d.SpinDown = 0;
			d.Pitch = d.SpinUp != 0 ? d.PitchStart : d.PitchRun;
			if (d.Pitch == 0) d.Pitch = PitchNorm;
			d.PitchFrac = d.Pitch << 8;
			d.VolFrac = d.Vol << 8;
			d.LfoFrac = 0;
			d.LfoRate = Math.Abs(d.LfoRate);
			d.CSpinCount = 1;
			if (d.CSpinUp != 0)
			{
				var pitchInc = (255 - d.PitchStart) / d.CSpinUp;
				d.PitchRun = d.PitchStart + pitchInc;
				if (d.PitchRun > 255) d.PitchRun = 255;
			}
			if ((d.SpinUpSav != 0 || d.SpinDownSav != 0 || (d.LfoType != 0 && d.LfoModPitch != 0)) && d.Pitch == PitchNorm)
			{
				d.Pitch = PitchNorm + 1; // engine never sends "no pitch" as the first pitch
			}

			var result = new Result();
			double curPitch = d.Pitch;
			double curVol = d.Vol;
			result.Samples.Add(new Sample { Time = 0, Pitch = curPitch / 100.0, Volume = curVol / 100.0 });

			var thinking = true;
			var nextThink = FirstThink;
			var toggleIndex = 0;
			var stopped = false;
			double t = 0;
			double lastEvent = 0;

			while (true)
			{
				var hasToggle = toggleIndex < toggles.Length;
				var next = double.MaxValue;
				var isToggle = false;
				if (thinking) next = nextThink;
				if (hasToggle && toggles[toggleIndex] <= next) { next = toggles[toggleIndex]; isToggle = true; }
				if (next == double.MaxValue || next > MaxDuration) break;

				t = next;
				lastEvent = t;

				if (isToggle)
				{
					toggleIndex++;
					if (d.CSpinUp != 0)
					{
						// Don't actually shut off, each trigger spins up to a higher pitch
						if (d.CSpinCount <= d.CSpinUp)
						{
							d.CSpinCount++;
							var pitchInc = (255 - d.PitchStart) / d.CSpinUp;
							d.SpinUp = d.SpinUpSav;
							d.SpinDown = 0;
							d.PitchRun = d.PitchStart + pitchInc * d.CSpinCount;
							if (d.PitchRun > 255) d.PitchRun = 255;
							nextThink = t + 0.1;
							thinking = true;
						}
					}
					else if (d.SpinDownSav != 0 || d.FadeOutSav != 0)
					{
						d.SpinDown = d.SpinDownSav;
						d.SpinUp = 0;
						d.FadeOut = d.FadeOutSav;
						d.FadeIn = 0;
						nextThink = t + 0.1;
						thinking = true;
					}
					else
					{
						stopped = true;
						break;
					}
					continue;
				}

				// RampThink
				int flags;
				bool stop;
				var changed = Think(d, rng, ref curPitch, ref curVol, out flags, out stop);
				if (stop) { stopped = true; break; }
				if (changed)
				{
					result.Samples.Add(new Sample { Time = t, Pitch = curPitch / 100.0, Volume = curVol / 100.0 });
				}

				if (!d.Ramping && d.LfoType == 0)
				{
					thinking = false; // "no ramps or lfo, stop thinking"
					if (result.SettleTime < 0) result.SettleTime = t;
				}
				else
				{
					nextThink = t + ThinkInterval;
				}
			}

			result.Duration = stopped ? t : Math.Min(MaxDuration, lastEvent + 2.0);
			if (result.Duration <= 0) result.Duration = 1;

			// Hold the final state until the end of the clip
			var last = result.Samples[result.Samples.Count - 1];
			if (last.Time < result.Duration)
			{
				result.Samples.Add(new Sample { Time = result.Duration, Pitch = last.Pitch, Volume = last.Volume });
			}
			return result;
		}

		/// <summary>One RampThink tick. Returns true if a pitch/volume update would be sent to the sound.</summary>
		private static bool Think(Dpv d, Random rng, ref double curPitch, ref double curVol, out int flags, out bool stop)
		{
			stop = false;
			flags = 0;
			var pitch = d.Pitch;
			var vol = d.Vol;
			var changed = false;

			if (!d.Ramping && d.LfoType == 0) return false;

			// pitch envelope
			if (d.SpinUp != 0 || d.SpinDown != 0)
			{
				var prev = d.PitchFrac >> 8;
				if (d.SpinUp > 0) d.PitchFrac += d.SpinUp;
				else if (d.SpinDown > 0) d.PitchFrac -= d.SpinDown;
				pitch = d.PitchFrac >> 8;

				if (pitch > d.PitchRun)
				{
					pitch = d.PitchRun;
					d.SpinUp = 0;
				}
				if (pitch < d.PitchStart)
				{
					stop = true;
					return false;
				}
				if (pitch > 255) pitch = 255;
				if (pitch < 1) pitch = 1;
				d.Pitch = pitch;
				changed |= prev != pitch;
				flags |= ChangePitch;
			}

			// amplitude envelope
			if (d.FadeIn != 0 || d.FadeOut != 0)
			{
				var prev = d.VolFrac >> 8;
				if (d.FadeIn > 0) d.VolFrac += d.FadeIn;
				else if (d.FadeOut > 0) d.VolFrac -= d.FadeOut;
				vol = d.VolFrac >> 8;

				if (vol > d.VolRun)
				{
					vol = d.VolRun;
					d.FadeIn = 0;
				}
				if (vol < d.VolStart)
				{
					stop = true;
					return false;
				}
				if (vol > 100) vol = 100;
				if (vol < 1) vol = 1;
				d.Vol = vol;
				changed |= prev != vol;
				flags |= ChangeVol;
			}

			// pitch/amplitude LFO
			if (d.LfoType != 0)
			{
				if (d.LfoFrac > 0x6fffffff) d.LfoFrac = 0;
				d.LfoFrac += d.LfoRate;
				var pos = d.LfoFrac >> 8;
				if (d.LfoFrac < 0)
				{
					d.LfoFrac = 0;
					d.LfoRate = Math.Abs(d.LfoRate);
					pos = 0;
				}
				else if (pos > 255)
				{
					pos = 255;
					d.LfoFrac = 255 << 8;
					d.LfoRate = -Math.Abs(d.LfoRate);
				}

				switch (d.LfoType)
				{
					case 1: // square
						d.LfoMult = pos < 128 ? 255 : 0;
						break;
					case 3: // random
						if (pos == 255) d.LfoMult = rng.Next(0, 256);
						break;
					default: // triangle
						d.LfoMult = pos;
						break;
				}

				if (d.LfoModPitch != 0)
				{
					var prev = pitch;
					pitch += ((d.LfoMult - 128) * d.LfoModPitch) / 100;
					if (pitch > 255) pitch = 255;
					if (pitch < 1) pitch = 1;
					changed |= prev != pitch;
					flags |= ChangePitch;
				}

				if (d.LfoModVol != 0)
				{
					var prev = vol;
					vol += ((d.LfoMult - 128) * d.LfoModVol) / 100;
					if (vol > 100) vol = 100;
					if (vol < 0) vol = 0;
					changed |= prev != vol;
					flags |= ChangeVol;
				}
			}

			if (flags != 0 && changed)
			{
				if (pitch == PitchNorm) pitch = PitchNorm + 1;
				if ((flags & ChangePitch) != 0) curPitch = pitch;
				if ((flags & ChangeVol) != 0) curVol = vol;
				return true;
			}
			return false;
		}
	}
}
