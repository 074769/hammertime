using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Sledge.Common.Logging
{
    /// <summary>
    /// Lightweight timing for finding slow parts of the editor: scene rebuilds, the engine frame loop, every
    /// pipeline, overlay and listener, in the 2D and 3D views.
    /// Turn on with the environment variable HAMMERTIME_PERF=1, or by creating an empty file called
    /// perf.enable next to the executable. Results are appended to perf.log next to the executable.
    /// When disabled, Measure does no work and allocates nothing.
    /// </summary>
    public static class PerfLog
    {
        public static readonly bool Enabled =
            Environment.GetEnvironmentVariable("HAMMERTIME_PERF") == "1" ||
            File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.enable"));

        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "perf.log");
        private static readonly ConcurrentDictionary<string, Bucket> Buckets = new ConcurrentDictionary<string, Bucket>();
        private static readonly ConcurrentDictionary<string, long> LastFlush = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<int, string> ViewLabels = new ConcurrentDictionary<int, string>();
        private static readonly object FileLock = new object();

        private class Bucket
        {
            public long Ticks;
            public long Count;
            public long MaxTicks;
        }

        /// <summary>
        /// Times a block of code. Dispose it to record the elapsed time. Default value does nothing.
        /// </summary>
        public struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _start;

            internal Scope(string name)
            {
                _name = name;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                if (_name == null) return;
                Record(_name, Stopwatch.GetTimestamp() - _start);
            }
        }

        /// <summary>
        /// Start timing a named block.
        /// </summary>
        public static Scope Measure(string name)
        {
            return Enabled ? new Scope(name) : default;
        }

        /// <summary>
        /// Start timing a block for a subject, recorded as "category:TypeName". The name is only built when enabled.
        /// </summary>
        public static Scope Measure(string category, object subject)
        {
            return Enabled ? new Scope(category + ":" + subject.GetType().Name) : default;
        }

        /// <summary>
        /// Like Measure(category, subject), but tagged with a viewport id so each view is listed separately.
        /// </summary>
        public static Scope MeasureForView(string category, object subject, int viewportId)
        {
            return Enabled ? new Scope(category + ":" + subject.GetType().Name + " [view " + viewportId + "]") : default;
        }

        /// <summary>
        /// Like Measure(name), but tagged with a viewport id so each view is listed separately.
        /// </summary>
        public static Scope MeasureForView(string name, int viewportId)
        {
            return Enabled ? new Scope(name + " [view " + viewportId + "]") : default;
        }

        /// <summary>
        /// Counts an event that has no duration (for example a frame that was skipped), tagged with a viewport id.
        /// </summary>
        public static void CountForView(string name, int viewportId)
        {
            if (!Enabled) return;
            Record(name + " [view " + viewportId + "]", 0);
        }

        /// <summary>
        /// Remembers what a viewport is (for example its camera type). Shown at the top of each log block.
        /// </summary>
        public static void SetViewLabel(int viewportId, string label)
        {
            if (!Enabled) return;
            ViewLabels[viewportId] = label;
        }

        private static void Record(string name, long ticks)
        {
            var bucket = Buckets.GetOrAdd(name, _ => new Bucket());
            lock (bucket)
            {
                bucket.Ticks += ticks;
                bucket.Count++;
                if (ticks > bucket.MaxTicks) bucket.MaxTicks = ticks;
            }
        }

        /// <summary>
        /// Write the totals recorded under the given name prefixes (all of them if none are given), then clear
        /// those totals. Other areas keep their totals for their own flush.
        /// </summary>
        public static void Flush(string title, params string[] prefixes)
        {
            if (!Enabled) return;

            var lines = new List<(string Name, long Ticks, long Count, long Max)>();
            foreach (var key in Buckets.Keys)
            {
                if (prefixes.Length > 0 && !prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal))) continue;
                if (!Buckets.TryRemove(key, out var bucket)) continue;
                lock (bucket)
                {
                    lines.Add((key, bucket.Ticks, bucket.Count, bucket.MaxTicks));
                }
            }
            if (lines.Count == 0) return;

            lines.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {title}");
            if (!ViewLabels.IsEmpty)
            {
                sb.AppendLine("  views: " + string.Join(", ", ViewLabels.OrderBy(x => x.Key).Select(x => x.Key + "=" + x.Value)));
            }
            foreach (var l in lines)
            {
                var total = ToMs(l.Ticks);
                var avg = l.Count > 0 ? total / l.Count : 0;
                sb.AppendLine($"  {l.Name,-60} total {total,10:F2} ms  calls {l.Count,8}  avg {avg,8:F4} ms  max {ToMs(l.Max),8:F3} ms");
            }

            try
            {
                lock (FileLock) File.AppendAllText(LogPath, sb.ToString());
            }
            catch
            {
                // Logging must never break the editor
            }
        }

        /// <summary>
        /// Flush the given name prefixes only if at least the given interval has passed since this title was last
        /// flushed. Use for per-frame code.
        /// </summary>
        public static void FlushIfDue(string title, int intervalMs, params string[] prefixes)
        {
            if (!Enabled) return;
            var now = Stopwatch.GetTimestamp();
            var last = LastFlush.GetOrAdd(title, now);
            if (ToMs(now - last) < intervalMs) return;
            LastFlush[title] = now;
            Flush(title, prefixes);
        }

        private static double ToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
