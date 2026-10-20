using System;
using System.Threading.Tasks;

namespace Sledge.Common.Shell.Documents
{
    /// <summary>
    /// Lets an exporter add its own options to the Save As / Export dialog.
    /// Export one of these (MEF) per format that needs options; the dialog shows the
    /// panel whenever the user picks a file extension the provider handles.
    /// </summary>
    public interface ISaveOptionsProvider
    {
        /// <summary>
        /// True if this provider has options for the given extension (e.g. ".obj") on the given document.
        /// </summary>
        bool Handles(IDocument document, string extension);

        /// <summary>
        /// Create the options panel for the document. Called once per dialog.
        /// </summary>
        ISaveOptionsPanel CreatePanel(IDocument document);
    }

    /// <summary>
    /// The options UI for one format.
    /// </summary>
    public interface ISaveOptionsPanel : IDisposable
    {
        /// <summary>
        /// The control to host in the dialog. Must be a System.Windows.Forms.Control
        /// (typed as object because Sledge.Common has no WinForms reference).
        /// </summary>
        object Control { get; }

        /// <summary>
        /// Read the options the user picked. Passed to <see cref="IDocumentOptionsSaver.Save"/>.
        /// </summary>
        object GetOptions();
    }

    /// <summary>
    /// Optional interface for document loaders whose Save can take the options chosen in the dialog.
    /// </summary>
    public interface IDocumentOptionsSaver
    {
        Task Save(IDocument document, string location, object options);
    }
}
