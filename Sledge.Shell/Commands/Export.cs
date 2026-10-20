using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.Shell.Components;
using Sledge.Shell.Properties;
using Sledge.Shell.Registers;

namespace Sledge.Shell.Commands
{
	[AutoTranslate]
	[Export(typeof(ICommand))]
	[CommandID("File:Export")]
	[MenuItem("File", "", "File", "L")]
	[MenuImage(typeof(Resources), nameof(Resources.Menu_Export))]
	public class Export : ICommand
	{
		private readonly IEnumerable<Lazy<IDocumentLoader>> _loaders;
		private readonly Lazy<DocumentRegister> _documentRegister;
		private readonly Lazy<SaveAsService> _saveAs;

		public string Name { get; set; } = "Export...";
		public string Details { get; set; } = "Export...";

		[ImportingConstructor]
		public Export(
			[ImportMany] IEnumerable<Lazy<IDocumentLoader>> loaders,
			[Import] Lazy<DocumentRegister> documentRegister,
			[Import] Lazy<SaveAsService> saveAs
		)
		{
			_loaders = loaders;
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
			if (doc == null) return;

			var formats = _loaders.Select(x => x.Value)
				.Where(x => x.CanSave(doc))
				.SelectMany(x => x.SupportedFileExtensions)
				.ToList();

			var result = _saveAs.Value.Prompt(doc, formats, "Export", "Export");
			if (result == null) return;

			// Export: writes the file without switching the document over to it
			await _documentRegister.Value.ExportDocument(doc, result.FileName, "", result.Options);
		}
	}
}
