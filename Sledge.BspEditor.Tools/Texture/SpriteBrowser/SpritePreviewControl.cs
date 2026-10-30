using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Sledge.Providers.Texture.Spr;

namespace Sledge.BspEditor.Tools.Texture.SpriteBrowser
{
	/// <summary>
	/// Shows one sprite at a time: zoom (fit or whole-number), pan, background, frame stepping and playback.
	/// </summary>
	public class SpritePreviewControl : Control
	{
		public static readonly string[] BackgroundNames = { "Dark", "Black", "Light", "Checker" };

		private static readonly Color Dark = Color.FromArgb(64, 66, 72);
		private static readonly Color CheckerA = Color.FromArgb(96, 96, 100);
		private static readonly Color CheckerB = Color.FromArgb(70, 70, 74);

		private SprFile _sprite;
		private string _message = "Select a sprite to preview";
		private int _frame;
		private int _zoom; // 0 = fit
		private int _background;
		private bool _outline;
		private bool _playing;
		private int _fps = 10;
		private PointF _pan;
		private Point _dragStart;
		private PointF _panStart;
		private bool _dragging;

		private readonly Timer _timer;
		private readonly Stopwatch _clock = new Stopwatch();
		private double _frameTime; // seconds spent on the current frame

		private Bitmap _composed;
		private int _composedFrame = -1;
		private Color _composedBackground;
		private SprFile _composedSprite;

		/// <summary>Raised when the frame changes because of playback.</summary>
		public event EventHandler<int> FrameChanged;

		/// <summary>Raised when the zoom changes (wheel or code), with the new zoom value (0 = fit).</summary>
		public event EventHandler<int> ZoomChanged;

		public SpritePreviewControl()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
			TabStop = true;
			BackColor = Dark;
			_timer = new Timer { Interval = 15 };
			_timer.Tick += (s, e) => Tick();
		}

		public int FrameCount => _sprite?.Frames.Count ?? 0;
		public int Frame => _frame;
		public SprFile Sprite => _sprite;

		public bool Playing
		{
			get => _playing;
			set
			{
				_playing = value && FrameCount > 1;
				_frameTime = 0;
				if (_playing)
				{
					_clock.Restart();
					_timer.Start();
				}
				else
				{
					_clock.Reset();
					_timer.Stop();
				}
			}
		}

		public int Fps
		{
			get => _fps;
			set => _fps = Math.Max(1, Math.Min(60, value));
		}

		public int Zoom
		{
			get => _zoom;
			set
			{
				if (_zoom == value) return;
				_zoom = value;
				_pan = PointF.Empty;
				Invalidate();
				ZoomChanged?.Invoke(this, _zoom);
			}
		}

		public int Background
		{
			get => _background;
			set
			{
				_background = Math.Max(0, Math.Min(BackgroundNames.Length - 1, value));
				Invalidate();
			}
		}

		public bool ShowOutline
		{
			get => _outline;
			set
			{
				_outline = value;
				Invalidate();
			}
		}

		public void SetMessage(string message)
		{
			Playing = false;
			DisposeSprite();
			_message = message;
			Invalidate();
		}

		public void SetSprite(SprFile sprite)
		{
			Playing = false;
			DisposeSprite();
			_sprite = sprite;
			_frame = 0;
			_pan = PointF.Empty;
			_message = null;
			Invalidate();
		}

		public void SetFrame(int frame)
		{
			if (_sprite == null) return;
			_frame = Math.Max(0, Math.Min(frame, _sprite.Frames.Count - 1));
			_frameTime = 0;
			Invalidate();
		}

		public void ResetView()
		{
			_pan = PointF.Empty;
			Zoom = 0;
			Invalidate();
		}

		private void DisposeSprite()
		{
			_composed?.Dispose();
			_composed = null;
			_composedFrame = -1;
			_composedSprite = null;
			_sprite?.Dispose();
			_sprite = null;
		}

		private void Tick()
		{
			if (!_playing || _sprite == null || _sprite.Frames.Count < 2) return;

			var elapsed = _clock.Elapsed.TotalSeconds;
			_clock.Restart();
			_frameTime += elapsed;

			var advanced = false;
			// Guard against a huge jump (the window was dragged, the machine stalled): never loop for long
			for (var guard = 0; guard < 64; guard++)
			{
				var duration = _sprite.Frames[_frame].Duration > 0 ? _sprite.Frames[_frame].Duration : 1.0 / _fps;
				if (_frameTime < duration) break;
				_frameTime -= duration;
				_frame = (_frame + 1) % _sprite.Frames.Count;
				advanced = true;
			}

			if (advanced)
			{
				Invalidate();
				FrameChanged?.Invoke(this, _frame);
			}
		}

		private Color BackgroundColor()
		{
			switch (_background)
			{
				case 1: return Color.Black;
				case 2: return Color.FromArgb(214, 214, 218);
				case 3: return CheckerB;
				default: return Dark;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			var g = e.Graphics;
			DrawBackground(g);

			if (_sprite == null || _sprite.Frames.Count == 0)
			{
				using (var brush = new SolidBrush(Color.FromArgb(200, 200, 206)))
				using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
				{
					g.DrawString(_message ?? "", Font, brush, ClientRectangle, format);
				}
				return;
			}

			var frame = _sprite.Frames[Math.Max(0, Math.Min(_frame, _sprite.Frames.Count - 1))];
			var zoom = EffectiveZoom(frame);
			var w = frame.Width * zoom;
			var h = frame.Height * zoom;
			var x = (ClientSize.Width - w) / 2f + _pan.X;
			var y = (ClientSize.Height - h) / 2f + _pan.Y;
			var dest = new RectangleF(x, y, w, h);

			var image = GetImage(frame);
			g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			g.DrawImage(image, dest);

			if (_outline)
			{
				using (var pen = new Pen(Color.FromArgb(220, 255, 200, 40)) { DashStyle = DashStyle.Dash })
				{
					g.DrawRectangle(pen, dest.X, dest.Y, dest.Width - 1, dest.Height - 1);
					var cx = ClientSize.Width / 2f + _pan.X;
					var cy = ClientSize.Height / 2f + _pan.Y;
					g.DrawLine(pen, cx - 6, cy, cx + 6, cy);
					g.DrawLine(pen, cx, cy - 6, cx, cy + 6);
				}
			}

			var label = (_zoom == 0 ? "Fit " : "") + Math.Round(zoom * 100) + "%   " + frame.Width + " x " + frame.Height;
			using (var brush = new SolidBrush(Color.FromArgb(220, 230, 230, 236)))
			using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
			{
				var pos = new PointF(8, ClientSize.Height - Font.Height - 6);
				g.DrawString(label, Font, shadow, pos.X + 1, pos.Y + 1);
				g.DrawString(label, Font, brush, pos);
			}
		}

		private void DrawBackground(Graphics g)
		{
			if (_background != 3)
			{
				using (var brush = new SolidBrush(BackgroundColor())) g.FillRectangle(brush, ClientRectangle);
				return;
			}

			const int cell = 12;
			using (var a = new SolidBrush(CheckerA))
			using (var b = new SolidBrush(CheckerB))
			{
				for (var yy = 0; yy * cell < ClientSize.Height; yy++)
				{
					for (var xx = 0; xx * cell < ClientSize.Width; xx++)
					{
						g.FillRectangle(((xx + yy) & 1) == 0 ? a : b, xx * cell, yy * cell, cell, cell);
					}
				}
			}
		}

		/// <summary>The picture to draw: additive sprites are added to a solid background first, the rest keep their alpha.</summary>
		private Image GetImage(SprFrame frame)
		{
			if (_sprite.Header.RenderMode != SpriteRenderMode.Additive) return frame.Image;

			var bg = _background == 3 ? CheckerB : BackgroundColor();
			if (_composed != null && _composedSprite == _sprite && _composedFrame == _frame && _composedBackground == bg) return _composed;

			_composed?.Dispose();
			_composed = SprFile.Compose(frame, SpriteRenderMode.Additive, bg);
			_composedSprite = _sprite;
			_composedFrame = _frame;
			_composedBackground = bg;
			return _composed;
		}

		private float EffectiveZoom(SprFrame frame)
		{
			if (_zoom > 0) return _zoom;

			var margin = 24f;
			var availW = Math.Max(1f, ClientSize.Width - margin * 2);
			var availH = Math.Max(1f, ClientSize.Height - margin * 2);
			var fit = Math.Min(availW / frame.Width, availH / frame.Height);
			// Enlarge small sprites in whole steps so the pixels stay crisp; shrink big ones smoothly
			return fit >= 1 ? Math.Max(1f, (float) Math.Floor(fit)) : fit;
		}

		private static readonly int[] ZoomSteps = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };

		/// <summary>Mouse wheel: zoom in or out through the whole-number steps, "fit" being the lowest.</summary>
		public void ApplyWheel(int delta, Point location)
		{
			if (_sprite == null || _sprite.Frames.Count == 0) return;

			var frame = _sprite.Frames[Math.Max(0, Math.Min(_frame, _sprite.Frames.Count - 1))];
			var effective = EffectiveZoom(frame);

			if (delta > 0)
			{
				// A big sprite that "fit" shrinks goes to 1:1 first
				if (_zoom == 0 && effective < 1f) Zoom = 1;
				else Zoom = NextStep(_zoom > 0 ? _zoom : Math.Max(1, (int) Math.Round(effective)), 1);
			}
			else if (_zoom > 0)
			{
				Zoom = NextStep(_zoom, -1); // 0 means fit
			}
		}

		private static int NextStep(int current, int direction)
		{
			if (direction > 0)
			{
				foreach (var z in ZoomSteps) if (z > current) return z;
				return ZoomSteps[ZoomSteps.Length - 1];
			}

			for (var i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < current) return ZoomSteps[i];
			return 0;
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			Focus();
			if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
			{
				_dragging = true;
				_dragStart = e.Location;
				_panStart = _pan;
				Cursor = Cursors.SizeAll;
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!_dragging) return;
			_pan = new PointF(_panStart.X + e.X - _dragStart.X, _panStart.Y + e.Y - _dragStart.Y);
			Invalidate();
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_dragging = false;
			Cursor = Cursors.Default;
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			base.OnMouseDoubleClick(e);
			ResetView();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_timer.Dispose();
				DisposeSprite();
			}
			base.Dispose(disposing);
		}
	}
}
