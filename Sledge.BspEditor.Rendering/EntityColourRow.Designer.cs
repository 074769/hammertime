using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Rendering
{
    partial class EntityColourRow
    {
        private IContainer components = null;

        public TextBox EntityNameTextBox;
        public Panel ColorPanel;
        public Button RemoveButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // ColorPanel
            // 
            ColorPanel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            ColorPanel.BackColor = Color.Gray;
            ColorPanel.Cursor = System.Windows.Forms.Cursors.Hand;
            ColorPanel.Location = new System.Drawing.Point(3, 3);
            ColorPanel.Name = "ColorPanel";
            ColorPanel.Size = new System.Drawing.Size(30, 22);
            ColorPanel.TabIndex = 0;
            ColorPanel.TabStop = false;
            // 
            // EntityNameTextBox
            // 
            EntityNameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            EntityNameTextBox.Location = new System.Drawing.Point(38, 3);
            EntityNameTextBox.Name = "EntityNameTextBox";
            EntityNameTextBox.Size = new System.Drawing.Size(257, 20);
            EntityNameTextBox.TabIndex = 1;
            // 
            // RemoveButton
            // 
            RemoveButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            RemoveButton.Cursor = System.Windows.Forms.Cursors.Hand;
            RemoveButton.Location = new System.Drawing.Point(296, 2);
            RemoveButton.Name = "RemoveButton";
            RemoveButton.Size = new System.Drawing.Size(26, 22);
            RemoveButton.TabIndex = 2;
            RemoveButton.Text = "X";
            // 
            // EntityColourRow
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(ColorPanel);
            Controls.Add(EntityNameTextBox);
            Controls.Add(RemoveButton);
            Dock = DockStyle.Top;
            Height = 28;
            Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            Name = "EntityColourRow";
            Size = new System.Drawing.Size(350, 28);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    
    }
}
