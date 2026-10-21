using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell.Documents;
using Newtonsoft.Json;
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

        private class Remembered
        {
            public string Folder { get; set; }
            public string Extension { get; set; }
        }

        // The last folder/format per context ("" = Save As / Save, "obj" = Export as OBJ, "export" = File > Export...)
        private readonly Dictionary<string, Remembered> _remembered = new Dictionary<string, Remembered>(StringComparer.OrdinalIgnoreCase);

        private Remembered Recall(string contextKey)
        {
            contextKey = contextKey ?? "";
            if (!_remembered.TryGetValue(contextKey, out var r)) _remembered[contextKey] = r = new Remembered();
            return r;
        }

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
        /// <param name="contextKey">
        /// Null for the normal Save / Save As (starts in the document's own folder).
        /// Any other value gives that kind of export its own remembered folder and format,
        /// which is tried before the document's folder.
        /// </param>
        public SaveAsResult Prompt(IDocument document, IEnumerable<FileExtensionInfo> formats, string title = "Save As", string actionText = "Save", string contextKey = null)
        {
            var memory = Recall(contextKey);

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

            var docFolderOk = docFolder != null && Directory.Exists(docFolder);
            var memoryOk = !String.IsNullOrEmpty(memory.Folder) && Directory.Exists(memory.Folder);
            var folder = contextKey == null
                ? (docFolderOk ? docFolder : memoryOk ? memory.Folder : null)
                : (memoryOk ? memory.Folder : docFolderOk ? docFolder : null);
            if (folder == null) folder = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);

            var name = !String.IsNullOrWhiteSpace(docFile) ? Path.GetFileNameWithoutExtension(docFile) : CleanName(document.Name);
            if (String.IsNullOrWhiteSpace(name)) name = "untitled";

            // Prefer the document's own format, then whatever was used last
            var docExt = String.IsNullOrWhiteSpace(docFile) ? null : Path.GetExtension(docFile);
            var extension = entries.Any(x => String.Equals(x.Extension, docExt, StringComparison.OrdinalIgnoreCase)) ? docExt : memory.Extension;

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
                memory.Extension = result.Extension;
                memory.Folder = SafeDirectory(result.FileName) ?? memory.Folder;
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
            var main = Recall(null);
            main.Extension = store.Get<string>("extension");
            main.Folder = store.Get<string>("folder");

            try
            {
                var json = store.Get<string>("contexts");
                var others = String.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<Dictionary<string, Remembered>>(json);
                if (others != null) foreach (var kv in others) _remembered[kv.Key] = kv.Value ?? new Remembered();
            }
            catch
            {
                // Unreadable saved paths just fall back to defaults
            }
            ValuesLoaded = true;
        }

        public void StoreValues(ISettingsStore store)
        {
            var main = Recall(null);
            store.Set("extension", main.Extension);
            store.Set("folder", main.Folder);

            var others = _remembered.Where(x => x.Key != "").ToDictionary(x => x.Key, x => x.Value);
            store.Set("contexts", JsonConvert.SerializeObject(others));
        }
    }
}
