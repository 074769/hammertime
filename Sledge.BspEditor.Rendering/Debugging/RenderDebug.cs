using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Sledge.BspEditor.Rendering.Debugging
{
    /// <summary>
    /// Frame-stamped trace of the overlay and scene rendering, written to hammertime-render-debug.log in the temp folder.
    /// Only active when the HAMMERTIME_RENDER_DEBUG environment variable is set to 1.
    /// </summary>
    public static class RenderDebug
    {
        public static readonly bool Enabled = System.Environment.GetEnvironmentVariable("HAMMERTIME_RENDER_DEBUG") == "1";

        private static readonly object _lock = new object();
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static readonly string _path = Path.Combine(Path.GetTempPath(), "hammertime-render-debug.log");
        private static long _frame;

        /// <summary>
        /// Sets the frame number stamped on following lines. The overlay update passes in the engine's frame counter.
        /// </summary>
        public static void SetFrame(long frame)
        {
            if (!Enabled) return;
            Interlocked.Exchange(ref _frame, frame);
        }

        public static void Write(string category, string message)
        {
            if (!Enabled) return;
            var line = $"{_clock.Elapsed.TotalMilliseconds,12:F2}ms  frame {Interlocked.Read(ref _frame),8}  thread {Thread.CurrentThread.ManagedThreadId,3}  {category,-8} {message}";
            lock (_lock)
            {
                File.AppendAllText(_path, line + System.Environment.NewLine);
            }
        }
    }
}
