using System;
using System.Drawing;

namespace Sledge.Common.Shell.Theming
{
    /// <summary>
    /// The one place the editor's UI colours come from. Anything that draws its own UI (custom controls,
    /// owner-drawn lists, tool strip renderers) reads from here at paint time instead of hardcoding colours,
    /// so switching between light and dark never leaves a half-themed control behind.
    /// The colours are plain RGB values (no SystemColors), so this works in every assembly.
    /// </summary>
    public static class Theme
    {
        /// <summary>True when the dark palette is active.</summary>
        public static bool IsDark { get; private set; }

        /// <summary>Raised after the palette changed. Raised on the thread that called <see cref="Set"/>.</summary>
        public static event EventHandler Changed;

        /// <summary>Switch palette. Does nothing (and raises nothing) if the palette is already active.</summary>
        public static void Set(bool dark)
        {
            if (IsDark == dark) return;
            IsDark = dark;
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static Color Pick(Color dark, Color light) => IsDark ? dark : light;
        private static Color Rgb(int r, int g, int b) => Color.FromArgb(r, g, b);

        /// <summary>Window / dialog / panel background.</summary>
        public static Color Control => Pick(Rgb(50, 50, 50), Rgb(240, 240, 240));

        /// <summary>Background of text boxes, lists and trees.</summary>
        public static Color Input => Pick(Rgb(35, 35, 35), Rgb(255, 255, 255));

        /// <summary>Raised surfaces: buttons, menus, header rows, inactive tabs.</summary>
        public static Color ControlAlt => Pick(Rgb(70, 70, 70), Rgb(227, 227, 227));

        /// <summary>Normal text.</summary>
        public static Color Text => Pick(Rgb(240, 240, 240), Rgb(0, 0, 0));

        /// <summary>Text of disabled items.</summary>
        public static Color TextDisabled => Pick(Rgb(140, 140, 140), Rgb(109, 109, 109));

        /// <summary>Normal 1px borders and separators.</summary>
        public static Color Border => Pick(Rgb(100, 100, 100), Rgb(160, 160, 160));

        /// <summary>Outline of flat buttons.</summary>
        public static Color ButtonBorder => Pick(Rgb(100, 100, 100), Rgb(105, 105, 105));

        /// <summary>Emphasised borders (checked buttons, focused items).</summary>
        public static Color BorderStrong => Rgb(140, 140, 140);

        /// <summary>The lit edge next to a <see cref="Border"/> line (the white line of a 3D edge in light mode).</summary>
        public static Color BorderLight => Pick(Rgb(75, 75, 75), Rgb(255, 255, 255));

        /// <summary>Mouse-over background.</summary>
        public static Color Hover => Pick(Rgb(85, 85, 85), Rgb(225, 225, 225));

        /// <summary>Background of a checked (toggled on) button.</summary>
        public static Color Checked => Pick(Rgb(105, 105, 105), Rgb(200, 200, 200));

        /// <summary>Background of a pressed button.</summary>
        public static Color Pressed => Pick(Rgb(125, 125, 125), Rgb(180, 180, 180));

        /// <summary>Background of the selected row / item in lists.</summary>
        public static Color Selection => Pick(Rgb(75, 100, 135), Rgb(0, 120, 215));

        /// <summary>Text on a <see cref="Selection"/> background.</summary>
        public static Color SelectionText => Rgb(255, 255, 255);

        /// <summary>Highlight outlines (focus / hot-track).</summary>
        public static Color Accent => Pick(Rgb(90, 150, 220), Rgb(0, 120, 215));

        /// <summary>Strip behind the document tabs.</summary>
        public static Color TabStrip => Control;

        /// <summary>Document tab, not selected.</summary>
        public static Color Tab => Pick(Rgb(70, 70, 70), Rgb(240, 240, 240));

        /// <summary>Document tab under the mouse.</summary>
        public static Color TabHover => Pick(Rgb(85, 85, 85), Rgb(248, 248, 248));

        /// <summary>The selected document tab.</summary>
        public static Color TabSelected => Pick(Rgb(105, 105, 105), Rgb(255, 255, 255));

        /// <summary>Lighten (amount &gt; 0) or darken (amount &lt; 0) a colour by moving each channel towards white / black.</summary>
        public static Color Shade(Color c, float amount)
        {
            var target = amount >= 0 ? 255 : 0;
            var t = Math.Min(1f, Math.Abs(amount));
            return Color.FromArgb(c.A,
                (int) (c.R + (target - c.R) * t),
                (int) (c.G + (target - c.G) * t),
                (int) (c.B + (target - c.B) * t));
        }
    }
}
