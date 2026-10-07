using System;
using System.Drawing;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Translations;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    [AutoTranslate]
    [Export]
    public partial class VertexScaleControl : UserControl
    {
        #region Translations
        
        public string ScaleDistance { set => this.InvokeLater(() => ScaleDistanceLabel.Text = value); }
        public string Reset { set => this.InvokeLater(() => ResetDistanceButton.Text = value); }
        public string ResetOrigin { set => this.InvokeLater(() => ResetOriginButton.Text = value); }

        #endregion

        private bool _freeze;

        public VertexScaleControl()
        {
            _freeze = true;
            InitializeComponent();
            _freeze = false;

            CreateHandle();
            HookTextChanges();
            LayoutControls();
        }

        private bool _layouting;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutControls();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Width, DoLayout(false));
        }

        private void LayoutControls()
        {
            if (_layouting) return;
            _layouting = true;
            try
            {
                SuspendLayout();
                var h = DoLayout(true);
                if (Height != h) Height = h;
                ResumeLayout(false);
            }
            finally
            {
                _layouting = false;
            }
        }

        private void HookTextChanges()
        {
            foreach (Control c in Controls) c.TextChanged += (s, e) => LayoutControls();
        }

        private int DoLayout(bool apply)
        {
            var m = LogicalToDeviceUnits(3);
            var buttonH = Math.Max(LogicalToDeviceUnits(23), Font.Height + LogicalToDeviceUnits(8));
            var inner = Math.Max(1, ClientSize.Width - 2 * m);

            var labelW = VertexControlLayout.TextWidth(ScaleDistanceLabel) + m;
            var resetW = Math.Max(LogicalToDeviceUnits(50), VertexControlLayout.TextWidth(ResetDistanceButton) + LogicalToDeviceUnits(16));
            var numberW = Math.Min(LogicalToDeviceUnits(120), Math.Max(LogicalToDeviceUnits(40), inner - labelW - resetW - 2 * m));
            var y = m;

            VertexControlLayout.Move(apply, ScaleDistanceLabel, m, y + (buttonH - ScaleDistanceLabel.Height) / 2);
            VertexControlLayout.Bounds(apply, DistanceValue, m + labelW, y + (buttonH - DistanceValue.Height) / 2, numberW, DistanceValue.Height);
            VertexControlLayout.Bounds(apply, ResetDistanceButton, m + inner - resetW, y, resetW, buttonH);
            y += buttonH + m;

            var originW = Math.Min(inner, Math.Max(inner / 2, VertexControlLayout.TextWidth(ResetOriginButton) + LogicalToDeviceUnits(24)));
            VertexControlLayout.Bounds(apply, ResetOriginButton, m + (inner - originW) / 2, y, originW, buttonH);
            y += buttonH + m;

            return y;
        }

        public void ResetValue()
        {
            _freeze = true;
            DistanceValue.Value = 100;
            Oy.Publish("VertexScaleTool:ValueReset", DistanceValue.Value);
            _freeze = false;
        }

        private void DistanceValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            Oy.Publish("VertexScaleTool:ValueChanged", DistanceValue.Value);
        }

        private void ResetDistanceClicked(object sender, EventArgs e)
        {
            ResetValue();
        }

        private void ResetOriginClicked(object sender, EventArgs e)
        {
            Oy.Publish("VertexScaleTool:ResetOrigin");
        }
    }
}
