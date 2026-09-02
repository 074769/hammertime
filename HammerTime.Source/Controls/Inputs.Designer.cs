namespace HammerTime.Source.Controls
{
    partial class Inputs
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
            cSource = new ColumnHeader();
            cOutput = new ColumnHeader();
            cInput = new ColumnHeader();
            cParameter = new ColumnHeader();
            cDelay = new ColumnHeader();
            cOnce = new ColumnHeader();
            SuspendLayout();
            // 
            // connectionsView
            // 
            connectionsView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            connectionsView.Columns.AddRange(new ColumnHeader[] { cSource, cOutput, cInput, cParameter, cDelay, cOnce });
            connectionsView.Location = new Point(3, 3);
            connectionsView.Name = "connectionsView";
            connectionsView.Size = new Size(673, 312);
            connectionsView.TabIndex = 0;
            connectionsView.UseCompatibleStateImageBehavior = false;
            connectionsView.View = View.Details;
            connectionsView.SelectedIndexChanged += listView1_SelectedIndexChanged;
            // 
            // cSource
            // 
            cSource.Text = "Source";
            // 
            // cOutput
            // 
            cOutput.Text = "Output";
            // 
            // cInput
            // 
            cInput.Text = "My Input";
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
            cOnce.Text = "Once";
            // 
            // Inputs
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(connectionsView);
            Name = "Inputs";
            Size = new Size(679, 378);
            ResumeLayout(false);
        }

        #endregion

        private ListView connectionsView;
        private ColumnHeader cSource;
        private ColumnHeader cOutput;
        private ColumnHeader cInput;
        private ColumnHeader cParameter;
        private ColumnHeader cDelay;
        private ColumnHeader cOnce;
    }
}
