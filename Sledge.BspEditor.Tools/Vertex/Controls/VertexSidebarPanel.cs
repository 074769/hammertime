using System;
using System.Drawing;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Tools.Vertex.Tools;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Translations;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    [Export(typeof(ISidebarComponent))]
    [OrderHint("F")]
    [AutoTranslate]
    public partial class VertexSidebarPanel : UserControl, ISidebarComponent, IManualTranslate
    {
        [Import] private VertexTool _tool;
        [ImportMany] private IEnumerable<Lazy<VertexSubtool>> _subTools;

        public string Title { get; set; } = "Vertex Tool";
        public object Control => this;

        private bool _layouting;

        public VertexSidebarPanel()
        {
            InitializeComponent();

            panel1.Resize += (s, e) => LayoutBottomButtons();
            LayoutBottomButtons();

            Oy.Subscribe<VertexTool>("Tool:Activated", t =>
            {
                SetSelectedTool(t.CurrentSubTool?.GetType());
            });
            Oy.Subscribe<Type>("VertexTool:SubToolChanged", t =>
            {
                SetSelectedTool(t);
            });
        }

        /// <summary>
        /// Deselect all / Reset to original split the panel width in half instead of using fixed positions.
        /// </summary>
        private void LayoutBottomButtons()
        {
            if (_layouting) return;
            _layouting = true;
            try
            {
                var m = LogicalToDeviceUnits(3);
                var buttonH = Math.Max(LogicalToDeviceUnits(23), Font.Height + LogicalToDeviceUnits(8));
                var inner = Math.Max(1, panel1.ClientSize.Width - 2 * m);
                var colW = (inner - m) / 2;

                DeselectAllButton.SetBounds(m, m, colW, buttonH);
                ResetButton.SetBounds(m + colW + m, m, inner - colW - m, buttonH);

                var h = buttonH + 2 * m;
                if (panel1.Height != h) panel1.Height = h;
            }
            finally
            {
                _layouting = false;
            }
        }

        public void Translate(ITranslationStringProvider strings)
        {
            CreateHandle();
            var prefix = GetType().FullName;
            this.InvokeLater(() =>
            {
                Title = strings.GetString(prefix, "Title");
                DeselectAllButton.Text = strings.GetString(prefix, "DeselectAll");
                ResetButton.Text = strings.GetString(prefix, "ResetToOriginal");
            });
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out VertexTool _);
        }

        public void SelectTool(Type tool)
        {
            Oy.Publish("VertexTool:SetSubTool", tool);
        }

        public void SetSelectedTool(Type tool)
        {
            AddToolButtons();

            foreach (RadioButton rb in ButtonLayoutPanel.Controls)
            {
                if (rb.Tag as Type != tool) rb.Checked = false;
                else if (!rb.Checked) rb.Checked = true;
            }

            ControlPanel.Controls.Clear();

            var t = _tool.CurrentSubTool;
            if (t == null)
            {
                ControlPanel.Text = "";
                return;
            }

            ControlPanel.Text = t.Title;
            if (t.Control != null)
            {
                ControlPanel.Controls.Add(t.Control);
                t.Control.Dock = DockStyle.Top;
                ControlPanel.Height = t.Control.GetPreferredSize(Size.Empty).Height;
            }
        }
        
        public void AddToolButtons()
        {
            if (ButtonLayoutPanel.Controls.Count > 0) return;

            foreach (var subTool in _subTools.Select(x => x.Value).OrderBy(x => x.OrderHint))
            {
                AddSubTool(subTool);
            }
        }

        private void AddSubTool(VertexSubtool tool)
        {
            var rdo = new RadioButton
            {
                Name = tool.Title,
                Text = tool.Title,
                AutoSize = true,
                Margin = new Padding(1),
                Tag = tool.GetType()
            };
            rdo.Click += (sender, e) => SelectTool((sender as RadioButton)?.Tag as Type);
            if (ButtonLayoutPanel.Controls.Count == 0) rdo.Checked = true;
            ButtonLayoutPanel.Controls.Add(rdo);
            ButtonLayoutPanel.Size = ButtonLayoutPanel.GetPreferredSize(rdo.Size);
        }

        private void DeselectAllButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("VertexTool:DeselectAll", String.Empty);
        }

        private void ResetButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("VertexTool:Reset", String.Empty);
        }
    }
}
