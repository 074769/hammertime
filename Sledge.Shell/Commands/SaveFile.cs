using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.Shell.Components;
using Sledge.Shell.Properties;
using Sledge.Shell.Registers;

namespace Sledge.Shell.Commands
{
    [AutoTranslate]
    [Export(typeof(ICommand))]
    [CommandID("File:Save")]
    [DefaultHotkey("Ctrl+S")]
    [MenuItem("File", "", "File", "H")]
    [MenuImage(typeof(Resources), nameof(Resources.Menu_Save))]
    public class SaveFile : ICommand
    {
        private readonly Lazy<DocumentRegister> _documentRegister;
        private readonly Lazy<SaveAsService> _saveAs;

        public string Name { get; set; } = "Save";
        public string Details { get; set; } = "Save";

        [ImportingConstructor]
        public SaveFile(
            [Import] Lazy<DocumentRegister> documentRegister,
            [Import] Lazy<SaveAsService> saveAs
        )
        {
            _documentRegister = documentRegister;
            _saveAs = saveAs;
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveDocument", out IDocument _);
        }

        public async Task Invoke(IContext context, CommandParameters parameters)
        {
            var doc = context.Get<IDocument>("ActiveDocument");
            if (doc != null)
            {
                var filename = doc.FileName;
                object options = null;

                if (filename == null || !Directory.Exists(Path.GetDirectoryName(filename)))
                {
                    var result = _saveAs.Value.Prompt(doc, _documentRegister.Value.GetSupportedFileExtensions(doc));
                    if (result == null) return;
                    filename = result.FileName;
                    options = result.Options;
                }

                await _documentRegister.Value.SaveDocument(doc, filename, "", options);
            }
        }
    }
}