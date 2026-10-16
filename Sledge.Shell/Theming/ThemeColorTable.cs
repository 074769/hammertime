using System.Drawing;
using System.Windows.Forms;
using Sledge.Common.Shell.Theming;

namespace Sledge.Shell.Theming
{
    /// <summary>
    /// Colour table for menus, context menus and tool strips. In light mode it is the stock Windows table;
    /// in dark mode every colour comes from the <see cref="Theme"/> palette (read live, so a palette change
    /// needs no new instance).
    /// </summary>
    public class ThemeColorTable : ProfessionalColorTable
    {
        public override Color ButtonCheckedGradientBegin => Theme.IsDark ? Theme.Checked : base.ButtonCheckedGradientBegin;
        public override Color ButtonCheckedGradientMiddle => Theme.IsDark ? Theme.Checked : base.ButtonCheckedGradientMiddle;
        public override Color ButtonCheckedGradientEnd => Theme.IsDark ? Theme.Checked : base.ButtonCheckedGradientEnd;
        public override Color ButtonCheckedHighlight => Theme.IsDark ? Theme.Checked : base.ButtonCheckedHighlight;
        public override Color ButtonCheckedHighlightBorder => Theme.IsDark ? Theme.BorderStrong : base.ButtonCheckedHighlightBorder;
        public override Color ButtonPressedBorder => Theme.IsDark ? Theme.BorderStrong : base.ButtonPressedBorder;
        public override Color ButtonSelectedBorder => Theme.IsDark ? Theme.BorderStrong : base.ButtonSelectedBorder;
        public override Color ButtonSelectedHighlightBorder => Theme.IsDark ? Theme.BorderStrong : base.ButtonSelectedHighlightBorder;
        public override Color ButtonPressedHighlightBorder => Theme.IsDark ? Theme.BorderStrong : base.ButtonPressedHighlightBorder;
        public override Color MenuItemBorder => Theme.IsDark ? Theme.BorderStrong : base.MenuItemBorder;
        public override Color ButtonPressedGradientBegin => Theme.IsDark ? Theme.Pressed : base.ButtonPressedGradientBegin;
        public override Color ButtonPressedGradientMiddle => Theme.IsDark ? Theme.Pressed : base.ButtonPressedGradientMiddle;
        public override Color ButtonPressedGradientEnd => Theme.IsDark ? Theme.Pressed : base.ButtonPressedGradientEnd;
        public override Color ButtonPressedHighlight => Theme.IsDark ? Theme.Pressed : base.ButtonPressedHighlight;
        public override Color ButtonSelectedGradientBegin => Theme.IsDark ? Theme.Hover : base.ButtonSelectedGradientBegin;
        public override Color ButtonSelectedGradientMiddle => Theme.IsDark ? Theme.Hover : base.ButtonSelectedGradientMiddle;
        public override Color ButtonSelectedGradientEnd => Theme.IsDark ? Theme.Hover : base.ButtonSelectedGradientEnd;
        public override Color ButtonSelectedHighlight => Theme.IsDark ? Theme.Hover : base.ButtonSelectedHighlight;
        public override Color CheckBackground => Theme.IsDark ? Theme.Checked : base.CheckBackground;
        public override Color CheckPressedBackground => Theme.IsDark ? Theme.Pressed : base.CheckPressedBackground;
        public override Color CheckSelectedBackground => Theme.IsDark ? Theme.Hover : base.CheckSelectedBackground;
        public override Color GripDark => Theme.IsDark ? Theme.Border : base.GripDark;
        public override Color GripLight => Theme.IsDark ? Theme.BorderLight : base.GripLight;
        public override Color ImageMarginGradientBegin => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginGradientBegin;
        public override Color ImageMarginGradientMiddle => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginGradientMiddle;
        public override Color ImageMarginGradientEnd => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginGradientEnd;
        public override Color ImageMarginRevealedGradientBegin => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginRevealedGradientBegin;
        public override Color ImageMarginRevealedGradientMiddle => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginRevealedGradientMiddle;
        public override Color ImageMarginRevealedGradientEnd => Theme.IsDark ? Theme.ControlAlt : base.ImageMarginRevealedGradientEnd;
        public override Color MenuBorder => Theme.IsDark ? Theme.Border : base.MenuBorder;
        public override Color MenuItemPressedGradientBegin => Theme.IsDark ? Theme.Checked : base.MenuItemPressedGradientBegin;
        public override Color MenuItemPressedGradientMiddle => Theme.IsDark ? Theme.Checked : base.MenuItemPressedGradientMiddle;
        public override Color MenuItemPressedGradientEnd => Theme.IsDark ? Theme.Checked : base.MenuItemPressedGradientEnd;
        public override Color MenuItemSelected => Theme.IsDark ? Theme.Hover : base.MenuItemSelected;
        public override Color MenuItemSelectedGradientBegin => Theme.IsDark ? Theme.Hover : base.MenuItemSelectedGradientBegin;
        public override Color MenuItemSelectedGradientEnd => Theme.IsDark ? Theme.Hover : base.MenuItemSelectedGradientEnd;
        public override Color MenuStripGradientBegin => Theme.IsDark ? Theme.Control : base.MenuStripGradientBegin;
        public override Color MenuStripGradientEnd => Theme.IsDark ? Theme.Control : base.MenuStripGradientEnd;
        public override Color OverflowButtonGradientBegin => Theme.IsDark ? Theme.ControlAlt : base.OverflowButtonGradientBegin;
        public override Color OverflowButtonGradientMiddle => Theme.IsDark ? Theme.ControlAlt : base.OverflowButtonGradientMiddle;
        public override Color OverflowButtonGradientEnd => Theme.IsDark ? Theme.ControlAlt : base.OverflowButtonGradientEnd;
        public override Color RaftingContainerGradientBegin => Theme.IsDark ? Theme.Control : base.RaftingContainerGradientBegin;
        public override Color RaftingContainerGradientEnd => Theme.IsDark ? Theme.Control : base.RaftingContainerGradientEnd;
        public override Color SeparatorDark => Theme.IsDark ? Theme.Border : base.SeparatorDark;
        public override Color SeparatorLight => Theme.IsDark ? Theme.BorderLight : base.SeparatorLight;
        public override Color StatusStripGradientBegin => Theme.IsDark ? Theme.Control : base.StatusStripGradientBegin;
        public override Color StatusStripGradientEnd => Theme.IsDark ? Theme.Control : base.StatusStripGradientEnd;
        public override Color ToolStripBorder => Theme.IsDark ? Theme.Border : base.ToolStripBorder;
        public override Color ToolStripContentPanelGradientBegin => Theme.IsDark ? Theme.Control : base.ToolStripContentPanelGradientBegin;
        public override Color ToolStripContentPanelGradientEnd => Theme.IsDark ? Theme.Control : base.ToolStripContentPanelGradientEnd;
        public override Color ToolStripDropDownBackground => Theme.IsDark ? Theme.ControlAlt : base.ToolStripDropDownBackground;
        public override Color ToolStripGradientBegin => Theme.IsDark ? Theme.Control : base.ToolStripGradientBegin;
        public override Color ToolStripGradientMiddle => Theme.IsDark ? Theme.Control : base.ToolStripGradientMiddle;
        public override Color ToolStripGradientEnd => Theme.IsDark ? Theme.Control : base.ToolStripGradientEnd;
        public override Color ToolStripPanelGradientBegin => Theme.IsDark ? Theme.Control : base.ToolStripPanelGradientBegin;
        public override Color ToolStripPanelGradientEnd => Theme.IsDark ? Theme.Control : base.ToolStripPanelGradientEnd;
    }
}
