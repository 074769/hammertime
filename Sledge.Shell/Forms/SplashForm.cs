using System.Drawing;
using System.Windows.Forms;

namespace Sledge.Shell.Forms
{
    /// <summary>
    /// Borderless window shown while Hammertime starts. Startup closes it once the shell is ready.
    /// </summary>
    internal class SplashForm : Form
    {
        private readonly Label _status;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 32, 36);
            ForeColor = Color.White;
            ClientSize = new Size(420, 140);

            Paint += (s, e) => e.Graphics.DrawRectangle(Pens.Gray, 0, 0, Width - 1, Height - 1);

            var title = new Label
            {
                Text = "Hammertime",
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 28),
                Size = new Size(ClientSize.Width, 50)
            };

            _status = new Label
            {
                Text = "Starting...",
                ForeColor = Color.Silver,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(0, 92),
                Size = new Size(ClientSize.Width, 24)
            };

            Controls.Add(title);
            Controls.Add(_status);
        }

        /// <summary>
        /// Update the status line and repaint it straight away, even while the UI thread is busy loading.
        /// </summary>
        public void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => SetStatus(text)));
                return;
            }

            _status.Text = text;
            Refresh();
        }
    }
}
