using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Storage;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;
using Sledge.QuickForms;
using Sledge.Shell;

namespace Sledge.BspEditor.Editing.Components.StorageSidebar
{
    /// <summary>
    /// The Storage section of the sidebar, below Visgroups. Lists the storage groups of the active map.
    /// Clicking a group selects it (for rename or delete); its checkbox shows or hides it.
    /// </summary>
    [Export(typeof(ISidebarComponent))]
    [Export(typeof(IInitialiseHook))]
    [OrderHint("H")]
    public class StorageSidebarPanel : UserControl, ISidebarComponent, IInitialiseHook
    {
        private readonly WeakReference<MapDocument> _document = new WeakReference<MapDocument>(null);
        private bool _refreshing;

        // Clicking a row's text selects it; clicking its checkbox shows or hides the group. Tag holds the group ID.
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.List,
            CheckBoxes = true,
            MultiSelect = false,
            HideSelection = false
        };
        private readonly Label _hint = new Label { Dock = DockStyle.Top, Height = 40, Text = "Checked groups are shown in the map. Storage is never saved into the map file." };
        private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false };
        private readonly Button _new = new Button { Text = "New", AutoSize = true };
        private readonly Button _rename = new Button { Text = "Rename", AutoSize = true };
        private readonly Button _delete = new Button { Text = "Delete", AutoSize = true };

        public string Title { get; set; } = "Storage";
        public object Control => this;

        public StorageSidebarPanel()
        {
            Controls.Add(_list);
            Controls.Add(_hint);
            Controls.Add(_buttons);
            _buttons.Controls.AddRange(new Control[] { _new, _rename, _delete });

            _list.ItemCheck += ListItemCheck;
            _list.ItemActivate += (s, e) => RenameSelected();
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _new.Click += (s, e) => NewGroup();
            _rename.Click += (s, e) => RenameSelected();
            _delete.Click += (s, e) => DeleteSelected();

            CreateHandle();
            UpdateButtons();
        }

        public bool IsInContext(IContext context) => true;

        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated);
            Oy.Subscribe<Change>("MapDocument:Changed", DocumentChanged);
            return Task.FromResult(0);
        }

        private Task DocumentActivated(IDocument document)
        {
            _document.SetTarget(document as MapDocument);
            this.InvokeLater(() => Refresh());
            return Task.FromResult(0);
        }

        private Task DocumentChanged(Change change)
        {
            if (_document.TryGetTarget(out var md) && ReferenceEquals(md, change.Document))
            {
                this.InvokeLater(() => Refresh());
            }
            return Task.FromResult(0);
        }

        private MapDocument Current()
        {
            return _document.TryGetTarget(out var md) ? md : null;
        }

        private StorageGroup SelectedGroup()
        {
            var doc = Current();
            if (doc == null || _list.SelectedItems.Count == 0) return null;
            return StorageRules.GetGroup(doc, (long)_list.SelectedItems[0].Tag);
        }

        /// <summary>
        /// Updates the rows in place, so the selection survives changes made elsewhere (undo, moves, etc.).
        /// </summary>
        private void Refresh()
        {
            var doc = Current();
            var groups = doc == null
                ? new List<StorageGroup>()
                : StorageRules.Groups(doc).OrderBy(x => x.ID).ToList();
            var ids = new HashSet<long>(groups.Select(x => x.ID));

            _refreshing = true;
            try
            {
                for (var i = _list.Items.Count - 1; i >= 0; i--)
                {
                    if (!ids.Contains((long)_list.Items[i].Tag)) _list.Items.RemoveAt(i);
                }

                foreach (var g in groups)
                {
                    var item = _list.Items.Cast<ListViewItem>().FirstOrDefault(x => (long)x.Tag == g.ID);
                    if (item == null)
                    {
                        item = new ListViewItem { Tag = g.ID };
                        _list.Items.Add(item);
                    }
                    item.Text = g.Name + "  (" + g.Objects.Count + ")";
                    item.Checked = g.Visible;
                }
            }
            finally
            {
                _refreshing = false;
            }

            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var hasDocument = Current() != null;
            var hasSelection = _list.SelectedItems.Count > 0;
            _new.Enabled = hasDocument;
            _rename.Enabled = hasDocument && hasSelection;
            _delete.Enabled = hasDocument && hasSelection;
        }

        private async void ListItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_refreshing) return;

            var doc = Current();
            if (doc == null || e.Index < 0 || e.Index >= _list.Items.Count) return;

            var show = e.NewValue == CheckState.Checked;
            await MapDocumentOperation.Perform(doc, new SetStorageVisible((long)_list.Items[e.Index].Tag, show));
        }

        private async void NewGroup()
        {
            var doc = Current();
            if (doc == null) return;

            var suggested = "Storage " + (StorageRules.Groups(doc).Count() + 1);
            using (var qf = new QuickForm("New storage group").TextBox("Name", "Name", suggested).OkCancel("OK", "Cancel"))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return;

                var name = qf.Object("Name")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                var group = new StorageGroup { ID = doc.Map.NumberGenerator.Next("StorageGroup"), Name = name };
                await MapDocumentOperation.Perform(doc, new AddMapData(group));
            }
        }

        private async void RenameSelected()
        {
            var doc = Current();
            var group = SelectedGroup();
            if (doc == null || group == null) return;

            using (var qf = new QuickForm("Rename storage group").TextBox("Name", "Name", group.Name).OkCancel("OK", "Cancel"))
            {
                if (await qf.ShowDialogAsync() != DialogResult.OK) return;

                var name = qf.Object("Name")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name)) return;

                await MapDocumentOperation.Perform(doc, new RenameStorageGroup(group.ID, name));
            }
        }

        private async void DeleteSelected()
        {
            var doc = Current();
            var group = SelectedGroup();
            if (doc == null || group == null) return;

            if (group.Objects.Count > 0)
            {
                MessageBox.Show("Move the objects out of \"" + group.Name + "\" before deleting it.", "Storage", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show("Delete the storage group \"" + group.Name + "\"?", "Storage", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            await MapDocumentOperation.Perform(doc, new RemoveMapData(group));
        }
    }
}
