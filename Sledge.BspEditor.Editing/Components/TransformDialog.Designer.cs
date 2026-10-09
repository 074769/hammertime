namespace Sledge.BspEditor.Editing.Components
{
    partial class TransformDialog
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.lblRotate = new System.Windows.Forms.RadioButton();
            this.lblScale = new System.Windows.Forms.RadioButton();
            this.lblMove = new System.Windows.Forms.RadioButton();
            this.lblTeleport = new System.Windows.Forms.RadioButton();
            this.RememberChoiceCheckBox = new System.Windows.Forms.CheckBox();
            this.UsePivotCheckBox = new System.Windows.Forms.CheckBox();
            this.ValueY = new System.Windows.Forms.NumericUpDown();
            this.ValueZ = new System.Windows.Forms.NumericUpDown();
            this.ValueX = new System.Windows.Forms.NumericUpDown();
            this.SourceValueZButton = new System.Windows.Forms.Button();
            this.ZeroValueZButton = new System.Windows.Forms.Button();
            this.SourceValueYButton = new System.Windows.Forms.Button();
            this.ZeroValueYButton = new System.Windows.Forms.Button();
            this.SourceValueXButton = new System.Windows.Forms.Button();
            this.ZeroValueXButton = new System.Windows.Forms.Button();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.CancelButton = new System.Windows.Forms.Button();
            this.OkButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.ValueY)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ValueZ)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.ValueX)).BeginInit();
            this.SuspendLayout();
            this.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            // 
            // lblRotate
            // 
            this.lblRotate.AutoSize = true;
            this.lblRotate.Checked = true;
            this.lblRotate.Location = new System.Drawing.Point(16, 16);
            this.lblRotate.Name = "lblRotate";
            this.lblRotate.Size = new System.Drawing.Size(78, 25);
            this.lblRotate.TabIndex = 0;
            this.lblRotate.TabStop = true;
            this.lblRotate.Text = "Rotate";
            this.lblRotate.UseVisualStyleBackColor = true;
            this.lblRotate.Click += new System.EventHandler(this.TypeChanged);
            // 
            // lblScale
            // 
            this.lblScale.AutoSize = true;
            this.lblScale.Location = new System.Drawing.Point(16, 48);
            this.lblScale.Name = "lblScale";
            this.lblScale.Size = new System.Drawing.Size(70, 25);
            this.lblScale.TabIndex = 1;
            this.lblScale.Text = "Scale";
            this.lblScale.UseVisualStyleBackColor = true;
            this.lblScale.Click += new System.EventHandler(this.TypeChanged);
            // 
            // lblMove
            // 
            this.lblMove.AutoSize = true;
            this.lblMove.Location = new System.Drawing.Point(16, 80);
            this.lblMove.Name = "lblMove";
            this.lblMove.Size = new System.Drawing.Size(70, 25);
            this.lblMove.TabIndex = 2;
            this.lblMove.Text = "Move";
            this.lblMove.UseVisualStyleBackColor = true;
            this.lblMove.Click += new System.EventHandler(this.TypeChanged);
            // 
            // lblTeleport
            // 
            this.lblTeleport.AutoSize = true;
            this.lblTeleport.Location = new System.Drawing.Point(16, 112);
            this.lblTeleport.Name = "lblTeleport";
            this.lblTeleport.Size = new System.Drawing.Size(91, 25);
            this.lblTeleport.TabIndex = 3;
            this.lblTeleport.Text = "Teleport";
            this.lblTeleport.UseVisualStyleBackColor = true;
            this.lblTeleport.Click += new System.EventHandler(this.TypeChanged);
            // 
            // RememberChoiceCheckBox
            // 
            this.RememberChoiceCheckBox.AutoSize = true;
            this.RememberChoiceCheckBox.Location = new System.Drawing.Point(16, 148);
            this.RememberChoiceCheckBox.Name = "RememberChoiceCheckBox";
            this.RememberChoiceCheckBox.Size = new System.Drawing.Size(159, 25);
            this.RememberChoiceCheckBox.TabIndex = 4;
            this.RememberChoiceCheckBox.Text = "Remember Choice";
            this.RememberChoiceCheckBox.UseVisualStyleBackColor = true;
            // 
            // UsePivotCheckBox
            // 
            this.UsePivotCheckBox.AutoSize = true;
            this.UsePivotCheckBox.Location = new System.Drawing.Point(160, 112);
            this.UsePivotCheckBox.Name = "UsePivotCheckBox";
            this.UsePivotCheckBox.Size = new System.Drawing.Size(109, 25);
            this.UsePivotCheckBox.TabIndex = 5;
            this.UsePivotCheckBox.Text = "Use Pivot";
            this.UsePivotCheckBox.UseVisualStyleBackColor = true;
            // 
            // ValueY
            // 
            this.ValueY.DecimalPlaces = 2;
            this.ValueY.Location = new System.Drawing.Point(160, 48);
            this.ValueY.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.ValueY.Minimum = new decimal(new int[] {
            16384,
            0,
            0,
            -2147483648});
            this.ValueY.Name = "ValueY";
            this.ValueY.Size = new System.Drawing.Size(90, 29);
            this.ValueY.TabIndex = 24;
            // 
            // ValueZ
            // 
            this.ValueZ.DecimalPlaces = 2;
            this.ValueZ.Location = new System.Drawing.Point(160, 80);
            this.ValueZ.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.ValueZ.Minimum = new decimal(new int[] {
            16384,
            0,
            0,
            -2147483648});
            this.ValueZ.Name = "ValueZ";
            this.ValueZ.Size = new System.Drawing.Size(90, 29);
            this.ValueZ.TabIndex = 25;
            // 
            // ValueX
            // 
            this.ValueX.DecimalPlaces = 2;
            this.ValueX.Location = new System.Drawing.Point(160, 16);
            this.ValueX.Maximum = new decimal(new int[] {
            16384,
            0,
            0,
            0});
            this.ValueX.Minimum = new decimal(new int[] {
            16384,
            0,
            0,
            -2147483648});
            this.ValueX.Name = "ValueX";
            this.ValueX.Size = new System.Drawing.Size(90, 29);
            this.ValueX.TabIndex = 26;
            // 
            // SourceValueZButton
            // 
            this.SourceValueZButton.Location = new System.Drawing.Point(302, 80);
            this.SourceValueZButton.Name = "SourceValueZButton";
            this.SourceValueZButton.Size = new System.Drawing.Size(90, 29);
            this.SourceValueZButton.TabIndex = 18;
            this.SourceValueZButton.Text = "Source";
            this.SourceValueZButton.UseVisualStyleBackColor = true;
            // 
            // ZeroValueZButton
            // 
            this.ZeroValueZButton.Location = new System.Drawing.Point(256, 80);
            this.ZeroValueZButton.Name = "ZeroValueZButton";
            this.ZeroValueZButton.Size = new System.Drawing.Size(40, 29);
            this.ZeroValueZButton.TabIndex = 19;
            this.ZeroValueZButton.Text = "0";
            this.ZeroValueZButton.UseVisualStyleBackColor = true;
            // 
            // SourceValueYButton
            // 
            this.SourceValueYButton.Location = new System.Drawing.Point(302, 48);
            this.SourceValueYButton.Name = "SourceValueYButton";
            this.SourceValueYButton.Size = new System.Drawing.Size(90, 29);
            this.SourceValueYButton.TabIndex = 20;
            this.SourceValueYButton.Text = "Source";
            this.SourceValueYButton.UseVisualStyleBackColor = true;
            // 
            // ZeroValueYButton
            // 
            this.ZeroValueYButton.Location = new System.Drawing.Point(256, 48);
            this.ZeroValueYButton.Name = "ZeroValueYButton";
            this.ZeroValueYButton.Size = new System.Drawing.Size(40, 29);
            this.ZeroValueYButton.TabIndex = 21;
            this.ZeroValueYButton.Text = "0";
            this.ZeroValueYButton.UseVisualStyleBackColor = true;
            // 
            // SourceValueXButton
            // 
            this.SourceValueXButton.Location = new System.Drawing.Point(302, 16);
            this.SourceValueXButton.Name = "SourceValueXButton";
            this.SourceValueXButton.Size = new System.Drawing.Size(90, 29);
            this.SourceValueXButton.TabIndex = 22;
            this.SourceValueXButton.Text = "Source";
            this.SourceValueXButton.UseVisualStyleBackColor = true;
            // 
            // ZeroValueXButton
            // 
            this.ZeroValueXButton.Location = new System.Drawing.Point(256, 16);
            this.ZeroValueXButton.Name = "ZeroValueXButton";
            this.ZeroValueXButton.Size = new System.Drawing.Size(40, 29);
            this.ZeroValueXButton.TabIndex = 23;
            this.ZeroValueXButton.Text = "0";
            this.ZeroValueXButton.UseVisualStyleBackColor = true;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(134, 84);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(22, 21);
            this.label5.TabIndex = 17;
            this.label5.Text = "Z:";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(134, 52);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(22, 21);
            this.label6.TabIndex = 16;
            this.label6.Text = "Y:";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.Location = new System.Drawing.Point(134, 20);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(22, 21);
            this.label7.TabIndex = 15;
            this.label7.Text = "X:";
            // 
            // CancelButton
            // 
            this.CancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelButton.Location = new System.Drawing.Point(211, 188);
            this.CancelButton.Name = "CancelButton";
            this.CancelButton.Size = new System.Drawing.Size(110, 38);
            this.CancelButton.TabIndex = 27;
            this.CancelButton.Text = "Cancel";
            this.CancelButton.UseVisualStyleBackColor = true;
            // 
            // OkButton
            // 
            this.OkButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OkButton.Location = new System.Drawing.Point(89, 188);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(110, 38);
            this.OkButton.TabIndex = 28;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            // 
            // TransformDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.ClientSize = new System.Drawing.Size(410, 250);
            this.Controls.Add(this.UsePivotCheckBox);
            this.Controls.Add(this.RememberChoiceCheckBox);
            this.Controls.Add(this.lblTeleport);
            this.Controls.Add(this.lblMove);
            this.Controls.Add(this.CancelButton);
            this.Controls.Add(this.OkButton);
            this.Controls.Add(this.ValueY);
            this.Controls.Add(this.ValueZ);
            this.Controls.Add(this.ValueX);
            this.Controls.Add(this.SourceValueZButton);
            this.Controls.Add(this.ZeroValueZButton);
            this.Controls.Add(this.SourceValueYButton);
            this.Controls.Add(this.ZeroValueYButton);
            this.Controls.Add(this.SourceValueXButton);
            this.Controls.Add(this.ZeroValueXButton);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.lblScale);
            this.Controls.Add(this.lblRotate);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "TransformDialog";
            this.ShowInTaskbar = false;
            this.Text = "Transform";
            ((System.ComponentModel.ISupportInitialize)(this.ValueY)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ValueZ)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.ValueX)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.RadioButton lblRotate;
        private System.Windows.Forms.RadioButton lblScale;
        private System.Windows.Forms.RadioButton lblMove;
        private System.Windows.Forms.RadioButton lblTeleport;
        private System.Windows.Forms.CheckBox RememberChoiceCheckBox;
        private System.Windows.Forms.CheckBox UsePivotCheckBox;
        private System.Windows.Forms.NumericUpDown ValueY;
        private System.Windows.Forms.NumericUpDown ValueZ;
        private System.Windows.Forms.NumericUpDown ValueX;
        private System.Windows.Forms.Button SourceValueZButton;
        private System.Windows.Forms.Button ZeroValueZButton;
        private System.Windows.Forms.Button SourceValueYButton;
        private System.Windows.Forms.Button ZeroValueYButton;
        private System.Windows.Forms.Button SourceValueXButton;
        private System.Windows.Forms.Button ZeroValueXButton;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Button CancelButton;
        private System.Windows.Forms.Button OkButton;
    }
}