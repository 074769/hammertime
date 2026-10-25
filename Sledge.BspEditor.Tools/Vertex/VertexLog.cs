using System;
using Sledge.Common.Logging;

namespace Sledge.BspEditor.Tools.Vertex
{
    /// <summary>
    /// Diagnostic messages for the vertex tool. They appear in the Log panel.
    /// </summary>
    internal static class VertexLog
    {
        private const string Source = "Vertex Tool";
        private static readonly object _lock = new object();
        private static string _lastRender;

        public static void Info(string message)
        {
            Log.Info(Source, message);
        }

        public static void Warning(string message, Exception ex = null)
        {
            Log.Warning(Source, message, ex);
        }

        /// <summary>
        /// Logs what the tool drew, but only when it changes, since the render runs every frame.
        /// </summary>
        public static void RenderState(string state)
        {
            lock (_lock)
            {
                if (state == _lastRender) return;
                _lastRender = state;
            }
            Info("Render draws: " + (string.IsNullOrEmpty(state) ? "nothing" : state));
        }
    }
}
