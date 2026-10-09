using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// A single custom entity colour override row: name, colour picker and remove button.
    /// </summary>
    public partial class EntityColourRow : UserControl
    {
        public event EventHandler RemoveRequested;
        public event EventHandler ColourChanged;
        public event EventHandler NameChanged;

        public EntityColourRow()
        {
            InitializeComponent();
            ColorPanel.Click += ColorPanel_Click;
            RemoveButton.Click += RemoveButton_Click;
            EntityNameTextBox.TextChanged += EntityNameTextBox_TextChanged;
        }

        /// <summary>
        /// The entity name to match.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EntityName
        {
            get => EntityNameTextBox.Text;
            set => EntityNameTextBox.Text = value;
        }

        /// <summary>
        /// The colour to use for the matched entity.
        /// </summary>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Colour
        {
            get => ColorPanel.BackColor;
            set => ColorPanel.BackColor = value;
        }

        private void ColorPanel_Click(object sender, EventArgs e)
        {
            using (var cp = new ColorDialog { Color = ColorPanel.BackColor, SolidColorOnly = true })
            {
                if (cp.ShowDialog() == DialogResult.OK)
                {
                    ColorPanel.BackColor = cp.Color;
                    ColourChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            RemoveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EntityNameTextBox_TextChanged(object sender, EventArgs e)
        {
            NameChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
