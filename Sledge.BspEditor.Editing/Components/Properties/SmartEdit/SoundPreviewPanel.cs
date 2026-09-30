using System;
using System.Drawing;
using System.Media;
using System.Windows.Forms;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
	/// <summary>
	/// Plays rendered wav data. Only one preview plays at a time.
	/// </summary>
	internal static class SoundPreviewPlayer
	{
		private static SoundPlayer _player;
		private static System.IO.MemoryStream _stream;

		public static void Play(byte[] wav)
		{
			Stop();
			_stream = new System.IO.MemoryStream(wav);
			_player = new SoundPlayer(_stream);
			_player.Play(); // asynchronous, the stream must stay alive until stopped
		}

		public static void Stop()
		{
			try { _player?.Stop(); } catch { /* ignore */ }
			_player?.Dispose();
			_stream?.Dispose();
			_player = null;
			_stream = null;
		}
	}

	/// <summary>
	/// A Preview/Stop button and a preview volume slider, shown under a sound choice.
	/// The volume only affects the preview, it is never written to the entity.
	/// </summary>
	internal class SoundPreviewPanel : Panel
	{
		private static int _lastVolume = 80;

		private readonly Button _button;
		private readonly TrackBar _volume;
		private readonly Label _percent;
		private readonly Label _status;
		private readonly Timer _endTimer;
		private bool _playing;

		public event EventHandler PreviewClicked;

		public SoundPreviewPanel()
		{
			Size = new Size(255, 58);

			_button = new Button { Text = "Preview", Location = new Point(0, 0), Size = new Size(75, 25), UseVisualStyleBackColor = true };
			_button.Click += ButtonClicked;
			Controls.Add(_button);

			_volume = new TrackBar
			{
				Location = new Point(80, 0),
				Size = new Size(130, 25),
				Minimum = 0,
				Maximum = 100,
				TickStyle = TickStyle.None,
				SmallChange = 5,
				LargeChange = 10,
				Value = _lastVolume
			};
			_volume.ValueChanged += (s, e) =>
			{
				_lastVolume = _volume.Value;
				_percent.Text = _volume.Value + "%";
			};
			Controls.Add(_volume);

			_percent = new Label { Text = _volume.Value + "%", Location = new Point(212, 6), Size = new Size(40, 15) };
			Controls.Add(_percent);

			_status = new Label { Location = new Point(0, 29), Size = new Size(255, 28), ForeColor = SystemColors.GrayText };
			Controls.Add(_status);

			_endTimer = new Timer();
			_endTimer.Tick += (s, e) => StopPlaying();
		}

		/// <summary>Preview volume, 0 - 1</summary>
		public double Volume => _volume.Value / 100.0;

		public bool PreviewEnabled
		{
			get => _button.Enabled;
			set
			{
				_button.Enabled = value;
				if (!value) StopPlaying();
			}
		}

		public string Status
		{
			get => _status.Text;
			set => _status.Text = value ?? "";
		}

		/// <summary>Call after starting playback so the button turns into Stop and resets when the clip ends.</summary>
		public void BeginPlaying(double seconds)
		{
			_playing = true;
			_button.Text = "Stop";
			_endTimer.Stop();
			_endTimer.Interval = Math.Max(100, (int) (seconds * 1000) + 150);
			_endTimer.Start();
		}

		public void StopPlaying()
		{
			if (_playing) SoundPreviewPlayer.Stop();
			_playing = false;
			_endTimer.Stop();
			_button.Text = "Preview";
		}

		private void ButtonClicked(object sender, EventArgs e)
		{
			if (_playing)
			{
				StopPlaying();
				return;
			}
			Status = "";
			PreviewClicked?.Invoke(this, EventArgs.Empty);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				StopPlaying();
				_endTimer.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
