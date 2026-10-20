using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Settings;
using Sledge.Shell.Forms;

namespace Sledge.Shell.Components
{
    /// <summary>
    /// The result of the Save As / Export dialog.
    /// </summary>
    public class SaveAsResult
    {
        /// <summary>Full path to save to.</summary>
        public string FileName { get; set; }

        /// <summary>The extension of the chosen format (e.g. ".obj").</summary>
        public string Extension { get; set; }

        /// <summary>Options from the format's panel, or null if the format has none.</summary>
        public object Options { get; set; }
    }

    /// <summary>
    /// Shows the Save As / Export dialog. Replaces the standard Windows SaveFileDialog.
    /// Remembers the last folder and format between sessions.
    /// </summary>
    [Export]
    [Export(typeof(ISettingsContainer))]
    public class SaveAsService : ISettingsContainer
    {
        private readonly IEnumerable<Lazy<ISaveOptionsProvider>> _optionProviders;
        private readonly Lazy<System.Windows.Forms.Form> _shell;

        private string _lastExtension;
        private string _lastFolder;

        string ISettingsContainer.Name => "Sledge.Shell.SaveAs";
        public bool ValuesLoaded { get; private set; }

        [ImportingConstructor]
        public SaveAsService(
            [ImportMany] IEnumerable<Lazy<ISaveOptionsProvider>> optionProviders,
            [Import("Shell")] Lazy<System.Windows.Forms.Form> shell
        )
        {
            _optionProviders = optionProviders;
            _shell = shell;
        }

        /// <summary>
        /// Ask the user where and how to save. Returns null if cancelled.
        /// </summary>
        /// <param name="document">The document being saved</param>
        /// <param name="formats">The formats to offer</param>
        /// <param name="title">Window title</param>
        /// <param name="actionText">Text of the confirm button</param>
        public SaveAsResult Prompt(IDocument document, IEnumerable<FileExtensionInfo> formats, string title = "Save As", string actionText = "Save")
        {
            var entries = new List<SaveAsDialog.FormatEntry>();
            foreach (var info in formats)
            {
                foreach (var ext in info.Extensions)
                {
                    if (entries.Any(x => String.Equals(x.Extension, ext, StringComparison.OrdinalIgnoreCase))) continue;
                    entries.Add(new SaveAsDialog.FormatEntry(info.Description, ext));
                }
            }
            if (entries.Count == 0) return null;

            var docFile = document.FileName;
            var docFolder = SafeDirectory(docFile);

            var folder = docFolder != null && Directory.Exists(docFolder) ? docFolder
                : !String.IsNullOrEmpty(_lastFolder) && Directory.Exists(_lastFolder) ? _lastFolder
                : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

            var name = !String.IsNullOrWhiteSpace(docFile) ? Path.GetFileNameWithoutExtension(docFile) : CleanName(document.Name);
            if (String.IsNullOrWhiteSpace(name)) name = "untitled";

            // Prefer the document's own format, then whatever was used last
            var docExt = String.IsNullOrWhiteSpace(docFile) ? null : Path.GetExtension(docFile);
            var extension = entries.Any(x => String.Equals(x.Extension, docExt, StringComparison.OrdinalIgnoreCase)) ? docExt : _lastExtension;

            var providers = _optionProviders.Select(x => x.Value).ToList();

            SaveAsResult result = null;
            _shell.Value.InvokeSync(() =>
            {
                using (var dialog = new SaveAsDialog(document, title, actionText, entries, providers, folder, name, extension))
                {
                    if (dialog.ShowDialog(_shell.Value) != DialogResult.OK) return;
                    result = new SaveAsResult
                    {
                        FileName = dialog.ResultFileName,
                        Extension = dialog.ResultExtension,
                        Options = dialog.ResultOptions
                    };
                }
            });

            if (result != null)
            {
                _lastExtension = result.Extension;
                _lastFolder = SafeDirectory(result.FileName) ?? _lastFolder;
            }
            return result;
        }

        private static string SafeDirectory(string path)
        {
            try { return String.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path); }
            catch { return null; }
        }

        private static string CleanName(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            return new string(name.Where(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0).ToArray()).Trim(' ', '*');
        }

        // Settings

        public IEnumerable<SettingKey> GetKeys()
        {
            yield break;
        }

        public void LoadValues(ISettingsStore store)
        {
            _lastExtension = store.Get<string>("extension");
            _lastFolder = store.Get<string>("folder");
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            store.Set("extension", _lastExtension);
            store.Set("folder", _lastFolder);
        }
    }
}
