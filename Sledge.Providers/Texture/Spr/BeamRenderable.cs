using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sledge.Rendering.Cameras;
using Sledge.Rendering.Engine;
using Sledge.Rendering.Interfaces;
using Sledge.Rendering.Pipelines;
using Sledge.Rendering.Primitives;
using Sledge.Rendering.Renderables;
using Sledge.Rendering.Resources;
using Sledge.Rendering.Viewports;
using Veldrid;
using Buffer = Sledge.Rendering.Resources.Buffer;
using RenderTexture = Sledge.Rendering.Resources.Texture;

namespace Sledge.Providers.Texture.Spr
{
	/// <summary>One start/end point pair of a beam, in world space.</summary>
	public readonly struct BeamPair
	{
		public readonly Vector3 Start;
		public readonly Vector3 End;

		public BeamPair(Vector3 start, Vector3 end)
		{
			Start = start;
			End = end;
		}
	}

	/// <summary>
	/// Draws a GoldSrc env_beam / env_laser style beam in the 3D viewport: a camera facing ribbon between
	/// each start/end pair, textured with a sprite and added on top of the scene.
	///
	/// The maths follows the engine's beam code (Xash3D FWGS ref/gl/gl_beams.c, R_DrawSegs and R_DrawRing,
	/// which mirror the GoldSrc client):
	///  - the ribbon is 2 * width units wide, width = BoltWidth * 0.1
	///  - noise amplitude = NoiseAmplitude / 100, midpoint displacement noise (FracNoise), re-rolled every frame
	///  - segment count is one per 4 units with noise (>= 0.5), one per ~13 units without, capped at 64
	///  - the texture repeats once per 100 units along the beam and scrolls TextureScroll * 0.1 times per second
	///  - Shade Start / Shade End fade the brightness along the beam
	/// Every engine-derived constant is below so it can be adjusted in one place if the preview differs from the game.
	/// </summary>
	public class BeamRenderable : IRenderable, IUpdateable, IResource
	{
		// Engine constants
		private const int NoiseDivisions = 64;
		/// <summary>Beam width on the wire is in tenths of a unit.</summary>
		public const float WidthScale = 0.1f;
		/// <summary>NoiseAmplitude is in hundredths.</summary>
		public const float NoiseScale = 0.01f;
		/// <summary>TextureScroll is in tenths of a texture-height per second.</summary>
		public const float ScrollScale = 0.1f;
		/// <summary>The framerate keyvalue is frames per 10 seconds.</summary>
		public const float FramerateScale = 0.1f;

		// Pre-multiplied so the TexturedAlpha shader's fixed lighting (ambient 0.8 + 0.5 * dot) comes out as exactly 1:
		// lightDirection * 0.4 gives dot = 0.4, and 0.5 * 0.4 + 0.8 = 1.
		private static readonly Vector3 UnlitNormal = new Vector3(0.2672612f, 0.5345225f, 0.8017837f) * 0.4f;

		private readonly RenderTexture _texture;
		private readonly Random _random = new Random();
		private readonly float[] _noise = new float[NoiseDivisions + 1];
		private readonly Dictionary<IViewport, Buffer> _buffers = new Dictionary<IViewport, Buffer>();
		private readonly BeamLocation _location = new BeamLocation();
		private BeamPair[] _pairs = new BeamPair[0];
		private double _seconds;

		/// <summary>BoltWidth keyvalue (raw).</summary>
		public float Width { get; set; } = 20;
		/// <summary>NoiseAmplitude keyvalue (raw).</summary>
		public float Noise { get; set; }
		/// <summary>TextureScroll keyvalue (raw).</summary>
		public float ScrollRate { get; set; } = 35;
		/// <summary>framerate keyvalue (raw, frames per 10 seconds).</summary>
		public float FrameRate { get; set; }
		/// <summary>framestart keyvalue.</summary>
		public float FrameStart { get; set; }
		/// <summary>Beam colour, 0-1 per channel.</summary>
		public Vector3 Colour { get; set; } = Vector3.One;
		/// <summary>renderamt / 255.</summary>
		public float Brightness { get; set; } = 1;
		/// <summary>The Ring spawnflag: the beam is drawn as a circle through both endpoints.</summary>
		public bool Ring { get; set; }
		public bool ShadeIn { get; set; }
		public bool ShadeOut { get; set; }

		public BeamRenderable(RenderTexture texture)
		{
			_texture = texture;
		}

		/// <summary>Replaces the set of start/end pairs that are drawn. An empty set hides the beam.</summary>
		public void SetPairs(IEnumerable<BeamPair> pairs)
		{
			var arr = pairs?.ToArray() ?? new BeamPair[0];
			_pairs = arr;
			if (arr.Length > 0) _location.Location = (arr[0].Start + arr[0].End) * 0.5f;
		}

		public void CreateResources(EngineInterface engine, RenderContext context)
		{
			// Buffers are created on demand per viewport
		}

		public void DestroyResources()
		{
			foreach (var b in _buffers.Values) b.Dispose();
			_buffers.Clear();
		}

		public void Dispose()
		{
			//
		}

		public IEnumerable<ILocation> GetLocationObjects(IPipeline pipeline, IViewport viewport)
		{
			yield return _location;
		}

		public bool ShouldRender(IPipeline pipeline, IViewport viewport)
		{
			return pipeline.Type == PipelineType.BeamAdditive
			       && viewport.Camera.Type == CameraType.Perspective
			       && _texture != null
			       && _pairs.Length > 0;
		}

		public void Update(long frame)
		{
			_seconds = frame / 1000.0;
			RegenerateNoise();
		}

		public void Render(RenderContext context, IPipeline pipeline, IViewport viewport, CommandList cl, ILocation locationObject)
		{
			Render(context, pipeline, viewport, cl);
		}

		public void Render(RenderContext context, IPipeline pipeline, IViewport viewport, CommandList cl)
		{
			var pairs = _pairs;
			if (_texture == null || pairs.Length == 0) return;

			var camera = viewport.Camera.Location;
			var verts = new List<VertexStandard>(256);
			var indices = new List<uint>(768);

			foreach (var pair in pairs)
			{
				AppendBeam(pair.Start, pair.End, camera, verts, indices);
			}

			if (verts.Count < 4 || indices.Count < 6) return;

			if (!_buffers.TryGetValue(viewport, out var buffer))
			{
				buffer = Engine.Interface.CreateBuffer();
				_buffers[viewport] = buffer;
			}

			buffer.Update(verts, indices);
			buffer.Bind(cl, 0);
			_texture.BindTo(cl, 1);
			cl.DrawIndexed((uint) buffer.IndexCount, 1, 0, 0, 0);
		}

		// Noise (engine: FracNoise, generated in powers of two and regenerated every frame)

		private void RegenerateNoise()
		{
			if (Noise <= 0) return;
			_noise[0] = 0;
			_noise[NoiseDivisions] = 0;
			FracNoise(0, NoiseDivisions);
		}

		private void FracNoise(int start, int divs)
		{
			if (divs < 2) return;
			var div2 = divs >> 1;
			var rand = (float) (_random.NextDouble() * 0.25 - 0.125);
			_noise[start + div2] = (_noise[start] + _noise[start + divs]) * 0.5f + divs * rand;
			if (div2 > 1)
			{
				FracNoise(start + div2, div2);
				FracNoise(start, div2);
			}
		}

		// Geometry

		private void AppendBeam(Vector3 start, Vector3 end, Vector3 camera, List<VertexStandard> verts, List<uint> indices)
		{
			var delta = end - start;
			var length = delta.Length();
			if (length < 0.1f) return;

			var width = Math.Max(Width, 0) * WidthScale;
			var amp = Math.Max(Noise, 0) * NoiseScale;
			var speed = ScrollRate * ScrollScale;

			// One segment per 4 units with noise, otherwise one per ~13 units, plus 3 (engine: R_BeamDraw)
			var segments = amp >= 0.5f ? (int) (length * 0.25f + 3f) : (int) (length * 0.075f + 3f);

			var frameCount = Math.Max(1, _texture.FrameCount);
			var frame = (int) (FrameStart + FrameRate * FramerateScale * _seconds) % frameCount;
			if (frame < 0) frame += frameCount;

			// Scroll: texture position wraps every full texture
			var scroll = speed * _seconds;
			var vOffset = (float) (scroll - Math.Floor(scroll));

			if (Ring) AppendRing(start, delta, length, segments, width, amp, camera, vOffset, frame, frameCount, verts, indices);
			else AppendSegments(start, delta, length, segments, width, amp, camera, vOffset, frame, frameCount, verts, indices);
		}

		private void AppendSegments(Vector3 start, Vector3 delta, float length, int segments, float width, float amp, Vector3 camera,
			float vOffset, int frame, int frameCount, List<VertexStandard> verts, List<uint> indices)
		{
			if (segments < 2) return;

			var maxWidth = width * 0.5f;
			var div = 1f / (segments - 1);
			if (maxWidth > 0 && length * div < maxWidth * 1.414f)
			{
				// Too many segments for this width would overlap, so use fewer
				segments = (int) (length / (maxWidth * 1.414f)) + 1;
				if (segments < 2) segments = 2;
			}
			if (segments > NoiseDivisions) segments = NoiseDivisions;
			div = 1f / (segments - 1);

			var length01 = length * 0.01f;
			var vStep = length01 * div; // one texture repeat per 100 units
			var scale = amp * length01 * 2f;
			var noiseStep = (int) ((NoiseDivisions - 1) * div * 65536f);
			var noiseIndex = 0;

			var dir = delta / length;
			var mid = start + delta * 0.5f;
			var forward = SafeNormalize(mid - camera, Vector3.UnitX);
			var perp = SafeNormalize(Vector3.Cross(forward, dir), Vector3.UnitZ);

			var pos = new Vector3[segments];
			for (var i = 0; i < segments; i++)
			{
				var p = start + delta * (i * div);
				if (scale != 0)
				{
					p += perp * (_noise[noiseIndex >> 16] * scale);
				}
				pos[i] = p;
				noiseIndex += noiseStep;
			}

			// Normal of each piece, perpendicular to the piece and to the view, used to fatten the beam
			var pieceNormals = new Vector3[segments - 1];
			for (var k = 0; k < segments - 1; k++)
			{
				var tangent = pos[k] - pos[k + 1];
				var toBeam = pos[k] - camera;
				pieceNormals[k] = SafeNormalize(Vector3.Cross(tangent, toBeam), Vector3.UnitZ);
			}

			var baseIndex = (uint) verts.Count;
			for (var i = 0; i < segments; i++)
			{
				Vector3 normal;
				if (i == 0) normal = pieceNormals[0];
				else if (i == segments - 1) normal = pieceNormals[segments - 2];
				else normal = SafeNormalize((pieceNormals[i - 1] + pieceNormals[i]) * 0.5f, pieceNormals[i]);

				var fraction = i * div;
				var shade = ShadeFactor(fraction);
				var tint = new Vector4(Colour * (Brightness * shade), 1f);
				var v = vOffset + i * vStep;

				verts.Add(MakeVertex(pos[i] + normal * width, 0f, v, frame, frameCount, tint));
				verts.Add(MakeVertex(pos[i] - normal * width, 1f, v, frame, frameCount, tint));
			}
			AddStripIndices(baseIndex, segments, indices);
		}

		private void AppendRing(Vector3 start, Vector3 delta, float length, int segments, float width, float amp, Vector3 camera,
			float vOffset, int frame, int frameCount, List<VertexStandard> verts, List<uint> indices)
		{
			if (segments < 2) return;
			var ringSegments = (int) (segments * Math.PI);
			if (ringSegments > NoiseDivisions * 8) ringSegments = NoiseDivisions * 8;

			var ringLength = length * 0.01f * (float) Math.PI;
			if (ringLength < 0.5f) ringLength = 0.5f; // don't lose all of the noise/texture on short beams

			var div = 1f / (ringSegments - 1);
			var vStep = ringLength * div / 8f;
			var scale = amp * ringLength / 8f;
			var noiseStep = (int) ((NoiseDivisions - 1) * div * 65536f) * 8;
			var noiseIndex = 0;

			var half = delta * 0.5f;
			var center = start + half;
			var xAxis = half;
			var radius = xAxis.Length();
			var yAxis = new Vector3(xAxis.Y, -xAxis.X, 0);
			yAxis = yAxis.LengthSquared() > 1e-6f ? Vector3.Normalize(yAxis) * radius : Vector3.Zero;

			var forward = SafeNormalize(center - camera, Vector3.UnitX);
			var right = SafeNormalize(Vector3.Cross(forward, Vector3.UnitZ), Vector3.UnitY);
			var up = Vector3.UnitZ;

			var baseIndex = (uint) verts.Count;
			var vLast = vOffset;
			var previous = Vector3.Zero;
			var pairCount = 0;
			var tint = new Vector4(Colour * Brightness, 1f);

			for (var i = 0; i < ringSegments + 1; i++)
			{
				var fraction = i * div;
				var angle = fraction * (float) (Math.PI * 2);
				var point = center + xAxis * (float) Math.Sin(angle) + yAxis * (float) Math.Cos(angle);

				// Distort using noise, then again along the other axis so the ring doesn't look flat
				var n = _noise[(noiseIndex >> 16) & (NoiseDivisions - 1)] * scale;
				point += up * n;
				point += right * (n * (float) Math.Cos(fraction * Math.PI * 24 + _seconds));

				if (i != 0)
				{
					var normal = SafeNormalize(Vector3.Cross(point - previous, point - camera), Vector3.UnitZ);
					vLast += vStep;
					verts.Add(MakeVertex(point + normal * width, 0f, vLast, frame, frameCount, tint));
					verts.Add(MakeVertex(point - normal * width, 1f, vLast, frame, frameCount, tint));
					pairCount++;
				}

				previous = point;
				noiseIndex += noiseStep;
			}

			AddStripIndices(baseIndex, pairCount, indices);
		}

		private float ShadeFactor(float fraction)
		{
			if (ShadeIn && ShadeOut) return fraction < 0.5f ? fraction : 1f - fraction;
			if (ShadeIn) return fraction;
			if (ShadeOut) return 1f - fraction;
			return 1f;
		}

		private static VertexStandard MakeVertex(Vector3 position, float u, float v, int frame, int frameCount, Vector4 tint)
		{
			// Multi-frame sprites are packed side by side, so U picks the frame column. Nudged in from the edges
			// so neighbouring frames don't bleed into each other.
			var uu = (frame + 0.001f + u * 0.998f) / frameCount;
			return new VertexStandard
			{
				Position = position,
				Normal = UnlitNormal,
				Colour = Vector4.One,
				Texture = new Vector2(uu, v),
				Tint = tint,
				Flags = VertexFlags.None
			};
		}

		/// <summary>Triangles for a strip of vertex pairs (left, right, left, right...).</summary>
		private static void AddStripIndices(uint baseIndex, int pairCount, List<uint> indices)
		{
			for (var i = 0; i < pairCount - 1; i++)
			{
				var a = baseIndex + (uint) (i * 2);
				indices.Add(a);
				indices.Add(a + 1);
				indices.Add(a + 2);
				indices.Add(a + 1);
				indices.Add(a + 3);
				indices.Add(a + 2);
			}
		}

		private static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
		{
			var len = v.Length();
			return len > 1e-6f ? v / len : fallback;
		}

		private class BeamLocation : ILocation
		{
			public Vector3 Location { get; set; }
		}
	}
}
