using System.Drawing;
using Sledge.Common.Shell.Context;

namespace Sledge.Common.Shell.Menu
{
    /// <summary>
    /// A command can implement this to get a small "options" handle on the right of its menu entry.
    /// Clicking the entry itself runs the command as normal (single click);
    /// clicking the handle opens an options popup instead and does not run the command.
    /// </summary>
    public interface IMenuItemOptions
    {
        /// <summary>
        /// Open the options popup.
        /// </summary>
        /// <param name="context">The current context</param>
        /// <param name="screenLocation">Screen position to show the popup at (right edge of the menu entry)</param>
        void ShowOptions(IContext context, Point screenLocation);
    }
}
