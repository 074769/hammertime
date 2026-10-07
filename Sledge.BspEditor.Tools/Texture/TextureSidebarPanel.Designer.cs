namespace Sledge.BspEditor.Tools.Texture
{
    partial class TextureSidebarPanel
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.BrowseButton = new System.Windows.Forms.Button();
            this.ReplaceButton = new System.Windows.Forms.Button();
            this.HistoryStrip = new Sledge.BspEditor.Tools.Texture.TextureHistoryStrip();
            this.SizeLabel = new System.Windows.Forms.Label();
            this.NameLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // BrowseButton
            // 
            this.BrowseButton.Location = new System.Drawing.Point(102, 137);
            this.BrowseButton.Name = "BrowseButton";
            this.BrowseButton.Size = new System.Drawing.Size(95, 22);
            this.BrowseButton.TabIndex = 11;
            this.BrowseButton.Text = "Browse...";
            this.BrowseButton.UseVisualStyleBackColor = true;
            this.BrowseButton.Click += new System.EventHandler(this.BrowseButtonClicked);
            // 
            // ReplaceButton
            // 
            this.ReplaceButton.Location = new System.Drawing.Point(102, 161);
            this.ReplaceButton.Name = "ReplaceButton";
            this.ReplaceButton.Size = new System.Drawing.Size(95, 22);
            this.ReplaceButton.TabIndex = 12;
            this.ReplaceButton.Text = "Replace...";
            this.ReplaceButton.UseVisualStyleBackColor = true;
            this.ReplaceButton.Click += new System.EventHandler(this.ReplaceButtonClicked);
            // 
            // HistoryStrip
            // 
            this.HistoryStrip.Location = new System.Drawing.Point(3, 3);
            this.HistoryStrip.Name = "HistoryStrip";
            this.HistoryStrip.Size = new System.Drawing.Size(194, 94);
            this.HistoryStrip.TabIndex = 10;
            this.HistoryStrip.TabStop = false;
            this.HistoryStrip.TextureClicked += new System.EventHandler<string>(this.HistoryTextureClicked);
            // 
            // SizeLabel
            // 
            this.SizeLabel.AutoEllipsis = true;
            this.SizeLabel.Location = new System.Drawing.Point(3, 142);
            this.SizeLabel.Name = "SizeLabel";
            this.SizeLabel.Size = new System.Drawing.Size(27, 13);
            this.SizeLabel.TabIndex = 13;
            this.SizeLabel.Text = "Size";
            // 
            // NameLabel
            // 
            this.NameLabel.AutoEllipsis = true;
            this.NameLabel.Location = new System.Drawing.Point(3, 122);
            this.NameLabel.Name = "NameLabel";
            this.NameLabel.Size = new System.Drawing.Size(35, 13);
            this.NameLabel.TabIndex = 13;
            this.NameLabel.Text = "Name";
            // 
            // TextureSidebarPanel
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.Controls.Add(this.NameLabel);
            this.Controls.Add(this.SizeLabel);
            this.Controls.Add(this.BrowseButton);
            this.Controls.Add(this.ReplaceButton);
            this.Controls.Add(this.HistoryStrip);
            this.DoubleBuffered = true;
            this.MinimumSize = new System.Drawing.Size(120, 0);
            this.Name = "TextureSidebarPanel";
            this.Size = new System.Drawing.Size(200, 186);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button BrowseButton;
        private System.Windows.Forms.Button ReplaceButton;
        private Sledge.BspEditor.Tools.Texture.TextureHistoryStrip HistoryStrip;
        private System.Windows.Forms.Label SizeLabel;
        private System.Windows.Forms.Label NameLabel;
    }
}
