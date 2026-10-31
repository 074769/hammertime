using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Sledge.Common.Logging
{
    /// <summary>
    /// Lightweight timing for finding which converters and overlays are slow.
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
        private static readonly object FileLock = new object();
        private static long _lastFlushTimestamp = Stopwatch.GetTimestamp();

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
        /// Write everything recorded since the last flush under a title, then clear the totals.
        /// </summary>
        public static void Flush(string title)
        {
            if (!Enabled) return;
            _lastFlushTimestamp = Stopwatch.GetTimestamp();

            var lines = new System.Collections.Generic.List<(string Name, long Ticks, long Count, long Max)>();
            foreach (var key in Buckets.Keys)
            {
                if (!Buckets.TryRemove(key, out var bucket)) continue;
                lock (bucket)
                {
                    lines.Add((key, bucket.Ticks, bucket.Count, bucket.MaxTicks));
                }
            }
            if (lines.Count == 0) return;

            lines.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {title}");
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
        /// Flush only if at least the given interval has passed since the last flush. Use for per-frame code.
        /// </summary>
        public static void FlushIfDue(string title, int intervalMs)
        {
            if (!Enabled) return;
            var elapsed = Stopwatch.GetTimestamp() - _lastFlushTimestamp;
            if (ToMs(elapsed) >= intervalMs) Flush(title);
        }

        private static double ToMs(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }
}
