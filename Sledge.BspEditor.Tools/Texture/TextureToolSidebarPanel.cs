using System;
using System.Drawing;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Texture
{
    [AutoTranslate]
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    public partial class TextureToolSidebarPanel : UserControl, ISidebarComponent
    {
        [Import] private TextureTool _tool;
        
        public string Title { get; set; } = "Texture Power Tools";
        public object Control => this;

        #region Translations

        public string RandomiseShiftValues
        {
            set => this.InvokeLater(() => RandomiseShiftValuesGroup.Text = value);
        }

        public string Min
        {
            set => this.InvokeLater(() => MinLabel.Text = value);
        }

        public string Max
        {
            set => this.InvokeLater(() => MaxLabel.Text = value);
        }

        public string RandomiseX
        {
            set => this.InvokeLater(() => RandomShiftXButton.Text = value);
        }

        public string RandomiseY
        {
            set => this.InvokeLater(() => RandomShiftYButton.Text = value);
        }

        public string FitToMultipleTiles
        {
            set => this.InvokeLater(() => FitGroup.Text = value);
        }

        public string TimesToTile
        {
            set => this.InvokeLater(() => TimesToTileLabel.Text = value);
        }

        public string Fit
        {
            set => this.InvokeLater(() => TileFitButton.Text = value);
        }

        #endregion

        public TextureToolSidebarPanel()
        {
            InitializeComponent();
            CreateHandle();

            foreach (var group in new[] { RandomiseShiftValuesGroup, FitGroup })
            {
                foreach (Control c in group.Controls) c.TextChanged += (s, e) => LayoutControls();
            }

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

        private static int TextWidth(Control c)
        {
            return TextRenderer.MeasureText(c.Text ?? "", c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        }

        /// <summary>
        /// Both group boxes fill the panel width and their contents are laid out proportionally,
        /// so nothing is clipped (or stuck to one side) when the sidebar is resized.
        /// </summary>
        private void LayoutControls()
        {
            if (_layouting || RandomiseShiftValuesGroup == null) return;
            _layouting = true;
            try
            {
                SuspendLayout();

                var p = LogicalToDeviceUnits(5);
                var m = LogicalToDeviceUnits(3);
                var rowH = Math.Max(LogicalToDeviceUnits(23), Font.Height + LogicalToDeviceUnits(8));
                var minNumber = LogicalToDeviceUnits(40);
                var maxNumber = LogicalToDeviceUnits(120);
                var groupW = Math.Max(1, ClientSize.Width - 2 * p);
                var y = p;

                // --- Randomise shift values
                var g1 = RandomiseShiftValuesGroup;
                var d1 = g1.DisplayRectangle;
                var w1 = Math.Max(1, groupW - (g1.Width - d1.Width));
                var labelW = Math.Max(TextWidth(MinLabel), TextWidth(MaxLabel)) + m;
                var buttonW = Math.Max(Math.Max(TextWidth(RandomShiftXButton), TextWidth(RandomShiftYButton)) + LogicalToDeviceUnits(24), (w1 - labelW) / 2);
                var numberW = Math.Min(maxNumber, Math.Max(minNumber, w1 - labelW - buttonW - m));
                var innerX = d1.Left + m;
                var rowTop = d1.Top;

                MinLabel.Location = new Point(innerX, rowTop + (rowH - MinLabel.Height) / 2);
                RandomShiftMin.SetBounds(innerX + labelW, rowTop + (rowH - RandomShiftMin.Height) / 2, numberW, RandomShiftMin.Height);
                RandomShiftXButton.SetBounds(d1.Left + w1 - m - buttonW, rowTop, buttonW, rowH);
                rowTop += rowH + m;

                MaxLabel.Location = new Point(innerX, rowTop + (rowH - MaxLabel.Height) / 2);
                RandomShiftMax.SetBounds(innerX + labelW, rowTop + (rowH - RandomShiftMax.Height) / 2, numberW, RandomShiftMax.Height);
                RandomShiftYButton.SetBounds(d1.Left + w1 - m - buttonW, rowTop, buttonW, rowH);
                rowTop += rowH + m;

                var g1H = rowTop + (g1.Height - d1.Bottom);
                g1.SetBounds(p, y, groupW, g1H);
                y += g1H + m;

                // --- Fit to multiple tiles
                var g2 = FitGroup;
                var d2 = g2.DisplayRectangle;
                var w2 = Math.Max(1, groupW - (g2.Width - d2.Width));
                var innerW2 = Math.Max(1, w2 - 2 * m);
                var tileLabelH = TextRenderer.MeasureText(TimesToTileLabel.Text ?? "", TimesToTileLabel.Font, new Size(innerW2, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + m;

                var labelW2 = Math.Max(TextWidth(label1), TextWidth(label4)) + m;
                var fitW = Math.Max(TextWidth(TileFitButton) + LogicalToDeviceUnits(24), (w2 - labelW2) / 2);
                var numberW2 = Math.Min(maxNumber, Math.Max(minNumber, w2 - labelW2 - fitW - 3 * m));
                var top2 = d2.Top;

                TimesToTileLabel.SetBounds(innerX, top2, innerW2, tileLabelH);
                top2 += tileLabelH;

                label1.Location = new Point(innerX, top2 + (rowH - label1.Height) / 2);
                TileFitX.SetBounds(innerX + labelW2, top2 + (rowH - TileFitX.Height) / 2, numberW2, TileFitX.Height);
                var fitTop = top2;
                top2 += rowH + m;

                label4.Location = new Point(innerX, top2 + (rowH - label4.Height) / 2);
                TileFitY.SetBounds(innerX + labelW2, top2 + (rowH - TileFitY.Height) / 2, numberW2, TileFitY.Height);
                top2 += rowH + m;

                // The Fit button spans both number rows
                TileFitButton.SetBounds(d2.Left + w2 - m - fitW, fitTop, fitW, 2 * rowH + m);

                var g2H = top2 + (g2.Height - d2.Bottom);
                g2.SetBounds(p, y, groupW, g2H);
                y += g2H + p;

                if (Height != y) Height = y;

                ResumeLayout(false);
            }
            finally
            {
                _layouting = false;
            }
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out TextureTool _);
        }

        private void RandomShiftXButtonClicked(object sender, EventArgs e)
        {
            var document = _tool.GetDocument();
            var fs = document?.Map.Data.GetOne<FaceSelection>();
            if (fs == null || fs.IsEmpty) return;

            var min = (int) RandomShiftMin.Value;
            var max = (int) RandomShiftMax.Value;
            
            var rand = new Random();

            var edit = new Transaction();
            foreach (var it in fs.GetSelectedFaces())
            {
                var clone = (Face) it.Value.Clone();
                clone.Texture.XShift = rand.Next(min, max + 1); // Upper bound is exclusive

                edit.Add(new RemoveMapObjectData(it.Key.ID, it.Value));
                edit.Add(new AddMapObjectData(it.Key.ID, clone));
            }

            MapDocumentOperation.Perform(document, edit);
        }

        private void RandomShiftYButtonClicked(object sender, EventArgs e)
        {
            var document = _tool.GetDocument();
            var fs = document?.Map.Data.GetOne<FaceSelection>();
            if (fs == null || fs.IsEmpty) return;

            var min = (int) RandomShiftMin.Value;
            var max = (int) RandomShiftMax.Value;
            
            var rand = new Random();

            var edit = new Transaction();
            foreach (var it in fs.GetSelectedFaces())
            {
                var clone = (Face) it.Value.Clone();
                clone.Texture.YShift = rand.Next(min, max + 1); // Upper bound is exclusive

                edit.Add(new RemoveMapObjectData(it.Key.ID, it.Value));
                edit.Add(new AddMapObjectData(it.Key.ID, clone));
            }

            MapDocumentOperation.Perform(document, edit);
        }

        private void TileFitButtonClicked(object sender, EventArgs e)
        {
            ApplyFit();
        }

        private async Task ApplyFit()
        {
            var document = _tool.GetDocument();
            var fs = document?.Map.Data.GetOne<FaceSelection>();
            if (fs == null || fs.IsEmpty) return;
            
            var tc = await document.Environment.GetTextureCollection();
            if (tc == null) return;
            
            var tileX = (int) TileFitX.Value;
            var tileY = (int) TileFitY.Value;

            var edit = new Transaction();
            foreach (var it in fs.GetSelectedFaces())
            {
                var clone = (Face) it.Value.Clone();
                
                var tex = await tc.GetTextureItem(clone.Texture.Name);
                if (tex == null) continue;

                clone.Texture.FitToPointCloud(tex.Width, tex.Height, new Cloud(clone.Vertices), tileX, tileY);
                
                edit.Add(new RemoveMapObjectData(it.Key.ID, it.Value));
                edit.Add(new AddMapObjectData(it.Key.ID, clone));
            }

            if (!edit.IsEmpty) await MapDocumentOperation.Perform(document, edit);
        }
    }
}
