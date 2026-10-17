using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Sledge.Common.Logging
{
    /// <summary>
    /// A small, synchronous, dependency-free logger for measuring application start up time.
    /// It writes "startup-timing.log" next to the executable (the previous run is kept as
    /// "startup-timing.prev.log"). It deliberately does not use the Oy based <see cref="Log"/>,
    /// because that does not exist yet while the container is still being built.
    ///
    /// Every line starts with the time in milliseconds since the process was created.
    /// Timed steps also print how long they took. When <see cref="Finish"/> is called a summary of the
    /// slowest steps is appended and logging stops.
    /// </summary>
    public static class StartupLog
    {
        private sealed class Step
        {
            public string Name;
            public double Ms;
        }

        private static readonly object Lock = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<Step> Steps = new List<Step>();
        private static readonly double ProcessOffsetMs;
        private static StreamWriter _writer;
        private static bool _finished;
        private static bool _assemblyEvents;

        [ThreadStatic] private static bool _inWrite;

        /// <summary>
        /// The file the log is written to, or null if it could not be opened.
        /// </summary>
        public static string FilePath { get; private set; }

        /// <summary>
        /// Milliseconds since logging started. Pass to <see cref="Done"/> to record a step.
        /// </summary>
        public static double Now => Clock.Elapsed.TotalMilliseconds;

        static StartupLog()
        {
            try
            {
                var sinceProcessStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                ProcessOffsetMs = Math.Max(0, sinceProcessStart - Clock.Elapsed.TotalMilliseconds);
            }
            catch
            {
                ProcessOffsetMs = 0;
            }

            try
            {
                OpenWriter();
                WriteHeader();

                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                _assemblyEvents = true;
            }
            catch
            {
                // Logging must never stop the application from starting
            }
        }

        private static void OpenWriter()
        {
            var dirs = new[] { AppDomain.CurrentDomain.BaseDirectory, Path.GetTempPath() };
            foreach (var dir in dirs)
            {
                try
                {
                    var path = Path.Combine(dir, "startup-timing.log");
                    var prev = Path.Combine(dir, "startup-timing.prev.log");
                    if (File.Exists(path)) File.Copy(path, prev, true);

                    var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                    FilePath = path;
                    return;
                }
                catch
                {
                    // Not writable (e.g. Program Files), try the next location
                }
            }
        }

        private static void WriteHeader()
        {
            Raw("Hammertime startup timing - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Raw("Times in [brackets] are milliseconds since the process was created. 'took' is the duration of that step.");
            Raw("Runtime: " + RuntimeInformation.FrameworkDescription + ", " + RuntimeInformation.OSDescription + ", " + (Environment.Is64BitProcess ? "x64" : "x86 (32-bit)"));
#if DEBUG
            Raw("Build: Debug (JIT and debug logging make this slower than Release)");
#else
            Raw("Build: Release");
#endif
            Raw("Directory: " + AppDomain.CurrentDomain.BaseDirectory);
            Raw("Command line: " + Environment.CommandLine);
            Raw("Processors: " + Environment.ProcessorCount);
            Raw("");
            Mark("Logger started (everything before this is .NET host and runtime start)");
        }

        private static double Elapsed => ProcessOffsetMs + Clock.Elapsed.TotalMilliseconds;

        private static void Raw(string line)
        {
            lock (Lock)
            {
                if (_writer == null) return;
                try { _writer.WriteLine(line); }
                catch { /* ignore */ }
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (_inWrite || _finished) return;
            try
            {
                var name = args.LoadedAssembly.GetName().Name;
                Mark("  assembly loaded: " + name);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Write a message with the current time.
        /// </summary>
        public static void Mark(string message)
        {
            if (_finished || _inWrite) return;
            lock (Lock)
            {
                if (_writer == null || _finished) return;
                _inWrite = true;
                try
                {
                    _writer.WriteLine("[" + Elapsed.ToString("F1", CultureInfo.InvariantCulture).PadLeft(9) + "] " + message);
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _inWrite = false;
                }
            }
        }

        /// <summary>
        /// Record a step that started at <paramref name="startMs"/> (a value from <see cref="Now"/>) and ends now.
        /// </summary>
        public static void Done(string name, double startMs)
        {
            if (_finished) return;
            var ms = Now - startMs;
            lock (Lock)
            {
                Steps.Add(new Step { Name = name, Ms = ms });
            }
            Mark(name + " - took " + ms.ToString("F1", CultureInfo.InvariantCulture) + " ms");
        }

        /// <summary>
        /// Time a block of code: <c>using (StartupLog.Scope("name")) { ... }</c>
        /// </summary>
        public static IDisposable Scope(string name)
        {
            return new ScopeImpl(name);
        }

        private sealed class ScopeImpl : IDisposable
        {
            private readonly string _name;
            private readonly double _start;
            private bool _disposed;

            public ScopeImpl(string name)
            {
                _name = name;
                _start = Now;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Done(_name, _start);
            }
        }

        /// <summary>
        /// Write the summary of the slowest steps and stop logging.
        /// </summary>
        public static void Finish()
        {
            if (_finished) return;

            Mark("Startup finished");

            lock (Lock)
            {
                if (_finished) return;
                _finished = true;

                try
                {
                    if (_assemblyEvents) AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

                    if (_writer != null)
                    {
                        _writer.WriteLine();
                        _writer.WriteLine("Total since process start: " + Elapsed.ToString("F0", CultureInfo.InvariantCulture) + " ms");
                        _writer.WriteLine("Working set: " + (Environment.WorkingSet / (1024 * 1024)) + " MB, GC collections gen0/1/2: "
                                          + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2));
                        _writer.WriteLine();
                        _writer.WriteLine("Slowest steps (steps can nest, so their times overlap):");
                        foreach (var s in Steps.OrderByDescending(x => x.Ms).Take(30))
                        {
                            _writer.WriteLine(s.Ms.ToString("F1", CultureInfo.InvariantCulture).PadLeft(10) + " ms  " + s.Name);
                        }
                        _writer.Flush();
                        _writer.Dispose();
                    }
                }
                catch
                {
                    // ignore
                }
                finally
                {
                    _writer = null;
                }
            }
        }
    }
}
