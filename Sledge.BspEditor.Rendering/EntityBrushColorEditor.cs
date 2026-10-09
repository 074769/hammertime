using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Sledge.Common.Translations;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// Custom setting editor for the <see cref="EntityBrushColorSettings.CustomEntityColours"/> setting.
    /// Displays Add/Import/Export buttons and one editable row per override.
    /// </summary>
    public partial class EntityBrushColorEditor : UserControl, ISettingEditor
    {
        private readonly TranslationStringsCatalog _catalog;
        private readonly ITranslationStringProvider _strings;
        private List<EntityColourOverride> _overrides = new List<EntityColourOverride>();

        public event EventHandler<SettingKey> OnValueChanged;

        // The settings form sets this to the localized setting name.
        public string Label
        {
            get => _titleLabel.Text;
            set => _titleLabel.Text = value;
        }

        public object Value
        {
            get => _overrides;
            set
            {
                _overrides = value as List<EntityColourOverride> ?? new List<EntityColourOverride>();
                RefreshRows();
            }
        }

        public object Control => this;
        public SettingKey Key { get; set; }

        public EntityBrushColorEditor(TranslationStringsCatalog catalog, ITranslationStringProvider strings)
        {
            _catalog = catalog;
            _strings = strings;
            // Ensure this assembly's translation strings are loaded before they are looked up.
            _catalog.Load(typeof(EntityBrushColorEditor));
            InitializeComponent();
            WireUpButtons();
            TranslateButtons();
        }

        private void WireUpButtons()
        {
            _btnAdd.Click += OnAddClick;
            _btnImport.Click += OnImportClick;
            _btnExport.Click += OnExportClick;
        }

        private string GetString(string key, string fallback)
        {
            return _strings.GetString(GetType().FullName, key) ?? fallback;
        }

        private void TranslateButtons()
        {
            _btnAdd.Text = GetString("Add", "Add");
            _btnImport.Text = GetString("Import", "Import");
            _btnExport.Text = GetString("Export", "Export");
        }

        private void OnAddClick(object sender, EventArgs e)
        {
            var row = new EntityColourRow
            {
                EntityName = "",
                Colour = Color.Magenta
            };
            row.NameChanged += Row_NameChanged;
            row.ColourChanged += Row_ColourChanged;
            row.RemoveRequested += Row_RemoveRequested;
            _rowPanel.Controls.Add(row);
            row.Focus();
            NotifyChanged();
        }

        private void Row_NameChanged(object sender, EventArgs e)
        {
            NotifyChanged();
        }

        private void Row_ColourChanged(object sender, EventArgs e)
        {
            NotifyChanged();
        }

        private void Row_RemoveRequested(object sender, EventArgs e)
        {
            var row = sender as EntityColourRow;
            _rowPanel.Controls.Remove(row);
            row.Dispose();
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            OnValueChanged?.Invoke(this, Key);
        }

        private void RefreshRows()
        {
            _rowPanel.Controls.Clear();
            foreach (var item in _overrides)
            {
                var row = new EntityColourRow
                {
                    EntityName = item.EntityName,
                    Colour = item.Colour
                };
                row.NameChanged += Row_NameChanged;
                row.ColourChanged += Row_ColourChanged;
                row.RemoveRequested += Row_RemoveRequested;
                _rowPanel.Controls.Add(row);
            }
        }

        private void OnImportClick(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "JSON files|*.json",
                Title = GetString("Import", "Import") + " overrides",
                DefaultExt = "json"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var text = System.IO.File.ReadAllText(ofd.FileName);
                    var imported = Newtonsoft.Json.JsonConvert.DeserializeObject<List<EntityColourOverride>>(text);
                    if (imported != null)
                    {
                        foreach (var item in imported.Where(x => x != null))
                        {
                            if (!_overrides.Any(o => o.EntityName == item.EntityName)) _overrides.Add(item);
                        }
                        RefreshRows();
                        NotifyChanged();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        string.Format(GetString("ImportFailed", "Could not import file: {0}"), ex.Message),
                        GetString("Import", "Import") + " failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportClick(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog
            {
                Filter = "JSON files|*.json",
                Title = GetString("Export", "Export") + " overrides",
                DefaultExt = "json"
            })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var text = Newtonsoft.Json.JsonConvert.SerializeObject(_overrides, Newtonsoft.Json.Formatting.Indented);
                    System.IO.File.WriteAllText(sfd.FileName, text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        string.Format(GetString("ExportFailed", "Could not export file: {0}"), ex.Message),
                        GetString("Export", "Export") + " failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }
}
