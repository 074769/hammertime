using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Tools.Vertex.Controls
{
    /// <summary>
    /// Small helpers for the manual (proportional) layouts used by the vertex tool controls.
    /// Every layout pass can run in "measure only" mode (apply = false) to work out the height needed.
    /// </summary>
    internal static class VertexControlLayout
    {
        public static int TextWidth(Control c)
        {
            return TextRenderer.MeasureText(c.Text ?? "", c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;
        }

        public static void Bounds(bool apply, Control c, int x, int y, int w, int h)
        {
            if (apply) c.SetBounds(x, y, w, h);
        }

        public static void Move(bool apply, Control c, int x, int y)
        {
            if (apply) c.Location = new Point(x, y);
        }
    }
}
