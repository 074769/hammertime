namespace Sledge.Shell.Settings.Editors
{
    partial class KeyCaptureEditor
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            this._label = new System.Windows.Forms.Label();
            this.KeyBox = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            //
            // _label
            //
            this._label.AutoSize = true;
            this._label.Location = new System.Drawing.Point(6, 6);
            this._label.Name = "_label";
            this._label.Size = new System.Drawing.Size(35, 13);
            this._label.TabIndex = 0;
            this._label.Text = "label1";
            //
            // KeyBox
            //
            this.KeyBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.KeyBox.Location = new System.Drawing.Point(208, 3);
            this.KeyBox.Name = "KeyBox";
            this.KeyBox.ReadOnly = true;
            this.KeyBox.Size = new System.Drawing.Size(130, 20);
            this.KeyBox.TabIndex = 1;
            this.KeyBox.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            //
            // KeyCaptureEditor
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.KeyBox);
            this.Controls.Add(this._label);
            this.Name = "KeyCaptureEditor";
            this.Size = new System.Drawing.Size(350, 26);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _label;
        private System.Windows.Forms.TextBox KeyBox;
    }
}
