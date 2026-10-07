using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Translations;
using Sledge.Providers.Texture;
using Sledge.Shell;

namespace Sledge.BspEditor.Tools.Texture
{
    [AutoTranslate]
    [Export(typeof(ISidebarComponent))]
    [Export(typeof(IInitialiseHook))]
    [Export(typeof(ISettingsContainer))]
    [OrderHint("B")]
    public partial class TextureSidebarPanel : UserControl, ISidebarComponent, IInitialiseHook, ISettingsContainer
    {
        // How many recently used textures are remembered (the strip shows two at a time, the rest scroll)
        private const int HistoryLimit = 16;
        public Task OnInitialise()
        {
            Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated);
            Oy.Subscribe<Change>("MapDocument:Changed", DocumentChanged);
            return Task.FromResult(0);
        }

        public string Title { get; set; } = "Texture";
        public object Control => this;

        private string _currentTexture;
        private WeakReference<MapDocument> _activeDocument;

        // Most recently added first. Guarded by _historyLock because documents change on background threads.
        private readonly object _historyLock = new object();
        private readonly List<string> _history = new List<string>();
        private readonly HashSet<string> _thumbnailRequested = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        private bool _layouting;

        // The Apply button is gone (Shift+T still applies); kept so existing translation files still bind
        public string Apply
        {
            set { }
        }

        public string Browse
        {
            set => this.InvokeLater(() => BrowseButton.Text = value);
        }

        public string Replace
        {
            set => this.InvokeLater(() => ReplaceButton.Text = value);
        }

        public TextureSidebarPanel()
        {
            CreateHandle();
            InitializeComponent();

            SizeLabel.Text = "";
            NameLabel.Text = "";
            _activeDocument = new WeakReference<MapDocument>(null);

            LayoutControls();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutControls();
        }

        /// <summary>
        /// Lay everything out proportionally: the two history cells and both button columns always
        /// split the available width in half, and the panel's height follows the cell size.
        /// </summary>
        private void LayoutControls()
        {
            if (_layouting || HistoryStrip == null) return;
            _layouting = true;
            try
            {
                var m = LogicalToDeviceUnits(3);
                var buttonH = Math.Max(LogicalToDeviceUnits(22), Font.Height + LogicalToDeviceUnits(8));
                var labelH = Math.Max(LogicalToDeviceUnits(16), Font.Height + 2);
                var maxCellH = LogicalToDeviceUnits(128);

                var inner = Math.Max(1, ClientSize.Width - 2 * m);
                var cellW = TextureHistoryStrip.CellWidthFor(inner);
                var cellH = Math.Max(LogicalToDeviceUnits(48), Math.Min(cellW, maxCellH));
                var stripH = cellH + (HistoryStrip.NeedsScrollBar ? HistoryStrip.ScrollBarHeight : 0);

                SuspendLayout();

                var y = m;
                HistoryStrip.SetBounds(m, y, inner, stripH);
                y += stripH + m;

                NameLabel.SetBounds(m, y, inner, labelH);
                y += labelH;
                SizeLabel.SetBounds(m, y, inner, labelH);
                y += labelH + m;

                var colW = (inner - m) / 2;
                var rightX = m + colW + m;
                var rightW = inner - colW - m;

                BrowseButton.SetBounds(m, y, colW, buttonH);
                ReplaceButton.SetBounds(rightX, y, rightW, buttonH);
                y += buttonH + m;

                if (Height != y) Height = y;

                ResumeLayout(false);
            }
            finally
            {
                _layouting = false;
            }
        }

        // Settings: remember the texture history between sessions

        string ISettingsContainer.Name => "Sledge.BspEditor.Tools.TextureHistory";

        public bool ValuesLoaded { get; private set; }

        IEnumerable<SettingKey> ISettingsContainer.GetKeys()
        {
            yield break;
        }

        void ISettingsContainer.LoadValues(ISettingsStore store)
        {
            var saved = store.Get("History", "") ?? "";

            lock (_historyLock)
            {
                // Anything picked before the settings arrived stays in front
                foreach (var name in saved.Split('|'))
                {
                    if (String.IsNullOrWhiteSpace(name)) continue;
                    if (_history.Exists(x => String.Equals(x, name, StringComparison.InvariantCultureIgnoreCase))) continue;
                    if (_history.Count < HistoryLimit) _history.Add(name);
                }
            }

            ValuesLoaded = true;
            this.InvokeLater(RefreshHistoryStrip);
        }

        void ISettingsContainer.StoreValues(ISettingsStore store)
        {
            string[] snapshot;
            lock (_historyLock) snapshot = _history.ToArray();
            store.Set("History", String.Join("|", snapshot));
        }

        private void BrowseButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("Command:Run", new CommandMessage("BspEditor:BrowseActiveTexture"));
        }

        private void ReplaceButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("Command:Run", new CommandMessage("BspEditor:ReplaceTextures"));
        }

        private async Task DocumentActivated(IDocument doc)
        {
            var md = doc as MapDocument;

            _activeDocument = new WeakReference<MapDocument>(md);
            _currentTexture = null;

            lock (_historyLock) _thumbnailRequested.Clear();

            await this.InvokeAsync(() =>
            {
                // The new document may use different WADs, so thumbnails are rebuilt for it
                HistoryStrip.ClearThumbnails();
                HistoryStrip.SelectedName = null;
            });

            if (md != null)
            {
                await TextureSelected(md.Map.Data.GetOne<ActiveTexture>()?.Name);
                await LoadMissingThumbnails(md);
            }
        }

        private void HistoryTextureClicked(object sender, string name)
        {
            if (!_activeDocument.TryGetTarget(out MapDocument md) || md == null) return;

            var at = new ActiveTexture { Name = name };
            MapDocumentOperation.Perform(md, new TrivialOperation(x => x.Map.Data.Replace(at), x => x.Update(at)));
        }

        /// <summary>
        /// Put a texture at the front of the history, unless it is already in there (then it stays where it is).
        /// Must be called on the UI thread.
        /// </summary>
        private void AddToHistory(string name)
        {
            bool added;
            lock (_historyLock)
            {
                var idx = _history.FindIndex(x => String.Equals(x, name, StringComparison.InvariantCultureIgnoreCase));
                added = idx < 0;
                if (added)
                {
                    _history.Insert(0, name);
                    if (_history.Count > HistoryLimit) _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);
                }
            }

            RefreshHistoryStrip(added);
            HistoryStrip.SelectedName = name;
        }

        private void RefreshHistoryStrip()
        {
            RefreshHistoryStrip(false);
        }

        private void RefreshHistoryStrip(bool scrollToStart)
        {
            string[] snapshot;
            lock (_historyLock)
            {
                snapshot = _history.ToArray();
                _thumbnailRequested.IntersectWith(snapshot);
            }

            HistoryStrip.SetNames(snapshot, scrollToStart);
            LayoutControls();

            if (_activeDocument.TryGetTarget(out MapDocument md) && md != null)
            {
                var _ = LoadMissingThumbnails(md);
            }
        }

        private async Task LoadMissingThumbnails(MapDocument doc)
        {
            List<string> todo;
            lock (_historyLock) todo = _history.Where(x => _thumbnailRequested.Add(x)).ToList();
            if (todo.Count == 0) return;

            try
            {
                var tc = await doc.Environment.GetTextureCollection();
                using (var ss = tc.GetStreamSource())
                {
                    foreach (var name in todo)
                    {
                        Bitmap bmp = null;
                        try
                        {
                            if (await tc.GetTextureItem(name) != null)
                            {
                                bmp = (await ss.GetImage(name, 256, 256))?.FirstOrDefault();
                            }
                        }
                        catch
                        {
                            // A texture that can't be loaded just shows its name
                        }

                        if (bmp == null) continue;

                        var thumbnail = bmp;
                        this.InvokeLater(() => HistoryStrip.SetThumbnail(name, thumbnail));
                    }
                }
            }
            catch
            {
                // No texture collection available right now, the cells show names instead
            }
        }

        private async Task DocumentChanged(Change change)
        {
            if (_activeDocument.TryGetTarget(out MapDocument t) && change.Document == t)
            {
                await TextureSelected(t.Map.Data.GetOne<ActiveTexture>()?.Name);
            }
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveDocument", out MapDocument _);
        }

        private async Task TextureSelected(string selection)
        {
            if (selection == _currentTexture) return;
            _currentTexture = selection;

            if (!_activeDocument.TryGetTarget(out MapDocument doc)) return;

            Bitmap bmp = null;
            TextureItem texItem = null;

            if (selection != null)
            {
                var tc = await doc.Environment.GetTextureCollection();
                texItem = await tc.GetTextureItem(selection);

                if (texItem != null)
                {
                    using (var ss = tc.GetStreamSource())
                    {
                        bmp = (await ss.GetImage(selection, 256, 256)).First();
                    }
                }
            }

            if (bmp != null)
            {
                lock (_historyLock) _thumbnailRequested.Add(selection);
            }

            this.InvokeLater(() =>
            {
                if (texItem != null)
                {
                    AddToHistory(selection);
                    if (bmp != null) HistoryStrip.SetThumbnail(selection, bmp);
                }
                else
                {
                    HistoryStrip.SelectedName = null;
                }

                NameLabel.Text = texItem?.Name ?? "";
                SizeLabel.Text = texItem == null ? "" : $"{texItem.Width} x {texItem.Height}";
            });
        }
    }
}
