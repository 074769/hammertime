using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.CompileErrors;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.BspEditor.Rendering.Overlay;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Shell;

namespace Sledge.BspEditor.Editing.Components.CompileErrors
{
    /// <summary>
    /// Window listing the errors and warnings from the compile log beside the open map.
    /// Hosted by the shell like the other dialogs (opened through the context), and kept separate from
    /// Check for Problems: those are editor problems, these are compiler output.
    /// </summary>
    [Export(typeof(IDialog))]
    public class CompileErrorsDialog : Form, IDialog
    {
        #pragma warning disable CS0649 // assigned by MEF
        [Import("Shell", typeof(Form))] private Lazy<Form> _parent;
        #pragma warning restore CS0649

        private MapDocument _document;
        private string _logPath;
        private List<CompileErrorsEntry> _entries = new List<CompileErrorsEntry>();
        private List<Subscription> _subscriptions;

        private readonly Label _header = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
        private readonly CheckedListBox _types = new CheckedListBox { Dock = DockStyle.Left, Width = 300, CheckOnClick = true, IntegralHeight = false };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        private readonly FlowLayoutPanel _buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(6) };
        private readonly Button _goTo = new Button { Text = "Go to", Width = 90 };
        private readonly Button _reload = new Button { Text = "Reload", Width = 90 };
        private readonly Button _close = new Button { Text = "Close", Width = 90 };

        public CompileErrorsDialog()
        {
            Text = "Compile Log";
            Size = new Size(1100, 580);
            MinimumSize = new Size(760, 320);

            _list.Columns.Add("Tool", 60);
            _list.Columns.Add("Sev", 60);
            _list.Columns.Add("Type", 240);
            _list.Columns.Add("Hull", 45);
            _list.Columns.Add("Entity", 55);
            _list.Columns.Add("Brush", 55);
            _list.Columns.Add("Location", 230);
            _list.Columns.Add("Message", 420);

            _list.DoubleClick += async (s, e) => await GoToSelected();
            _types.ItemCheck += TypesItemCheck;

            _goTo.Click += async (s, e) => await GoToSelected();
            _reload.Click += async (s, e) => await LoadAsync();
            _close.Click += (s, e) => Close();
            _buttons.Controls.AddRange(new Control[] { _goTo, _reload, _close });

            // Fill must be added first so the docked panels take their space around it
            Controls.Add(_list);
            Controls.Add(_types);
            Controls.Add(_buttons);
            Controls.Add(_header);

        }

        public bool IsInContext(IContext context)
        {
            return context.HasAny("BspEditor:CompileErrors");
        }

        public void SetVisible(IContext context, bool visible)
        {
            this.InvokeLater(() =>
            {
                if (visible)
                {
                    _document = context.Get<MapDocument>("ActiveDocument");
                    if (!Visible) Show(_parent.Value);
                    Subscribe();
                    _ = LoadAsync();
                }
                else
                {
                    Hide();
                    Unsubscribe();
                    CompileErrorsOverlay.Clear();
                }
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Closing only hides; the shell owns the window and its context decides when it shows again
            e.Cancel = true;
            Oy.Publish("Context:Remove", new ContextInfo("BspEditor:CompileErrors"));
        }

        private void Subscribe()
        {
            if (_subscriptions != null) return;
            _subscriptions = new List<Subscription>
            {
                Oy.Subscribe<MapDocument>("Document:Activated", DocumentActivated)
            };
        }

        private void Unsubscribe()
        {
            if (_subscriptions == null) return;
            _subscriptions.ForEach(x => x.Dispose());
            _subscriptions = null;
        }

        private async Task DocumentActivated(MapDocument document)
        {
            if (!Visible) return;
            _document = document;
            CompileErrorsOverlay.Clear();
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            var mapPath = _document?.FileName;
            _logPath = string.IsNullOrWhiteSpace(mapPath) ? null : Path.ChangeExtension(mapPath, ".log");

            if (_logPath != null && File.Exists(_logPath))
            {
                var path = _logPath;
                _entries = await Task.Run(() => CompileErrorsParser.Parse(path));
            }
            else
            {
                _entries = new List<CompileErrorsEntry>();
            }

            RebuildTypes();
            ApplyFilter();
        }

        private void RebuildTypes()
        {
            _types.BeginUpdate();
            _types.Items.Clear();

            var groups = _entries
                .GroupBy(x => x.FilterKey)
                .OrderBy(g => CategoryRank(g.First().Category))
                .ThenByDescending(g => g.Count());

            foreach (var g in groups)
            {
                var first = g.First();
                var item = new TypeItem { Key = g.Key, Text = first.FilterLabel + "  [" + g.Count() + "]" };
                _types.Items.Add(item, !CompileErrorsSettings.IsHidden(g.Key));
            }

            _types.EndUpdate();
        }

        private void ApplyFilter()
        {
            var shown = _entries.Where(x => !CompileErrorsSettings.IsHidden(x.FilterKey)).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var e in shown)
            {
                var item = new ListViewItem(new[]
                {
                    e.Tool,
                    e.Severity.ToString(),
                    e.TypeName,
                    e.Hull?.ToString() ?? "",
                    e.Entity?.ToString() ?? "",
                    e.Brush?.ToString() ?? "",
                    LocationText(e),
                    e.Text.Split('\n')[0],
                })
                { Tag = e };

                if (e.Severity == CompileErrorsSeverity.Error) item.ForeColor = Color.Firebrick;
                _list.Items.Add(item);
            }
            _list.EndUpdate();

            var hidden = _entries.Count - shown.Count;
            var state = _logPath == null ? "Map is not saved yet"
                : File.Exists(_logPath) ? _logPath
                : "No log file found: " + _logPath;
            _header.Text = state + "     " + shown.Count + " shown, " + hidden + " hidden";
        }

        private void TypesItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (!(_types.Items[e.Index] is TypeItem item)) return;

            CompileErrorsSettings.SetHidden(item.Key, e.NewValue != CheckState.Checked);

            // The checked state changes after this event fires, so filter on the next pass
            BeginInvoke((Action)(() =>
            {
                ApplyFilter();
                Oy.Publish("Settings:Save");
            }));
        }

        private async Task GoToSelected()
        {
            if (_document == null || _list.SelectedItems.Count == 0) return;
            if (!(_list.SelectedItems[0].Tag is CompileErrorsEntry entry)) return;

            var obj = CompileErrorsLookup.FindObject(_document, entry);
            if (obj != null)
            {
                var tran = new Transaction(new Deselect(_document.Selection), new Select(obj));
                await MapDocumentOperation.Perform(_document, tran);
            }

            CompileErrorsOverlay.Show(_document, entry.Box, entry.Points);

            var focus = CompileErrorsLookup.FocusBox(entry, obj);
            if (focus != null)
            {
                await Task.WhenAll(
                    Oy.Publish("MapDocument:Viewport:Focus3D", focus),
                    Oy.Publish("MapDocument:Viewport:Focus2D", focus));
            }
        }

        private static string LocationText(CompileErrorsEntry e)
        {
            if (e.Box != null) return Format(e.Box.Start) + " to " + Format(e.Box.End);
            if (e.Points.Count > 0)
            {
                return Format(e.Points[0]) + (e.Points.Count > 1 ? " (+" + (e.Points.Count - 1) + ")" : "");
            }
            return "";
        }

        private static string Format(Vector3 v)
        {
            return v.X.ToString("0") + " " + v.Y.ToString("0") + " " + v.Z.ToString("0");
        }

        private static int CategoryRank(string category)
        {
            switch (category)
            {
                case "Coordinates": return 0;
                case "Vis": return 1;
                case "Geometry": return 2;
                case "Texture": return 3;
                case "Limits": return 4;
                default: return 5;
            }
        }

        private class TypeItem
        {
            public string Key;
            public string Text;
            public override string ToString() => Text;
        }
    }
}
