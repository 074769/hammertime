using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using LogicAndTrick.Oy;
using Sledge.Common.Logging;
using Sledge.Common.Shell.Hooks;

namespace Sledge.Shell
{
    /// <summary>
    /// The class that bootstraps the whole thing.
    /// </summary>
    [Export]
    internal class Bootstrapper
    {
        private readonly List<IStartupHook> _startupHooks;
        private readonly List<IUIStartupHook> _uiStartupHooks;
        private readonly List<IInitialiseHook> _initialiseHooks;
        private readonly List<IShuttingDownHook> _shuttingDownHooks;
        private readonly List<IUIShutdownHook> _uiShutdownHooks;
        private readonly List<IShutdownHook> _shutdownHooks;

        [ImportingConstructor]
        public Bootstrapper
        (
            [ImportMany] IEnumerable<Lazy<IStartupHook>> startupHooks,
            [ImportMany] IEnumerable<Lazy<IUIStartupHook>> uiStartupHooks,
            [ImportMany] IEnumerable<Lazy<IInitialiseHook>> initialiseHooks,
            [ImportMany] IEnumerable<Lazy<IShuttingDownHook>> shuttingDownHooks,
            [ImportMany] IEnumerable<Lazy<IUIShutdownHook>> uiShutdownHooks,
            [ImportMany] IEnumerable<Lazy<IShutdownHook>> shutdownHooks
        )
        {
            using (StartupLog.Scope("Bootstrapper: create all hook instances"))
            {
                _startupHooks = Resolve("startup", startupHooks);
                _uiStartupHooks = Resolve("UI startup", uiStartupHooks);
                _initialiseHooks = Resolve("initialise", initialiseHooks);
                _shuttingDownHooks = Resolve("shutting down", shuttingDownHooks);
                _uiShutdownHooks = Resolve("UI shutdown", uiShutdownHooks);
                _shutdownHooks = Resolve("shutdown", shutdownHooks);
            }
        }

        /// <summary>
        /// Instantiate the hooks (MEF creates the part and its imports on first .Value), timing each one.
        /// </summary>
        private static List<T> Resolve<T>(string kind, IEnumerable<Lazy<T>> lazies)
        {
            var list = new List<T>();
            foreach (var lazy in lazies)
            {
                var start = StartupLog.Now;
                var value = lazy.Value;
                StartupLog.Done("Create " + kind + " hook: " + value.GetType().FullName, start);
                list.Add(value);
            }
            return list;
        }

        private void Handle<T>(string description, T hook, Action<T> function)
        {
            var name = description + ": " + hook.GetType().FullName;
            var start = StartupLog.Now;
            try
            {
#if DEBUG
                Log.Debug("Bootstrapper", description + ": " + hook.GetType().FullName);
#endif
                function(hook);
            }
            catch (Exception e)
            {
                StartupLog.Mark("FAILED " + name + " - " + e.GetType().Name + ": " + e.Message);
                Oy.Publish("Shell:UnhandledException", e);
            }
            finally
            {
                StartupLog.Done(name, start);
            }
        }

        private async Task Handle<T>(string description, T hook, Func<T, Task> function)
        {
            var name = description + ": " + hook.GetType().FullName;
            var start = StartupLog.Now;
            try
            {
#if DEBUG
                Log.Debug("Bootstrapper", description + ": " + hook.GetType().FullName);
#endif
                await function(hook);
            }
            catch (Exception e)
            {
                StartupLog.Mark("FAILED " + name + " - " + e.GetType().Name + ": " + e.Message);
                Oy.Publish("Shell:UnhandledException", e);
            }
            finally
            {
                StartupLog.Done(name, start);
            }
        }

        /// <summary>
        /// Run all UI startup hooks
        /// </summary>
        public void UIStartup()
        {
            var phase = StartupLog.Now;
            foreach (var hook in _uiStartupHooks.OrderBy(x => x.GetType().Name))
            {
                Handle("UI Startup hook", hook, h => h.OnUIStartup());
            }
            StartupLog.Done("PHASE: all UI startup hooks", phase);
        }

        /// <summary>
        /// Run all startup hooks
        /// </summary>
        /// <returns>The running task</returns>
        public async Task Startup()
        {
            var phase = StartupLog.Now;
            foreach (var export in _startupHooks.OrderBy(x => x.GetType().FullName))
            {
                await Handle("Startup hook", export, h => h.OnStartup());
            }
            StartupLog.Done("PHASE: all startup hooks", phase);
        }

        /// <summary>
        /// Run all initialise hooks
        /// </summary>
        /// <returns>The running task</returns>
        public async Task Initialise()
        {
            var phase = StartupLog.Now;
            foreach (var export in _initialiseHooks.OrderBy(x => x.GetType().FullName))
            {
                await Handle("Initialise hook", export, h => h.OnInitialise());
            }
            StartupLog.Done("PHASE: all initialise hooks", phase);
        }

        /// <summary>
        /// Run all shutting down hooks and stops if cancelled
        /// </summary>
        /// <returns>False if shutdown cannot continue</returns>
        public async Task<bool> ShuttingDown()
        {
            foreach (var export in _shuttingDownHooks.OrderBy(x => x.GetType().FullName))
            {
                try
                {
                    Log.Debug("Bootstrapper", "Shutting down hook: " + export.GetType().FullName);
                    if (!await export.OnShuttingDown()) return false;
                }
                catch
                {
                    // We're shutting down anyway, so don't report this one
                }
            }
            return true;
        }

        /// <summary>
        /// Run all UI shutdown hooks
        /// </summary>
        public void UIShutdown()
        {
            foreach (var hook in _uiShutdownHooks.OrderBy(x => x.GetType().Name))
            {
                Handle("UI Shutdown hook", hook, h => h.OnUIShutdown());
            }
        }

        /// <summary>
        /// Run all shutdown hooks
        /// </summary>
        /// <returns>The running task</returns>
        public async Task Shutdown()
        {
            foreach (var export in _shutdownHooks.OrderBy(x => x.GetType().FullName))
            {
                await Handle("Shutdown hook", export, h => h.OnShutdown());
            }
        }
    }
}