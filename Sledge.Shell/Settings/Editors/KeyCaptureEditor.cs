using System;
using System.Windows.Forms;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Settings.Editors
{
    /// <summary>
    /// An editor for <see cref="Keys"/> settings that lets the user bind a key by pressing it,
    /// instead of picking it from a (very large) dropdown. Click the box then press a key.
    /// </summary>
    public partial class KeyCaptureEditor : UserControl, ISettingEditor
    {
        public event EventHandler<SettingKey> OnValueChanged;

        private bool _capturing;
        private string _value;

        string ISettingEditor.Label
        {
            get => KeyBox.Text;
            set
            {
                _label.Text = value;
                KeyBox.Left = _label.Right + 6;
                KeyBox.Width = Math.Max(80, ClientSize.Width - KeyBox.Left - 6);
            }
        }

        // Keys settings are stored by their enum name (e.g. "W"), matching the old EnumEditor format.
        public object Value
        {
            get => _value;
            set
            {
                var s = Convert.ToString(value);
                _value = s;
                _capturing = false;
                UpdateText();
            }
        }

        public object Control => this;
        public SettingKey Key { get; set; }

        public KeyCaptureEditor()
        {
            InitializeComponent();

            _value = "None";
            UpdateText();

            KeyBox.GotFocus += (o, e) => { _capturing = true; UpdateText(); };
            KeyBox.LostFocus += (o, e) => { _capturing = false; UpdateText(); };
            KeyBox.PreviewKeyDown += KeyBox_PreviewKeyDown;
            KeyBox.KeyDown += KeyBox_KeyDown;
        }

        private void UpdateText()
        {
            KeyBox.Text = _capturing ? "Press a key..." : (_value ?? "None");
        }

        private void KeyBox_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            // Make sure arrow/tab/escape reach the control instead of being swallowed.
            e.IsInputKey = true;
        }

        private void KeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturing) return;

            e.Handled = true;
            e.SuppressKeyPress = true;

            // Ignore lone modifier presses; only commit a real key (optionally with modifiers).
            switch (e.KeyCode)
            {
                case Keys.ShiftKey:
                case Keys.ControlKey:
                case Keys.Menu:
                case Keys.LWin:
                case Keys.RWin:
                    return;
            }

            _value = e.KeyCode.ToString();
            _capturing = false;
            UpdateText();
            OnValueChanged?.Invoke(this, Key);

            // Move focus away so the capture stops (and so it doesn't eat the next tab).
            if (Parent != null) Parent.Focus();
        }
    }
}
