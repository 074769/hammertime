using System;
using System.IO;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
	/// <summary>
	/// Renders a PCM wav with a volume gain and, optionally, a pitch/volume envelope
	/// (see <see cref="AmbientPresetSimulator"/>) into a new 16-bit PCM wav that
	/// System.Media.SoundPlayer can play. Supports 8 and 16 bit PCM, any channel count.
	/// </summary>
	internal static class WavPreviewRenderer
	{
		/// <summary>
		/// Render a wav.
		/// </summary>
		/// <param name="source">The bytes of the original .wav file</param>
		/// <param name="gain">Preview volume, 0 - 1</param>
		/// <param name="envelope">Pitch/volume envelope, or null to play the sound unmodified</param>
		/// <param name="loop">True to loop the source for the length of the envelope</param>
		/// <exception cref="NotSupportedException">The wav is not 8/16 bit PCM</exception>
		public static byte[] Render(byte[] source, double gain, AmbientPresetSimulator.Result envelope, bool loop)
		{
			int channels, sampleRate, bits, dataOffset, dataLength;
			ParseHeader(source, out channels, out sampleRate, out bits, out dataOffset, out dataLength);

			var bytesPerSample = bits / 8;
			var frameSize = bytesPerSample * channels;
			var srcFrames = dataLength / frameSize;
			if (srcFrames < 1) throw new NotSupportedException("The wav file contains no audio data.");

			// Decode to float
			var src = new float[srcFrames * channels];
			for (var i = 0; i < src.Length; i++)
			{
				var o = dataOffset + i * bytesPerSample;
				src[i] = bits == 8
					? (source[o] - 128) / 128f
					: (short) (source[o] | (source[o + 1] << 8)) / 32768f;
			}

			gain = Math.Max(0, Math.Min(1, gain));
			var outFrames = envelope == null ? srcFrames : (int) Math.Ceiling(envelope.Duration * sampleRate);
			if (outFrames < 1) outFrames = 1;
			var dst = new short[outFrames * channels];

			double pos = 0;
			for (var f = 0; f < outFrames; f++)
			{
				double pitch = 1, vol = 1;
				if (envelope != null) envelope.Evaluate((double) f / sampleRate, out pitch, out vol);

				if (loop && srcFrames > 0) pos %= srcFrames;
				var i0 = (int) pos;
				if (i0 >= srcFrames) break; // ran off the end of a non-looping sound: silence from here

				var i1 = i0 + 1;
				if (i1 >= srcFrames) i1 = loop ? 0 : i0;
				var frac = (float) (pos - i0);

				for (var c = 0; c < channels; c++)
				{
					var a = src[i0 * channels + c];
					var b = src[i1 * channels + c];
					var v = (a + (b - a) * frac) * vol * gain;
					if (v > 1) v = 1;
					if (v < -1) v = -1;
					dst[f * channels + c] = (short) (v * 32767f);
				}
				pos += pitch;
			}

			return WriteWav(dst, channels, sampleRate);
		}

		private static void ParseHeader(byte[] b, out int channels, out int sampleRate, out int bits, out int dataOffset, out int dataLength)
		{
			channels = sampleRate = bits = dataOffset = dataLength = 0;
			if (b == null || b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
			{
				throw new NotSupportedException("Not a RIFF/WAVE file. Only .wav files can be previewed.");
			}

			var haveFmt = false;
			var haveData = false;
			var pos = 12;
			while (pos + 8 <= b.Length)
			{
				var id = (char) b[pos] + "" + (char) b[pos + 1] + (char) b[pos + 2] + (char) b[pos + 3];
				var size = BitConverter.ToInt32(b, pos + 4);
				var body = pos + 8;
				if (size < 0) size = b.Length - body;

				if (id == "fmt " && body + 16 <= b.Length)
				{
					var tag = BitConverter.ToUInt16(b, body);
					channels = BitConverter.ToUInt16(b, body + 2);
					sampleRate = BitConverter.ToInt32(b, body + 4);
					bits = BitConverter.ToUInt16(b, body + 14);
					// 1 = PCM, 0xFFFE = extensible (assume PCM, the bit depth check below guards it)
					if (tag != 1 && tag != 0xFFFE) throw new NotSupportedException("Only uncompressed PCM wav files can be previewed.");
					haveFmt = true;
				}
				else if (id == "data")
				{
					dataOffset = body;
					dataLength = Math.Min(size, b.Length - body);
					haveData = true;
				}

				if (haveFmt && haveData) break;
				pos = body + size + (size & 1);
			}

			if (!haveFmt || !haveData) throw new NotSupportedException("The wav file is missing its format or data chunk.");
			if (channels < 1 || sampleRate < 1 || (bits != 8 && bits != 16)) throw new NotSupportedException("Only 8 or 16 bit PCM wav files can be previewed.");
		}

		private static byte[] WriteWav(short[] samples, int channels, int sampleRate)
		{
			using (var ms = new MemoryStream(44 + samples.Length * 2))
			using (var w = new BinaryWriter(ms))
			{
				var dataBytes = samples.Length * 2;
				w.Write(new[] { 'R', 'I', 'F', 'F' });
				w.Write(36 + dataBytes);
				w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
				w.Write(16);
				w.Write((short) 1);
				w.Write((short) channels);
				w.Write(sampleRate);
				w.Write(sampleRate * channels * 2);
				w.Write((short) (channels * 2));
				w.Write((short) 16);
				w.Write(new[] { 'd', 'a', 't', 'a' });
				w.Write(dataBytes);
				foreach (var s in samples) w.Write(s);
				w.Flush();
				return ms.ToArray();
			}
		}
	}
}
