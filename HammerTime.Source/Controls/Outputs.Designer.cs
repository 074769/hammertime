namespace HammerTime.Source.Controls
{
    partial class Outputs
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
            connectionsView = new ListView();
            cOutputs = new ColumnHeader();
            cEntity = new ColumnHeader();
            cTargetInput = new ColumnHeader();
            cParameter = new ColumnHeader();
            cDelay = new ColumnHeader();
            cOnce = new ColumnHeader();
            label1 = new Label();
            label2 = new Label();
            label3 = new Label();
            label4 = new Label();
            label5 = new Label();
            outputBox = new ComboBox();
            entityBox = new ComboBox();
            inputBox = new ComboBox();
            parameterBox = new ComboBox();
            delayBox = new TextBox();
            chkFireOnce = new CheckBox();
            SuspendLayout();
            // 
            // connectionsView
            // 
            connectionsView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            connectionsView.Columns.AddRange(new ColumnHeader[] { cOutputs, cEntity, cTargetInput, cParameter, cDelay, cOnce });
            connectionsView.Location = new Point(3, 3);
            connectionsView.Name = "connectionsView";
            connectionsView.Size = new Size(589, 270);
            connectionsView.TabIndex = 1;
            connectionsView.UseCompatibleStateImageBehavior = false;
            connectionsView.View = View.Details;
            // 
            // cOutputs
            // 
            cOutputs.Text = "My Output";
            // 
            // cEntity
            // 
            cEntity.Text = "Target Entity";
            // 
            // cTargetInput
            // 
            cTargetInput.Text = "Target Input";
            // 
            // cParameter
            // 
            cParameter.Text = "Parameter";
            // 
            // cDelay
            // 
            cDelay.Text = "Delay";
            // 
            // cOnce
            // 
            cOnce.Text = "Only Once";
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label1.AutoSize = true;
            label1.Location = new Point(3, 279);
            label1.Name = "label1";
            label1.Size = new Size(105, 15);
            label1.TabIndex = 2;
            label1.Text = "My Output named";
            // 
            // label2
            // 
            label2.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label2.AutoSize = true;
            label2.Location = new Point(3, 307);
            label2.Name = "label2";
            label2.Size = new Size(129, 15);
            label2.TabIndex = 3;
            label2.Text = "Targets entities named ";
            // 
            // label3
            // 
            label3.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label3.AutoSize = true;
            label3.Location = new Point(3, 332);
            label3.Name = "label3";
            label3.Size = new Size(76, 15);
            label3.TabIndex = 4;
            label3.Text = "Via this input";
            // 
            // label4
            // 
            label4.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label4.AutoSize = true;
            label4.Location = new Point(3, 354);
            label4.Name = "label4";
            label4.Size = new Size(158, 15);
            label4.TabIndex = 5;
            label4.Text = "With a parameter override of";
            // 
            // label5
            // 
            label5.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            label5.AutoSize = true;
            label5.Location = new Point(3, 379);
            label5.Name = "label5";
            label5.Size = new Size(146, 15);
            label5.TabIndex = 6;
            label5.Text = "After a delay in seconds of";
            // 
            // outputBox
            // 
            outputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            outputBox.FormattingEnabled = true;
            outputBox.Location = new Point(172, 279);
            outputBox.Name = "outputBox";
            outputBox.Size = new Size(121, 23);
            outputBox.TabIndex = 7;
            // 
            // entityBox
            // 
            entityBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            entityBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            entityBox.AutoCompleteSource = AutoCompleteSource.ListItems;
            entityBox.FormattingEnabled = true;
            entityBox.Location = new Point(172, 304);
            entityBox.Name = "entityBox";
            entityBox.Size = new Size(121, 23);
            entityBox.TabIndex = 8;
            // 
            // inputBox
            // 
            inputBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            inputBox.FormattingEnabled = true;
            inputBox.Location = new Point(172, 329);
            inputBox.Name = "inputBox";
            inputBox.Size = new Size(121, 23);
            inputBox.TabIndex = 9;
            // 
            // parameterBox
            // 
            parameterBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            parameterBox.FormattingEnabled = true;
            parameterBox.Location = new Point(172, 354);
            parameterBox.Name = "parameterBox";
            parameterBox.Size = new Size(121, 23);
            parameterBox.TabIndex = 10;
            // 
            // delayBox
            // 
            delayBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            delayBox.Location = new Point(172, 379);
            delayBox.Name = "delayBox";
            delayBox.Size = new Size(100, 23);
            delayBox.TabIndex = 11;
            // 
            // chkFireOnce
            // 
            chkFireOnce.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            chkFireOnce.AutoSize = true;
            chkFireOnce.Location = new Point(299, 381);
            chkFireOnce.Name = "chkFireOnce";
            chkFireOnce.Size = new Size(74, 19);
            chkFireOnce.TabIndex = 12;
            chkFireOnce.Text = "Fire once";
            chkFireOnce.UseVisualStyleBackColor = true;
            // 
            // Outputs
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(chkFireOnce);
            Controls.Add(delayBox);
            Controls.Add(parameterBox);
            Controls.Add(inputBox);
            Controls.Add(entityBox);
            Controls.Add(outputBox);
            Controls.Add(label5);
            Controls.Add(label4);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(label1);
            Controls.Add(connectionsView);
            Name = "Outputs";
            Size = new Size(595, 410);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ListView connectionsView;
        private ColumnHeader cOutputs;
        private ColumnHeader cEntity;
        private ColumnHeader cTargetInput;
        private ColumnHeader cParameter;
        private ColumnHeader cDelay;
        private ColumnHeader cOnce;
        private Label label1;
        private Label label2;
        private Label label3;
        private Label label4;
        private Label label5;
        private ComboBox outputBox;
        private ComboBox entityBox;
        private ComboBox inputBox;
        private ComboBox parameterBox;
        private TextBox delayBox;
        private CheckBox chkFireOnce;
    }
}
