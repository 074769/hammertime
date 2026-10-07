using System;
using System.Drawing;
using System.Linq;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Translations;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
	[AutoTranslate]
	[Export]
	public partial class VertexEditFaceControl : UserControl
	{
		#region Translations

		public string WithSelectedFaces { set => this.InvokeLater(() => WithSelectedFacesLabel.Text = value); }
		public string Units { set => this.InvokeLater(() => { UnitsLabel1.Text = value; UnitsLabel2.Text = value; }); }
		public string PokeBy { set => this.InvokeLater(() => PokeByLabel.Text = value); }
		public string BevelBy { set => this.InvokeLater(() => BevelByLabel.Text = value); }
		public string Poke { set => this.InvokeLater(() => PokeFaceButton.Text = value); }
		public string Bevel { set => this.InvokeLater(() => BevelButton.Text = value); }

		#endregion

		public VertexEditFaceControl()
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
		    var rowH = Math.Max(LogicalToDeviceUnits(24), Font.Height + LogicalToDeviceUnits(10));
		    var inner = Math.Max(1, ClientSize.Width - 2 * m);

		    var rows = new (Label label, Control input, Label units, Button button)[]
		    {
		        (PokeByLabel, PokeFaceCount, UnitsLabel1, PokeFaceButton),
		        (BevelByLabel, BevelValue, UnitsLabel2, BevelButton),
		        (label2, ExtrudeValue, label1, ExtrudeButton)
		    };

		    var labelW = rows.Max(r => VertexControlLayout.TextWidth(r.label)) + m;
		    var unitsW = rows.Max(r => VertexControlLayout.TextWidth(r.units)) + m;
		    var buttonW = Math.Max(LogicalToDeviceUnits(50), rows.Max(r => VertexControlLayout.TextWidth(r.button)) + LogicalToDeviceUnits(16));
		    var inputW = Math.Min(LogicalToDeviceUnits(100), Math.Max(LogicalToDeviceUnits(40), inner - labelW - unitsW - buttonW - m));

		    var y = m;
		    VertexControlLayout.Move(apply, WithSelectedFacesLabel, m, y);
		    y += Math.Max(WithSelectedFacesLabel.Height, Font.Height) + m;

		    foreach (var r in rows)
		    {
		        var x = m;
		        VertexControlLayout.Move(apply, r.label, x, y + (rowH - r.label.Height) / 2);
		        x += labelW;
		        VertexControlLayout.Bounds(apply, r.input, x, y + (rowH - r.input.Height) / 2, inputW, r.input.Height);
		        x += inputW + m;
		        VertexControlLayout.Move(apply, r.units, x, y + (rowH - r.units.Height) / 2);
		        VertexControlLayout.Bounds(apply, r.button, m + inner - buttonW, y, buttonW, rowH);
		        y += rowH + m;
		    }

		    return y;
		}

		private void BevelButtonClicked(object sender, EventArgs e)
		{
			Oy.Publish("VertexEditFaceTool:Bevel", (int) BevelValue.Value);
		}

		private void PokeFaceButtonClicked(object sender, EventArgs e)
		{
			Oy.Publish("VertexEditFaceTool:Poke", (int) PokeFaceCount.Value);
		}

		private void ExtrudeButtonClicked(object sender, EventArgs e)
		{
			Oy.Publish("VertexEditFaceTool:Extrude", (int)ExtrudeValue.Value);

		}
	}
}
