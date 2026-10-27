using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    /// <summary>
    /// Per-axis scale dialog opened with Alt+E. Each axis takes a factor from 0 to 1 (1 = unchanged).
    /// Unlocked axes are linked and always share one value. A locked axis is not used for scaling:
    /// it is pinned to 1 and disabled until it is unlocked, and then it rejoins the shared value.
    /// </summary>
    public class VertexScaleDialog : Form
    {
        private readonly NumericUpDown[] _values = new NumericUpDown[3];
        private readonly CheckBox[] _locks = new CheckBox[3];
        private decimal _linked = 1m;
        private bool _freeze;

        public event EventHandler FactorsChanged;

        public VertexScaleDialog()
        {
            Text = "Vertex Scale";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(230, 150);

            var axes = new[] { "X", "Y", "Z" };
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var y = 12 + i * 32;

                var label = new Label { Text = axes[i], Location = new Point(12, y + 4), AutoSize = true };
                _values[i] = new NumericUpDown
                {
                    Location = new Point(40, y),
                    Size = new Size(80, 22),
                    Minimum = 0,
                    Maximum = 1,
                    DecimalPlaces = 2,
                    Increment = 0.05m,
                    Value = 1m
                };
                _locks[i] = new CheckBox { Text = "Lock", Location = new Point(132, y + 2), AutoSize = true };

                _values[i].ValueChanged += (s, e) => OnValueChanged(index);
                _locks[i].CheckedChanged += (s, e) => OnLockChanged(index);

                Controls.Add(label);
                Controls.Add(_values[i]);
                Controls.Add(_locks[i]);
            }

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(56, 116), Size = new Size(75, 26) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(139, 116), Size = new Size(75, 26) };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>
        /// The scale factor for X, Y and Z. Locked axes are always 1.
        /// </summary>
        public float[] GetFactors()
        {
            return new[] { Factor(0), Factor(1), Factor(2) };
        }

        private float Factor(int i)
        {
            return _locks[i].Checked ? 1f : (float) _values[i].Value;
        }

        private void OnValueChanged(int index)
        {
            if (_freeze || _locks[index].Checked) return;

            // Changing one unlocked axis sets the shared value for every unlocked axis
            _linked = _values[index].Value;
            _freeze = true;
            for (var j = 0; j < 3; j++)
            {
                if (!_locks[j].Checked) _values[j].Value = _linked;
            }
            _freeze = false;

            FactorsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnLockChanged(int index)
        {
            if (_freeze) return;

            _freeze = true;
            if (_locks[index].Checked)
            {
                // Locked: the axis is not in use, so it is pinned to 1 and disabled
                _values[index].Value = 1m;
                _values[index].Enabled = false;
            }
            else
            {
                // Unlocked: the axis rejoins the shared value
                _values[index].Enabled = true;
                _values[index].Value = _linked;
            }
            _freeze = false;

            FactorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
