using LogicAndTrick.Oy;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Translations;
using Sledge.Common.Transport;
using Sledge.Shell.Components;
using Sledge.Shell.Controls;
using Sledge.Shell.Registers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sledge.Shell.Forms
{
	/// <summary>
	/// The application's base window
	/// </summary>
	[Export]
	[Export("Shell", typeof(Form))]
	[Export(typeof(ISettingsContainer))]
	[AutoTranslate]
	internal partial class Shell : BaseForm, ISettingsContainer
	{
		private readonly object _lock = new object();

		private readonly Lazy<Bootstrapper> _bootstrapper;
		private readonly Lazy<DocumentRegister> _documentRegister;
		private readonly Lazy<SaveAsService> _saveAs;
		private readonly Lazy<ITranslationStringProvider> _translation;

		public string Title { get; set; } = "Hammertime Shell";

		public string UnsavedChanges { get; set; } = "Unsaved changes in file";
		public string SaveChangesToFile { get; set; } = "Save changes to {0}?";
		public bool ValuesLoaded { get; private set; } = false;


		internal ToolStripPanel ToolStrip => ToolStripContainer.TopToolStripPanel;
		internal ToolStripContainer ToolbarContainer => ToolStripContainer;

		[ImportingConstructor]
		public Shell(
			[Import] Lazy<Bootstrapper> bootstrapper,
			[Import] Lazy<DocumentRegister> documentRegister,
			[Import] Lazy<SaveAsService> saveAs,
			[Import] Lazy<ITranslationStringProvider> translation
		)
		{
			_bootstrapper = bootstrapper;
			_documentRegister = documentRegister;
			_saveAs = saveAs;
			_translation = translation;

			InitializeComponent();
			InitializeShell();
			this.Opacity = 0;
			this.Enabled = false; // Ignore input until startup has finished
            AllowDrop = true;
            this.DragEnter += Shell_DragEnter;
            this.DragDrop += Shell_DragDrop;

        }

        private async void Shell_DragDrop(object sender, DragEventArgs e)
        {
			var args = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var arg in args)
            {
                Common.Logging.Log.Debug(nameof(Shell), $"Command line: `{arg}`");
                if (!File.Exists(arg)) continue;

                await _documentRegister.Value.OpenDocument(arg);
            }
        }

        void Shell_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        /// <summary>
        /// Setup the shell pre-startup
        /// </summary>
        private void InitializeShell()
		{
			DocumentTabs.TabPages.Clear();
			LayoutTabStrip();

			Oy.Subscribe<List<string>>("Shell:InstanceOpened", async a => await this.InvokeAsync(() => InstanceOpened(a)));

			Oy.Subscribe<IDocument>("Document:Opened", async d => await this.InvokeAsync(() => OpenDocument(d)));
			Oy.Subscribe<IDocument>("Document:Closed", async d => await this.InvokeLaterAsync(() => CloseDocument(d)));
			Oy.Subscribe<IDocument>("Document:Changed", async d => await this.InvokeAsync(() => DocumentChanged(d)));
			Oy.Subscribe<IDocument>("Document:Activated", async d => await this.InvokeAsync(() => DocumentActivated(d)));
			Oy.Subscribe<DocumentCloseMessage>("Document:RequestClose", async d => await this.InvokeAsync(() => DocumentRequestClose(d)));

			Oy.Subscribe<string>("Shell:OpenCommandBox", async o => await this.InvokeAsync(() => OpenCommandBox(o)));

			Oy.Subscribe<Exception>("Shell:UnhandledException", ShowExceptionDialog);
			Oy.Subscribe<Exception>("Shell:UnhandledExceptionOnce", ShowExceptionDialogOnce);
		}

		// Error handling

		private Task ShowExceptionDialog(Exception obj)
		{
			this.InvokeLater(() =>
			{
				var ed = new ExceptionWindow(obj);
				ed.ShowDialog(this);
			});
			return Task.CompletedTask;
		}

		private Task ShowExceptionDialogOnce(Exception obj)
		{
			return ShowException(obj) ? ShowExceptionDialog(obj) : Task.CompletedTask;
		}

		private readonly HashSet<string> _shownExceptions = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

		private string GetExceptionData(Exception exception)
		{
			return exception == null ? "" : $"{exception.Message}|{exception.StackTrace}|{GetExceptionData(exception.InnerException)}";
		}

		private bool ShowException(Exception exception)
		{
			var exData = GetExceptionData(exception);
			if (_shownExceptions.Contains(exData)) return false;

			_shownExceptions.Add(exData);
			return true;
		}

		// Lifecycle

		private async Task InstanceOpened(IEnumerable<string> args)
		{
			foreach (var arg in args.Skip(1))
			{
				Common.Logging.Log.Debug(nameof(Shell), $"Command line: `{arg}`");
				if (!File.Exists(arg)) continue;

				await _documentRegister.Value.OpenDocument(arg);
			}
		}

		protected override void OnLoad(EventArgs e)
		{
			// Bootstrap the shell
			_bootstrapper.Value.UIStartup();
			StartupSequence();

			// Set up bootstrapping for shutdown
			Closing += DoClosing;

			Text = Title;
		}

		// While the previous session's documents are being reopened, the tab strip's own selection events
		// are ignored: each document is activated explicitly as it opens, and the tab control can raise a
		// late selection change for the first tab, which would switch the views back to the first file.
		private volatile bool _restoringSession;

		/// <summary>
		/// Makes sure the newest document's tab is the selected one, and that it is the active document.
		/// </summary>
		private void ReselectLastDocument()
		{
			var last = _documentRegister.Value.OpenDocuments.LastOrDefault();
			if (last == null) return;

			var page = DocumentTabs.TabPages.OfType<TabPage>().FirstOrDefault(x => x.Tag == last);
			if (page == null) return;

			if (DocumentTabs.SelectedTab != page) DocumentTabs.SelectedTab = page;
			_documentRegister.Value.ActivateDocument(last);
		}

		/// <summary>
		/// Runs the startup hooks, settings and reopened documents in order. The shell is revealed only after
		/// all of them have finished, whether or not one of them failed.
		/// </summary>
		private async void StartupSequence()
		{
			try
			{
				Startup.SetSplashStatus("Starting up...", 40);
				await _bootstrapper.Value.Startup();

				Startup.SetSplashStatus("Loading settings...", 60);
				await _bootstrapper.Value.Initialise();

				Startup.SetSplashStatus("Opening maps...", 80);
				await PostLoad();
			}
			catch (Exception e)
			{
				Oy.Publish("Shell:UnhandledException", e);
			}
			finally
			{
				this.InvokeLater(ShowWhenReady);
			}
		}

		/// <summary>
		/// Reveal the shell now that startup has finished, then tell listeners (the splash screen) it is ready.
		/// </summary>
		private void ShowWhenReady()
		{
			Enabled = true;
			Opacity = 1;
			Activate();
			Ready?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Raised once the shell is visible and all startup work, including reopening documents, has finished.
		/// </summary>
		internal event EventHandler Ready;

		private async Task PostLoad()
		{
			_restoringSession = true;
			try
			{
				if (_openDocuments != null && _openLastFiles)
				{
					var total = _openDocuments.Count;
					for (var i = 0; i < total; i++)
					{
						Startup.SetSplashStatus($"Opening maps ({i + 1}/{total})...", 80 + 18 * i / total);
						var dp = _openDocuments[i];
						await _documentRegister.Value.OpenDocument(dp.ToPointer(), dp.Loader);
					}
				}
				await Oy.Publish("Shell:InstanceOpened", Environment.GetCommandLineArgs().ToList());
			}
			finally
			{
				// Once the window has settled, go back to the newest document and stop ignoring tab events
				this.InvokeLater(() =>
				{
					EventHandler idle = null;
					idle = (s, e) =>
					{
						Application.Idle -= idle;
						_restoringSession = false;
						ReselectLastDocument();
					};
					Application.Idle += idle;
				});
			}
		}

		private void PersistCurrentOpenDocumentList()
		{
			_openDocuments = new List<LoadedDocument>();
			lock (_lock)
			{
				foreach (var d in _documentRegister.Value.OpenDocuments)
				{
					var pointer = _documentRegister.Value.GetDocumentPointer(d);
					if (pointer == null) continue;

					var dp = new LoadedDocument(pointer);
					_openDocuments.Add(dp);
				}
			}
		}

		private void CancelClose(object sender, CancelEventArgs e) => e.Cancel = true;

		private async void DoClosing(object sender, CancelEventArgs e)
		{
			e.Cancel = true;
			Closing -= DoClosing;
			Closing += CancelClose;

			await Task.Yield();

			// Save any unsaved documents
			if (!await SaveUnsavedDocuments())
			{
				Closing += DoClosing;
				Closing -= CancelClose;
				return;
			}

			// Save the list of documents so we can re-open them next time
			PersistCurrentOpenDocumentList();

			// We've already asked the user if they want to save and they've either saved
			// or chosen to discard changes, so we force the close now.
			foreach (var doc in _documentRegister.Value.OpenDocuments.ToList())
			{
				await _documentRegister.Value.ForceCloseDocument(doc);
			}

			// Close anything else
			if (!await _bootstrapper.Value.ShuttingDown())
			{
				Closing += DoClosing;
				Closing -= CancelClose;
				return;
			}

			// Unsubscribe the event (no infinite loops!) and close for good
			Closing -= CancelClose;
			Enabled = false;
			await _bootstrapper.Value.Shutdown();
			this.InvokeSync(() =>
			{
				_bootstrapper.Value.UIShutdown();
			});
			Close();
		}

		private async Task<bool> SaveUnsavedDocuments()
		{
			var unsaved = _documentRegister.Value.OpenDocuments.Where(x => x.HasUnsavedChanges).ToList();
			if (!unsaved.Any()) return true; // nothing unsaved

			DialogResult result;

			using (var d = new SaveChangesForm(unsaved))
			{
				d.Translate(_translation.Value);
				d.Owner = this;
				result = await d.ShowDialogAsync();
			}

			// Don't continue
			if (result == DialogResult.Cancel) return false;

			// Discard changes and continue
			if (result == DialogResult.No) return true;

			// Save changes and continue
			foreach (var doc in unsaved)
			{
				await SaveDocument(doc);
			}

			return true;
		}

		/// <summary>
		/// Get the list of docking panels in the shell
		/// </summary>
		/// <returns>The list of docking panels</returns>
		internal IEnumerable<DockedPanel> GetDockPanels()
		{
			yield return LeftSidebar;
			yield return RightSidebar;
			yield return BottomSidebar;
		}

		// Subscriptions

		/// <summary>
		/// A document has been opened, add a tab for it
		/// </summary>
		private Task OpenDocument(IDocument document)
		{
			lock (_lock)
			{
				var page = new TabPage
				{
					Text = document.Name,
					Tag = document
				};
				DocumentTabs.TabPages.Add(page);
				DocumentTabs.SelectedIndex = DocumentTabs.TabPages.Count - 1;
				page.ImageKey = document.HasUnsavedChanges ? "Dirty" : "Clean";
			}

			LayoutTabStrip();
			return Task.CompletedTask;
		}

		/// <summary>
		/// A document has been activated, ensure the correct tab is selected
		/// </summary>
		private void DocumentActivated(IDocument document)
		{
			// Update the window title & other things
			if (document == null || document is NoDocument)
			{
				DocumentContainer.Controls.Clear();
				Text = Title;
			}
			else
			{
				var currentControl = DocumentContainer.Controls.OfType<Control>().FirstOrDefault();
				if (currentControl != document.Control)
				{
					DocumentContainer.Controls.Clear();
					DocumentContainer.Controls.Add((Control)document.Control);
					DocumentContainer.Controls[0].Dock = DockStyle.Fill;
				}

				Text = Title + @" - " + document.Name;
			}
		}

		/// <summary>
		/// A document has been closed, remove the tab for it
		/// </summary>
		private Task CloseDocument(IDocument document)
		{
			var page = DocumentTabs.TabPages.OfType<TabPage>().FirstOrDefault(x => x.Tag == document);
			if (page != null) DocumentTabs.TabPages.Remove(page);
			LayoutTabStrip();
			return Task.CompletedTask;
		}

		/// <summary>
		/// A document has changed, make sure the tab has the correct properties
		/// </summary>
		private Task DocumentChanged(IDocument document)
		{
			var page = DocumentTabs.TabPages.OfType<TabPage>().FirstOrDefault(x => x.Tag == document);
			if (page != null && document != null)
			{
				page.Text = document.Name;
				page.ImageKey = document.HasUnsavedChanges ? "Dirty" : "Clean";
			}

			if (DocumentTabs.SelectedTab?.Tag is IDocument sd)
			{
				Text = Title + @" - " + sd.Name;
			}

			LayoutTabStrip();
			return Task.CompletedTask;
		}

		/// <summary>
		/// A request to close a document has been made, prompt the user to save changes
		/// or cancel the request.
		/// </summary>
		private async Task DocumentRequestClose(DocumentCloseMessage msg)
		{
			var doc = msg.Document;
			if (doc.HasUnsavedChanges)
			{
				var r = MessageBox.Show(this, String.Format(SaveChangesToFile, doc.Name), UnsavedChanges, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
				if (r == DialogResult.Cancel)
				{
					msg.Cancel();
					return;
				}

				if (r == DialogResult.Yes)
				{
					if (!await SaveDocument(doc))
					{
						msg.Cancel();
					}
				}
			}
		}

		/// <summary>
		/// Save a document, prompting for a file name if needed
		/// </summary>
		private async Task<bool> SaveDocument(IDocument doc)
		{
			var filename = doc.FileName;
			object options = null;

			if (String.IsNullOrWhiteSpace(filename) || !Directory.Exists(Path.GetDirectoryName(filename)))
			{
				var result = _saveAs.Value.Prompt(doc, _documentRegister.Value.GetSupportedFileExtensions(doc));
				if (result == null) return false;
				filename = result.FileName;
				options = result.Options;
			}

			return await _documentRegister.Value.SaveDocument(doc, filename, "", options);
		}

		private Task OpenCommandBox(string obj)
		{
			var cb = new CommandBox();
			cb.Location = new Point(Location.X + (Size.Width - cb.Width) / 2, Location.Y + (Size.Height - cb.Height) / 4);
			cb.StartPosition = FormStartPosition.Manual;
			cb.Show(this);
			return Task.CompletedTask;
		}

		// Form events

		private void TabChanged(object sender, EventArgs e)
		{
			Common.Logging.Log.Debug(nameof(Shell), $"Tab changed to index {DocumentTabs.SelectedIndex} (restoring session: {_restoringSession})");
			if (_restoringSession) return;
			_documentRegister.Value.ActivateDocument(DocumentTabs.SelectedTab?.Tag as IDocument);
		}

		/// <summary>
		/// Menu keeps its natural width on the left; the tab area fills the rest of the row
		/// </summary>
		private void LayoutTopBar()
		{
			if (MenuStrip.Height > 0 && TopBar.Height != MenuStrip.Height) TopBar.Height = MenuStrip.Height;
			var left = MenuStrip.Right;
			TabHost.SetBounds(left, 0, Math.Max(0, TopBar.ClientSize.Width - left), TopBar.ClientSize.Height);
		}

		/// <summary>
		/// The tab control fills the row; the "+" button sits on top of its empty space, right after
		/// the last tab, and moves right as tabs are added. Only when the tabs run out of room does the
		/// "+" stop at the right edge and the tab control shrink to leave it space.
		/// </summary>
		private void LayoutTabStrip()
		{
			var count = DocumentTabs.TabPages.Count;
			const int buttonWidth = 30;
			const int gap = 2;
			var w = TabHost.ClientSize.Width;
			var h = TabHost.ClientSize.Height;

			var tabsRight = count == 0 ? 0 : DocumentTabs.TabsRight;
			var overflow = count > 0 && tabsRight + gap + buttonWidth > w;

			DocumentTabs.Visible = count > 0;
			DocumentTabs.SetBounds(0, 0, overflow ? Math.Max(0, w - buttonWidth - gap) : w, h);

			var x = count == 0 ? 2 : (overflow ? w - buttonWidth : tabsRight + gap);
			NewTabButton.SetBounds(x, 0, buttonWidth, h);
			NewTabButton.BringToFront();
			NewTabButton.Invalidate();
		}

		private void RequestNewDocument(object sender, EventArgs e)
		{
			Oy.Publish("Command:Run", new CommandMessage("File:New"));
		}

		private void RequestClose(object sender, int index)
		{
			if (DocumentTabs.TabPages[index].Tag is IDocument doc)
			{
				_documentRegister.Value.RequestCloseDocument(doc);
			}
		}

		// Settings
		private List<LoadedDocument> _openDocuments;
		[Setting("OpenLastFiles")] public bool _openLastFiles = true;

		string ISettingsContainer.Name => "Sledge.Shell";

		public IEnumerable<SettingKey> GetKeys()
		{
			yield return new SettingKey("Autosaving", "OpenLastFiles", typeof(bool));
		}

		public void LoadValues(ISettingsStore store)
		{
			_openDocuments = store.Get<LoadedDocument[]>("OpenDocuments")?.ToList();
			_openLastFiles = store.Get<bool>("OpenLastFiles");
			ValuesLoaded = true;
		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("OpenDocuments", _openDocuments?.ToArray());
			store.Set("OpenLastFiles", _openLastFiles);
		}


		private class LoadedDocument
		{
			public string Loader { get; set; }
			public string FileName { get; set; }
			public Dictionary<string, string> Metadata { get; set; }

			// ReSharper disable once UnusedMember.Local
			// Don't delete this, it's used for reflection
			public LoadedDocument()
			{
				Metadata = new Dictionary<string, string>();
			}

			public LoadedDocument(SerialisedObject pointer)
			{
				Metadata = new Dictionary<string, string>();
				Loader = pointer.Name;
				foreach (var pp in pointer.Properties) Metadata[pp.Key] = pp.Value;
				FileName = pointer.Get<string>("FileName");
			}

			public DocumentPointer ToPointer()
			{
				var so = new DocumentPointer(Loader);
				foreach (var m in Metadata) so.Set(m.Key, m.Value);
				so.FileName = FileName;
				return so;
			}
		}
	}
}
