namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    partial class VertexEditFaceControl
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
			WithSelectedFacesLabel = new System.Windows.Forms.Label();
			PokeFaceButton = new System.Windows.Forms.Button();
			PokeByLabel = new System.Windows.Forms.Label();
			UnitsLabel1 = new System.Windows.Forms.Label();
			PokeFaceCount = new System.Windows.Forms.NumericUpDown();
			ExtrudeButton = new System.Windows.Forms.Button();
			ExtrudeValue = new System.Windows.Forms.NumericUpDown();
			label1 = new System.Windows.Forms.Label();
			label2 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)PokeFaceCount).BeginInit();
			((System.ComponentModel.ISupportInitialize)ExtrudeValue).BeginInit();
			SuspendLayout();
			// 
			// WithSelectedFacesLabel
			// 
			WithSelectedFacesLabel.AutoSize = true;
			WithSelectedFacesLabel.Location = new System.Drawing.Point(2, 5);
			WithSelectedFacesLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			WithSelectedFacesLabel.Name = "WithSelectedFacesLabel";
			WithSelectedFacesLabel.Size = new System.Drawing.Size(111, 15);
			WithSelectedFacesLabel.TabIndex = 3;
			WithSelectedFacesLabel.Text = "With selected faces:";
			// 
			// PokeFaceButton
			// 
			PokeFaceButton.Location = new System.Drawing.Point(177, 24);
			PokeFaceButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			PokeFaceButton.Name = "PokeFaceButton";
			PokeFaceButton.Size = new System.Drawing.Size(58, 27);
			PokeFaceButton.TabIndex = 9;
			PokeFaceButton.Text = "Poke";
			PokeFaceButton.UseVisualStyleBackColor = true;
			PokeFaceButton.Click += PokeFaceButtonClicked;
			// 
			// PokeByLabel
			// 
			PokeByLabel.AutoSize = true;
			PokeByLabel.Location = new System.Drawing.Point(4, 30);
			PokeByLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			PokeByLabel.Name = "PokeByLabel";
			PokeByLabel.Size = new System.Drawing.Size(49, 15);
			PokeByLabel.TabIndex = 7;
			PokeByLabel.Text = "Poke by";
			// 
			// UnitsLabel1
			// 
			UnitsLabel1.AutoSize = true;
			UnitsLabel1.Location = new System.Drawing.Point(140, 30);
			UnitsLabel1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			UnitsLabel1.Name = "UnitsLabel1";
			UnitsLabel1.Size = new System.Drawing.Size(33, 15);
			UnitsLabel1.TabIndex = 6;
			UnitsLabel1.Text = "units";
			// 
			// PokeFaceCount
			// 
			PokeFaceCount.Location = new System.Drawing.Point(70, 28);
			PokeFaceCount.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			PokeFaceCount.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
			PokeFaceCount.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			PokeFaceCount.Name = "PokeFaceCount";
			PokeFaceCount.Size = new System.Drawing.Size(68, 23);
			PokeFaceCount.TabIndex = 11;
			PokeFaceCount.Value = new decimal(new int[] { 16, 0, 0, 0 });
			// 
			// ExtrudeButton
			// 
			ExtrudeButton.Location = new System.Drawing.Point(177, 83);
			ExtrudeButton.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			ExtrudeButton.Name = "ExtrudeButton";
			ExtrudeButton.Size = new System.Drawing.Size(58, 27);
			ExtrudeButton.TabIndex = 14;
			ExtrudeButton.Text = "Extrude";
			ExtrudeButton.UseVisualStyleBackColor = true;
			ExtrudeButton.Click += ExtrudeButtonClicked;
			// 
			// ExtrudeValue
			// 
			ExtrudeValue.Location = new System.Drawing.Point(70, 87);
			ExtrudeValue.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			ExtrudeValue.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
			ExtrudeValue.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			ExtrudeValue.Name = "ExtrudeValue";
			ExtrudeValue.Size = new System.Drawing.Size(68, 23);
			ExtrudeValue.TabIndex = 15;
			ExtrudeValue.Value = new decimal(new int[] { 16, 0, 0, 0 });
			// 
			// label1
			// 
			label1.AutoSize = true;
			label1.Location = new System.Drawing.Point(140, 89);
			label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(33, 15);
			label1.TabIndex = 12;
			label1.Text = "units";
			// 
			// label2
			// 
			label2.AutoSize = true;
			label2.Location = new System.Drawing.Point(4, 89);
			label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
			label2.Name = "label2";
			label2.Size = new System.Drawing.Size(63, 15);
			label2.TabIndex = 13;
			label2.Text = "Extrude by";
			// 
			// VertexEditFaceControl
			// 
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			Controls.Add(ExtrudeButton);
			Controls.Add(ExtrudeValue);
			Controls.Add(label1);
			Controls.Add(label2);
			Controls.Add(PokeFaceButton);
			Controls.Add(WithSelectedFacesLabel);
			Controls.Add(PokeFaceCount);
			Controls.Add(UnitsLabel1);
			Controls.Add(PokeByLabel);
			Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
			Name = "VertexEditFaceControl";
			Size = new System.Drawing.Size(246, 113);
			((System.ComponentModel.ISupportInitialize)PokeFaceCount).EndInit();
			((System.ComponentModel.ISupportInitialize)ExtrudeValue).EndInit();
			ResumeLayout(false);
			PerformLayout();
		}

		#endregion

		private System.Windows.Forms.Label WithSelectedFacesLabel;
		private System.Windows.Forms.Button PokeFaceButton;
		private System.Windows.Forms.Label PokeByLabel;
		private System.Windows.Forms.Label UnitsLabel1;
		private System.Windows.Forms.NumericUpDown PokeFaceCount;
		private System.Windows.Forms.Button ExtrudeButton;
		private System.Windows.Forms.NumericUpDown ExtrudeValue;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label2;
	}
}
