using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Shell.Documents;
using Sledge.Shell.Registers;

namespace Sledge.Shell.Forms
{
    /// <summary>
    /// The Save As / Export dialog. Folder + file name + format, plus a host area where
    /// exporters can show their own options (see <see cref="ISaveOptionsProvider"/>).
    /// </summary>
    internal class SaveAsDialog : Form
    {
        internal class FormatEntry
        {
            public string Description { get; }
            public string Extension { get; }
            public FormatEntry(string description, string extension)
            {
                Description = description;
                Extension = extension;
            }
            public override string ToString() => Description + " (*" + Extension + ")";
        }

        private readonly IDocument _document;
        private readonly IList<ISaveOptionsProvider> _providers;
        private readonly Dictionary<string, ISaveOptionsPanel> _panels = new Dictionary<string, ISaveOptionsPanel>(StringComparer.OrdinalIgnoreCase);

        private readonly TextBox _folder;
        private readonly TextBox _name;
        private readonly ComboBox _format;
        private readonly GroupBox _optionsBox;
        private readonly Panel _optionsHost;
        private ISaveOptionsPanel _currentPanel;
        private bool _updating;

        public string ResultFileName { get; private set; }
        public string ResultExtension { get; private set; }
        public object ResultOptions { get; private set; }

        public SaveAsDialog(
            IDocument document,
            string title,
            string actionText,
            IList<FormatEntry> formats,
            IList<ISaveOptionsProvider> providers,
            string initialFolder,
            string initialName,
            string initialExtension)
        {
            _document = document;
            _providers = providers;

            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // Folder
            layout.Controls.Add(MakeLabel("Save in:"), 0, 0);
            _folder = new TextBox { Dock = DockStyle.Fill, Text = initialFolder ?? "", Margin = new Padding(3, 3, 3, 6) };
            layout.Controls.Add(_folder, 1, 0);
            var browse = new Button { Text = "Browse...", AutoSize = true, MinimumSize = new Size(80, 24), Margin = new Padding(3, 2, 3, 6) };
            browse.Click += (s, e) => BrowseFolder();
            layout.Controls.Add(browse, 2, 0);

            // File name
            layout.Controls.Add(MakeLabel("File name:"), 0, 1);
            _name = new TextBox { Dock = DockStyle.Fill, Text = initialName ?? "", Margin = new Padding(3, 3, 3, 6) };
            layout.Controls.Add(_name, 1, 1);
            layout.SetColumnSpan(_name, 2);

            // Format
            layout.Controls.Add(MakeLabel("Format:"), 0, 2);
            _format = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
            foreach (var f in formats) _format.Items.Add(f);
            layout.Controls.Add(_format, 1, 2);
            layout.SetColumnSpan(_format, 2);

            // Per-format options
            _optionsHost = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
            _optionsBox = new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 4, 8, 8),
                Margin = new Padding(3, 6, 3, 6),
                Visible = false
            };
            _optionsBox.Controls.Add(_optionsHost);
            layout.Controls.Add(_optionsBox, 0, 3);
            layout.SetColumnSpan(_optionsBox, 3);

            // Buttons
            var ok = new Button { Text = actionText, AutoSize = true, MinimumSize = new Size(80, 26) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 26) };
            ok.Click += (s, e) => Accept();
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 0, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 3);

            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = cancel;

            // Pick the starting format (this also builds its options panel)
            var start = formats.FirstOrDefault(x => String.Equals(x.Extension, initialExtension, StringComparison.OrdinalIgnoreCase)) ?? formats.FirstOrDefault();
            if (start != null) _format.SelectedItem = start;
            ShowOptionsFor(_format.SelectedItem as FormatEntry);
            _format.SelectedIndexChanged += (s, e) =>
            {
                if (_updating) return;
                var entry = _format.SelectedItem as FormatEntry;
                ShowOptionsFor(entry);
                ChangeNameExtension(entry);
            };

            Shown += (s, e) =>
            {
                _name.Focus();
                _name.SelectAll();
            };
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 8, 6) };
        }

        private void BrowseFolder()
        {
            using (var fbd = new FolderBrowserDialog { SelectedPath = Directory.Exists(_folder.Text) ? _folder.Text : "" })
            {
                if (fbd.ShowDialog(this) == DialogResult.OK) _folder.Text = fbd.SelectedPath;
            }
        }

        /// <summary>
        /// If the typed name already ends in one of the known extensions, swap it for the newly chosen one.
        /// </summary>
        private void ChangeNameExtension(FormatEntry entry)
        {
            if (entry == null) return;
            var current = Path.GetExtension(_name.Text.Trim());
            if (String.IsNullOrEmpty(current)) return;
            var known = _format.Items.OfType<FormatEntry>().Any(x => String.Equals(x.Extension, current, StringComparison.OrdinalIgnoreCase));
            if (known) _name.Text = Path.GetFileNameWithoutExtension(_name.Text.Trim()) + entry.Extension;
        }

        private void ShowOptionsFor(FormatEntry entry)
        {
            // Detach whatever is showing (panels are kept so their state survives switching formats)
            foreach (Control c in _optionsHost.Controls.OfType<Control>().ToList()) _optionsHost.Controls.Remove(c);
            _currentPanel = null;
            _optionsBox.Visible = false;
            if (entry == null) return;

            if (!_panels.TryGetValue(entry.Extension, out var panel))
            {
                var provider = _providers.FirstOrDefault(x => x.Handles(_document, entry.Extension));
                panel = provider?.CreatePanel(_document);
                _panels[entry.Extension] = panel; // null is remembered too
                if (panel?.Control is Control created)
                {
                    created.Dock = DockStyle.Top;
                    try { DialogRegister.ColorControlsRecursively(created, false); } catch { /* theming is cosmetic */ }
                }
            }

            if (panel?.Control is Control control)
            {
                _optionsHost.Controls.Add(control);
                _optionsBox.Text = entry.Description + " options";
                _optionsBox.Visible = true;
                _currentPanel = panel;
            }
        }

        private void Accept()
        {
            var folder = _folder.Text.Trim();
            var name = _name.Text.Trim();

            if (name.Length == 0)
            {
                MessageBox.Show(this, "Enter a file name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "The file name contains characters that aren't allowed.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _name.Focus();
                return;
            }
            if (folder.Length == 0)
            {
                MessageBox.Show(this, "Choose a folder to save in.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _folder.Focus();
                return;
            }

            // A typed extension that matches another format switches to that format (and its options)
            var entry = _format.SelectedItem as FormatEntry;
            var typed = Path.GetExtension(name);
            var typedEntry = String.IsNullOrEmpty(typed) ? null : _format.Items.OfType<FormatEntry>().FirstOrDefault(x => String.Equals(x.Extension, typed, StringComparison.OrdinalIgnoreCase));
            if (typedEntry != null && typedEntry != entry)
            {
                _updating = true;
                _format.SelectedItem = typedEntry;
                _updating = false;
                ShowOptionsFor(typedEntry);
                entry = typedEntry;
            }
            if (entry == null) return;
            if (typedEntry == null) name += entry.Extension;

            string path;
            try
            {
                if (!Directory.Exists(folder))
                {
                    var create = MessageBox.Show(this, "The folder doesn't exist:\n" + folder + "\n\nCreate it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (create != DialogResult.Yes) return;
                    Directory.CreateDirectory(folder);
                }
                path = Path.Combine(folder, name);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Can't use that folder:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (File.Exists(path))
            {
                var overwrite = MessageBox.Show(this, Path.GetFileName(path) + " already exists.\nDo you want to replace it?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (overwrite != DialogResult.Yes) return;
            }

            ResultFileName = path;
            ResultExtension = entry.Extension;
            ResultOptions = _currentPanel?.GetOptions();
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var panel in _panels.Values.Where(x => x != null)) panel.Dispose();
                _panels.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
