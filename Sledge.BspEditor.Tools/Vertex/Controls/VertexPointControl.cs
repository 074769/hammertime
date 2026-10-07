using System;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Tools.Vertex.Tools;
using Sledge.Common.Easings;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Translations;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    [Export]
    [AutoTranslate]
    public partial class VertexPointControl : UserControl
    {
        public bool AutomaticallyMerge
        {
            get => AutoMerge.Checked;
            set => AutoMerge.Checked = value;
        }

        public bool SplitEnabled
        {
            get => SplitButton.Enabled;
            set => SplitButton.Enabled = value;
        }

        #region Translations

        public string MergeOverlappingVertices { set => this.InvokeLater(() => MergeButton.Text = value); }
        public string MergeAutomatically { set => this.InvokeLater(() => AutoMerge.Text = value); }
        public string SplitFace { set => this.InvokeLater(() => SplitButton.Text = value); }
        public string ShowPoints { set => this.InvokeLater(() => ShowPointsCheckbox.Text = value); }
        public string ShowMidpoints { set => this.InvokeLater(() => ShowMidpointsCheckbox.Text = value); }
        public string MergeResults { get; set; }

        #endregion

        public VertexPointControl()
        {
            InitializeComponent();
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
            var indent = m + LogicalToDeviceUnits(4);
            var y = m;

            VertexControlLayout.Bounds(apply, MergeButton, m, y, inner, buttonH);
            y += buttonH + m;

            VertexControlLayout.Move(apply, AutoMerge, indent, y);
            y += AutoMerge.Height + m;

            VertexControlLayout.Move(apply, MergeResultsLabel, indent, y);
            y += Math.Max(MergeResultsLabel.Height, Font.Height) + m;

            var splitW = Math.Min(inner, Math.Max(inner / 2, VertexControlLayout.TextWidth(SplitButton) + LogicalToDeviceUnits(24)));
            VertexControlLayout.Bounds(apply, SplitButton, m + (inner - splitW) / 2, y, splitW, buttonH);
            y += buttonH + m;

            // One checkbox per row, so neither can be pushed off the edge of a narrow panel
            VertexControlLayout.Move(apply, ShowPointsCheckbox, indent, y);
            y += ShowPointsCheckbox.Height + m;

            VertexControlLayout.Move(apply, ShowMidpointsCheckbox, indent, y);
            y += ShowMidpointsCheckbox.Height + m;

            return y;
        }

        private void SplitButtonClicked(object sender, EventArgs e)
        {
			Oy.Publish("Command:Run", new CommandMessage("BspEditor:VertexSplitEdge"));
		}

		private void MergeButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("VertexPointTool:Merge", string.Empty);
        }

        public void ShowMergeResult(int mergedVertices, int removedFaces)
        {
            if (mergedVertices + removedFaces <= 0) return;
            MergeResultsLabel.Text = String.Format(MergeResults, mergedVertices, removedFaces);
            MergeResultsLabel.Trigger();
        }

        private void ShowPointsChanged(object sender, EventArgs e)
        {
            VertexPointTool.VisiblePoints v;
            var sp = ShowPointsCheckbox.Checked;
            var smp = ShowMidpointsCheckbox.Checked;

            if (sp && smp)
            {
                v = VertexPointTool.VisiblePoints.All;
            }
            else if (sp)
            {
                v = VertexPointTool.VisiblePoints.Vertices;
            }
            else if (smp)
            {
                v = VertexPointTool.VisiblePoints.Midpoints;
            }
            else if (sender == ShowMidpointsCheckbox)
            {
                v = VertexPointTool.VisiblePoints.Vertices;
                ShowPointsCheckbox.Checked = true;
            }
            else
            {
                v = VertexPointTool.VisiblePoints.Midpoints;
                ShowMidpointsCheckbox.Checked = true;
            }

            Oy.Publish("VertexPointTool:SetVisiblePoints", v.ToString());
        }

        public void SetVisiblePoints(VertexPointTool.VisiblePoints visiblePoints)
        {
            ShowPointsCheckbox.Checked = visiblePoints != VertexPointTool.VisiblePoints.Midpoints;
            ShowMidpointsCheckbox.Checked = visiblePoints != VertexPointTool.VisiblePoints.Vertices;
        }
    }

    public class FadeLabel : Label
    {
        private long _lastTick;
        private long _remaining;

        private readonly Timer _timer;
        private readonly Easing _easing;

        public int FadeTime { get; set; } = 1000;

        public FadeLabel()
        {
            _easing = Easing.FromType(EasingType.Sinusoidal, EasingDirection.Out);
            _timer = new Timer
            {
                Enabled = false,
                Interval = 50
            };
            _timer.Tick += (s, e) => Tick();
            _remaining = 0;
        }

        private void Tick()
        {
            var tick = DateTime.Now.Ticks / 10000;
            _remaining -= (tick - _lastTick);
            _lastTick = tick;
            if (_remaining <= 0) _timer.Stop();
            Invalidate();
            Refresh();
        }

        public void Trigger()
        {
            _remaining = FadeTime;
            _lastTick = DateTime.Now.Ticks / 10000;
            _timer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var val = _easing.Evaluate((_remaining * 1d) / FadeTime);
            val = Math.Min(1, Math.Max(0, val));
            var a = (int) (val * 255);
            var c = Color.FromArgb(a, ForeColor);
            
            using (var brush = new SolidBrush(c))
            {
                e.Graphics.DrawString(Text, Font, brush, ClientRectangle);
            }
        }
    }
}
