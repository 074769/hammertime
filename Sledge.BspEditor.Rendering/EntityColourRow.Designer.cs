using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Rendering
{
    partial class EntityColourRow
    {
        private IContainer components = null;

        public TextBox EntityNameTextBox;
        public Button RemoveButton;
        public Panel ColorPanel;

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
            EntityNameTextBox = new TextBox();
            RemoveButton = new Button();
            ColorPanel = new Panel();
            SuspendLayout();
            // 
            // RemoveButton
            // 
            RemoveButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            RemoveButton.Cursor = System.Windows.Forms.Cursors.Hand;
            RemoveButton.DialogResult = System.Windows.Forms.DialogResult.None;
            RemoveButton.Location = new System.Drawing.Point(3, 2);
            RemoveButton.Name = "RemoveButton";
            RemoveButton.Size = new System.Drawing.Size(26, 22);
            RemoveButton.TabIndex = 2;
            RemoveButton.Text = "X";
            RemoveButton.UseVisualStyleBackColor = true;
            // 
            // EntityNameTextBox
            // 
            EntityNameTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right)));
            EntityNameTextBox.Location = new System.Drawing.Point(38, 3);
            EntityNameTextBox.Name = "EntityNameTextBox";
            EntityNameTextBox.Size = new System.Drawing.Size(257, 20);
            EntityNameTextBox.TabIndex = 1;
            // 
            // ColorPanel
            // 
            ColorPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            ColorPanel.BackColor = System.Drawing.Color.Gray;
            ColorPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            ColorPanel.Cursor = System.Windows.Forms.Cursors.Hand;
            ColorPanel.Location = new System.Drawing.Point(296, 3);
            ColorPanel.Name = "ColorPanel";
            ColorPanel.Size = new System.Drawing.Size(30, 22);
            ColorPanel.TabIndex = 0;
            ColorPanel.TabStop = false;
            // 
            // EntityColourRow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.ColorPanel);
            this.Controls.Add(this.EntityNameTextBox);
            this.Controls.Add(this.RemoveButton);
            this.Dock = System.Windows.Forms.DockStyle.Top;
            this.Height = 28;
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "EntityColourRow";
            this.Size = new System.Drawing.Size(350, 28);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion
    }
}