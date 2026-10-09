using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Rendering
{
    partial class EntityBrushColorEditor
    {
        private IContainer components = null;

        public Label _titleLabel;
        public FlowLayoutPanel _btnPanel;
        public Button _btnAdd;
        public Button _btnImport;
        public Button _btnExport;
        public Panel _rowPanel;

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
            // _titleLabel
            // 
            _titleLabel.Dock = DockStyle.Top;
            _titleLabel.Location = new System.Drawing.Point(0, 0);
            _titleLabel.Name = "_titleLabel";
            _titleLabel.Size = new System.Drawing.Size(350, 22);
            _titleLabel.TabIndex = 0;
            _titleLabel.Text = "";
            _titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // _btnPanel
            // 
            _btnPanel.Controls.Add(_btnAdd);
            _btnPanel.Controls.Add(_btnImport);
            _btnPanel.Controls.Add(_btnExport);
            _btnPanel.Dock = DockStyle.Top;
            _btnPanel.FlowDirection = FlowDirection.LeftToRight;
            _btnPanel.Location = new System.Drawing.Point(0, 22);
            _btnPanel.Name = "_btnPanel";
            _btnPanel.Size = new System.Drawing.Size(350, 34);
            _btnPanel.TabIndex = 1;
            // 
            // _btnAdd
            // 
            _btnAdd.Cursor = System.Windows.Forms.Cursors.Hand;
            _btnAdd.Name = "_btnAdd";
            _btnAdd.Size = new System.Drawing.Size(75, 23);
            _btnAdd.TabIndex = 0;
            _btnAdd.Text = "Add";
            // 
            // _btnImport
            // 
            _btnImport.Cursor = System.Windows.Forms.Cursors.Hand;
            _btnImport.Name = "_btnImport";
            _btnImport.Size = new System.Drawing.Size(75, 23);
            _btnImport.TabIndex = 1;
            _btnImport.Text = "Import";
            // 
            // _btnExport
            // 
            _btnExport.Cursor = System.Windows.Forms.Cursors.Hand;
            _btnExport.Name = "_btnExport";
            _btnExport.Size = new System.Drawing.Size(75, 23);
            _btnExport.TabIndex = 2;
            _btnExport.Text = "Export";
            // 
            // _rowPanel
            // 
            _rowPanel.AutoScroll = true;
            _rowPanel.BorderStyle = BorderStyle.None;
            _rowPanel.Dock = DockStyle.Fill;
            _rowPanel.Location = new System.Drawing.Point(0, 56);
            _rowPanel.Name = "_rowPanel";
            _rowPanel.Size = new System.Drawing.Size(350, 194);
            _rowPanel.TabIndex = 2;
            // 
            // EntityBrushColorEditor
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            Controls.Add(_titleLabel);
            Controls.Add(_btnPanel);
            Controls.Add(_rowPanel);
            Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            Name = "EntityBrushColorEditor";
            Size = new System.Drawing.Size(350, 250);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}
