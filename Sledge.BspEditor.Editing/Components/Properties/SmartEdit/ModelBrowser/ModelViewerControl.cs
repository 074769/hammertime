using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.Common.Logging;
using Sledge.Providers.Model.Mdl10.Format;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit.ModelBrowser
{
	/// <summary>
	/// An interactive, software rendered preview of a .mdl: drag to orbit, wheel to zoom (towards the cursor),
	/// right / middle / shift+drag to pan, double click to reset. Plays sequences.
	/// Rendering runs on a background task (one at a time); while interacting it renders without anti-aliasing and
	/// then re-renders at full quality once the input settles.
	/// </summary>
	public class ModelViewerControl : Control
	{
		private const float DefaultAzimuth = 35f;
		private const float DefaultElevation = 22f;
		private const string Hint = "Drag: rotate   Wheel: zoom   Right / Shift+drag: pan   Double-click: reset";

		private MdlFile _mdl;
		private ModelScene _scene;
		private Bitmap _image;
		private string _message = "Select a model to preview";

		private float _azimuth = DefaultAzimuth;
		private float _elevation = DefaultElevation;
		private float _zoom = 1f;
		private PointF _pan;
		private ModelShading _shading = ModelShading.Smooth;
		private int _skin;
		private int[] _body = new int[0];

		private int _sequence;
		private int _frame;
		private float _sub;
		private double _playTime;
		private bool _playing;

		private readonly Timer _playTimer;
		private readonly Timer _settleTimer;
		private readonly Stopwatch _clock = new Stopwatch();
		private double _lastTick;

		private bool _rendering;
		private bool _dirty;
		private bool _final;

		private bool _dragging;
		private MouseButtons _dragButton;
		private Point _last;

		private readonly Font _hintFont;

		/// <summary>Raised during playback with the new frame number.</summary>
		public event EventHandler<int> FrameChanged;

		public ModelViewerControl()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
			BackColor = Color.FromArgb(64, 66, 72);
			ForeColor = Color.Gainsboro;
			_hintFont = new Font(FontFamily.GenericSansSerif, 7.5f);

			_playTimer = new Timer { Interval = 30 };
			_playTimer.Tick += PlayTick;

			_settleTimer = new Timer { Interval = 220 };
			_settleTimer.Tick += (s, e) =>
			{
				_settleTimer.Stop();
				if (_scene == null) return;
				_final = true;
				if (_playing) return; // playback stays at fast quality
				if (_rendering) _dirty = true;
				else RunRender();
			};
		}

		#region Public API

		public ModelShading Shading
		{
			get { return _shading; }
			set
			{
				_shading = value;
				Request(true);
			}
		}

		public int Skin
		{
			get { return _skin; }
			set
			{
				_skin = value;
				Request(true);
			}
		}

		public int Frame
		{
			get { return _frame; }
		}

		public int FrameCount
		{
			get
			{
				return _mdl != null && ModelScene.IsAnimatable(_mdl, _sequence) ? _mdl.Sequences[_sequence].Header.NumFrames : 0;
			}
		}

		public bool Playing
		{
			get { return _playing; }
			set
			{
				value = value && FrameCount > 1;
				if (value == _playing) return;
				_playing = value;
				if (_playing)
				{
					_playTime = _frame / Fps();
					_clock.Restart();
					_lastTick = 0;
					_playTimer.Start();
				}
				else
				{
					_playTimer.Stop();
					_clock.Stop();
					_sub = 0f;
					Request(true);
				}
			}
		}

		public void SetModel(MdlFile mdl)
		{
			StopPlayback();
			_mdl = mdl;
			_scene = new ModelScene(mdl);
			_body = new int[mdl.BodyParts != null ? mdl.BodyParts.Count : 0];
			_sequence = 0;
			_frame = 0;
			_sub = 0f;
			_pan = PointF.Empty;
			_message = null;
			Request(false);
		}

		public void SetMessage(string message)
		{
			StopPlayback();
			_scene = null;
			_mdl = null;
			_message = message;
			DisposeImage();
			Invalidate();
		}

		public void SetSequence(int sequence)
		{
			var wasPlaying = _playing;
			StopPlayback();
			_sequence = sequence;
			_frame = 0;
			_sub = 0f;
			Request(true);
			if (wasPlaying) Playing = true;
		}

		public void SetFrame(int frame)
		{
			StopPlayback();
			var count = FrameCount;
			_frame = count > 0 ? Math.Max(0, Math.Min(frame, count - 1)) : 0;
			_sub = 0f;
			Request(true);
		}

		public void SetBodyModel(int part, int model)
		{
			if (part < 0 || part >= _body.Length) return;
			_body[part] = model;
			Request(true);
		}

		public void ResetView()
		{
			_azimuth = DefaultAzimuth;
			_elevation = DefaultElevation;
			_zoom = 1f;
			_pan = PointF.Empty;
			Request(true);
		}

		/// <summary>
		/// Zoom towards a point (client coordinates). Called by the dialog's mouse wheel filter.
		/// </summary>
		public void ApplyWheel(int delta, Point location)
		{
			var newZoom = Math.Max(0.1f, Math.Min(50f, _zoom * (float) Math.Pow(1.0015, delta)));
			var ratio = newZoom / _zoom;
			var mx = location.X - ClientSize.Width / 2f;
			var my = location.Y - ClientSize.Height / 2f;
			_pan = new PointF(mx + (_pan.X - mx) * ratio, my + (_pan.Y - my) * ratio);
			_zoom = newZoom;
			Request(true);
		}

		#endregion

		#region Playback

		private float Fps()
		{
			try
			{
				var fps = _mdl.Sequences[_sequence].Header.Framerate;
				return fps > 0.01f ? fps : 30f;
			}
			catch
			{
				return 30f;
			}
		}

		private void StopPlayback()
		{
			if (!_playing) return;
			_playing = false;
			_playTimer.Stop();
			_clock.Stop();
			_sub = 0f;
		}

		private void PlayTick(object sender, EventArgs e)
		{
			var count = FrameCount;
			if (count <= 1)
			{
				Playing = false;
				return;
			}

			var now = _clock.Elapsed.TotalSeconds;
			_playTime += now - _lastTick;
			_lastTick = now;

			var f = _playTime * Fps();
			var whole = (long) Math.Floor(f);
			_frame = (int) (whole % count);
			_sub = (float) (f - whole);

			var handler = FrameChanged;
			if (handler != null) handler(this, _frame);
			Request(true);
		}

		#endregion

		#region Rendering

		private void Request(bool fast)
		{
			if (_scene == null || !IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

			if (fast)
			{
				_final = false;
				_settleTimer.Stop();
				_settleTimer.Start();
			}
			else
			{
				_final = true;
			}

			if (_rendering) _dirty = true;
			else RunRender();
		}

		private async void RunRender()
		{
			if (_rendering) return;
			_rendering = true;
			try
			{
				do
				{
					_dirty = false;
					var scene = _scene;
					if (scene == null || IsDisposed || ClientSize.Width <= 0 || ClientSize.Height <= 0) break;

					var options = new ModelRenderOptions
					{
						Width = ClientSize.Width,
						Height = ClientSize.Height,
						Background = BackColor,
						Azimuth = _azimuth,
						Elevation = _elevation,
						Zoom = _zoom,
						Pan = _pan,
						Shading = _shading,
						SuperSample = _final && !_playing ? 2 : 1,
						Sequence = _sequence,
						Frame = _frame,
						SubFrame = _sub,
						Skin = _skin,
						Body = (int[]) _body.Clone()
					};

					Bitmap bmp = null;
					await Task.Run(() =>
					{
						try
						{
							bmp = scene.Render(options);
						}
						catch (Exception ex)
						{
							Log.Error(nameof(ModelViewerControl), "Unable to render model preview", ex);
						}
					});

					if (IsDisposed || scene != _scene)
					{
						if (bmp != null) bmp.Dispose();
						continue;
					}

					DisposeImage();
					_image = bmp;
					_message = bmp == null ? "No preview available" : null;
					Invalidate();
				} while (_dirty && !IsDisposed);
			}
			finally
			{
				_rendering = false;
			}
		}

		private void DisposeImage()
		{
			var old = _image;
			_image = null;
			if (old != null) old.Dispose();
		}

		#endregion

		#region Input

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (_scene == null) return;
			_dragging = true;
			_dragButton = e.Button;
			_last = e.Location;
			Capture = true;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!_dragging) return;

			var dx = e.X - _last.X;
			var dy = e.Y - _last.Y;
			_last = e.Location;
			if (dx == 0 && dy == 0) return;

			var pan = _dragButton == MouseButtons.Right || _dragButton == MouseButtons.Middle
			          || (_dragButton == MouseButtons.Left && (ModifierKeys & Keys.Shift) == Keys.Shift);
			if (pan)
			{
				_pan = new PointF(_pan.X + dx, _pan.Y + dy);
			}
			else if (_dragButton == MouseButtons.Left)
			{
				_azimuth = (_azimuth - dx * 0.6f + 360f) % 360f;
				_elevation = Math.Max(-89f, Math.Min(89f, _elevation + dy * 0.5f));
			}
			Request(true);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_dragging = false;
			Capture = false;
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			if (e.Button == MouseButtons.Left && _scene != null) ResetView();
		}

		#endregion

		#region Control

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Request(false);
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			Request(true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			g.Clear(BackColor);

			if (_image != null)
			{
				if (_image.Width == ClientSize.Width && _image.Height == ClientSize.Height) g.DrawImageUnscaled(_image, 0, 0);
				else g.DrawImage(_image, ClientRectangle);

				using (var brush = new SolidBrush(Color.FromArgb(150, 220, 220, 220)))
				{
					g.DrawString(Hint, _hintFont, brush, 8, ClientSize.Height - _hintFont.Height - 6);
				}
			}

			if (_message != null)
			{
				using (var brush = new SolidBrush(ForeColor))
				using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
				{
					g.DrawString(_message, Font, brush, ClientRectangle, format);
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_playTimer.Dispose();
				_settleTimer.Dispose();
				_hintFont.Dispose();
				DisposeImage();
			}
			base.Dispose(disposing);
		}

		#endregion
	}
}
