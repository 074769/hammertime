using System;
using System.ComponentModel;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common;
using Sledge.Common.Logging;

namespace Sledge.Shell
{
	/// <summary>
	/// Class to bootstrap the shell
	/// </summary>
	public static class Startup
	{
		/// <summary>
		/// Called before the composition container is created, but after the default catalog has been created.
		/// </summary>
		public static event EventHandler<AggregateCatalog> BuildCatalog;

		/// <summary>
		/// Run the shell as an application using a container from the application catalog
		/// </summary>
		public static void Run()
		{
			try
			{
				StartupLog.Mark("Startup.Run: building MEF catalog (the assembly scan itself runs lazily, see 'catalog:' lines)");
				var catalog = new AggregateCatalog();

				catalog.Catalogs.Add(new ValidAssembliesInDirectoryContainer(AppDomain.CurrentDomain.BaseDirectory));
				using (StartupLog.Scope("BuildCatalog event handlers"))
				{
					BuildCatalog?.Invoke(null, catalog);
				}

				CompositionContainer container;
				using (StartupLog.Scope("Create CompositionContainer"))
				{
					container = new CompositionContainer(catalog, CompositionOptions.DisableSilentRejection);
				}

				Run(container);
			}
			catch (Exception e)
			{
				StartupLog.Mark("Startup FAILED: " + e.Message);
				StartupLog.Finish();
				using (StreamWriter outputFile = new StreamWriter("./startup.log"))
				{
					outputFile.WriteLine(DateTime.Now);
					outputFile.WriteLine(".Net Version: {0}", Environment.Version.ToString());

					var path = Path.GetFullPath("./");
					outputFile.WriteLine($"Directory: {Path.GetFullPath("./")}");
					outputFile.WriteLine("Present files:");
                    foreach (var file in Directory.GetFiles(path))
                    {
						outputFile.WriteLine(new FileInfo(file).Length);
						outputFile.WriteLine(Path.GetFileName(file));
						outputFile.WriteLine("----");
                    }
                    outputFile.WriteLine(e.Message);
					outputFile.WriteLine(e.ToString());
				}
			}
		}

		/// <summary>
		/// Run the shell with a custom container
		/// </summary>
		/// <param name="container">The container</param>
		public static void Run(CompositionContainer container)
		{
			Common.Container.Initialise(container);

			using (StartupLog.Scope("WinForms visual styles setup"))
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
			}

			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += (s, e) => UnhandledException(e.Exception);
			AppDomain.CurrentDomain.UnhandledException += (s, e) => UnhandledException(e.ExceptionObject as Exception);

			Oy.UnhandledException += (s, e) => UnhandledException(e.Exception);

			Forms.Shell shell;
			using (StartupLog.Scope("Resolve Shell export (MEF composition: assembly scan, part creation, Shell constructor)"))
			{
				shell = container.GetExport<Forms.Shell>().Value;
			}
			var si = new SingleInstance(shell);
			

			si.UnhandledException += (s, e) =>
			{
				e.ExitApplication = false;
				UnhandledException(e.Exception);
			};

			EventHandler firstIdle = null;
			firstIdle = (s, e) =>
			{
				Application.Idle -= firstIdle;
				StartupLog.Mark("First Application.Idle (message loop is running and the UI is responsive)");
			};
			Application.Idle += firstIdle;

			StartupLog.Mark("Entering SingleInstance.Run (creates the main window, runs Shell.OnLoad)");
			si.Run(Environment.GetCommandLineArgs());
		}

		private static void UnhandledException(Exception ex)
		{
			if (ex == null) return;
			try
			{
				Oy.Publish("Shell:UnhandledException", ex);
			}
			catch
			{
				// Exception in an exception? Oh dear...
			}
		}
	}
}