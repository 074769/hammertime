using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations;
using Sledge.BspEditor.Modification.Operations.Data;
using Sledge.BspEditor.Primitives;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.Common.Translations;
using Sledge.DataStructures.Geometric;
using Sledge.Shell;
using Sledge.Shell.Forms;

namespace Sledge.BspEditor.Tools.Texture
{
    /// <summary>
    /// This dialog is linked directly to the texture tool's context and provides useful operations for editing textures.
    /// </summary>
    [Export(typeof(IDialog))]
    [AutoTranslate]
    public partial class TextureApplicationForm : BaseForm, IDialog, IManualTranslate
    {
        // We want to use the shell for the parent
        [Import("Shell", typeof(Form))] private Form _shell;

        private readonly List<string> _recentTextures = new List<string>();

        // Event stopper to make sure we don't fire change events recursively 
        private bool _freeze;

        // The current values of the texture property controls
        private readonly CurrentTextureProperties _currentTextureProperties;

        private WeakReference<MapDocument> _document = new WeakReference<MapDocument>(null);

        private event EventHandler DebouncedPropertiesChanged;
        // 1 while a property change is waiting for its debounced (undoable) commit
        private int _pendingCommit;
        // Face replacement (remove old Face + add clone with the same ID) must never overlap, otherwise the second
        // one can't find the face the first already replaced and both clones end up in the solid (duplicate face IDs)
        private readonly System.Threading.SemaphoreSlim _applyLock = new System.Threading.SemaphoreSlim(1, 1);
        private IDisposable _saveChanges;

        // One combined viewer: textures on the selected faces first, then the recently used ones
        private TextureListPanel TexturesList;
        private List<string> _selectedTextures = new List<string>();

        public MapDocument Document
        {
            get
            {
                MapDocument d;
                return _document.TryGetTarget(out d) ? d : null;
            }
            set
            {
                _document = new WeakReference<MapDocument>(value);
                var precision = 4; // todo post-beta: environment-specific texture values precision
                                   // _document != null && _document.Game != null && _document.Game.Engine == Engine.Goldsource ? 2 : 4;
                ScaleXValue.DecimalPlaces = ScaleYValue.DecimalPlaces = precision;
            }
        }

        public TextureApplicationForm()
        {
            _freeze = true;

            InitializeComponent();
            InitialiseTextureLists();

            TexturesList.HighlightedTexturesChanged += TextureListHighlightedTexturesChanged;

            _freeze = false;

            _currentTextureProperties = new CurrentTextureProperties();

            Oy.Subscribe<IDocument>("Document:Activated", SetDocument);
            Oy.Subscribe<Change>("MapDocument:Changed", DocumentChanged);
            Oy.Subscribe<object>("TextureTool:SelectionChanged", async x => await FaceSelectionChanged());

            // Throttle property changes so they only apply after 500 ms, this should keep the undo stack clear
            _saveChanges = Observable.FromEventPattern(x => DebouncedPropertiesChanged += x, x => DebouncedPropertiesChanged -= x)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .Subscribe(x => CommitPendingChanges());
        }

        public string LeftClick { get; set; }
        public string RightClick { get; set; }

        public string ActionLift { get; set; }
        public string ActionSelect { get; set; }

        public string ActionApply { get; set; }
        public string ActionValues { get; set; }
        public string ActionAxis { get; set; }
        public string ActionAlignToView { get; set; }

		public void Translate(ITranslationStringProvider strings)
		{
			if (Handle == null) CreateHandle();
			var prefix = GetType().FullName;
			this.InvokeLater(() =>
			{
				Text = strings.GetString(prefix, "Title");

                ScaleLabel.Text = strings.GetString(prefix, "Scale");
                ShiftLabel.Text = strings.GetString(prefix, "Shift");

                BrowseButton.Text = strings.GetString(prefix, "Browse");
                ReplaceButton.Text = strings.GetString(prefix, "Replace");
                ApplyButton.Text = strings.GetString(prefix, "Apply");

                RotationLabel.Text = strings.GetString(prefix, "Rotation");
                LightmapLabel.Text = strings.GetString(prefix, "Lightmap");
                SmoothingGroupsButton.Text = strings.GetString(prefix, "SmoothingGroups");

                AlignGroup.Text = strings.GetString(prefix, "Align");
                AlignToWorldCheckbox.Text = strings.GetString(prefix, "World");
                AlignToFaceCheckbox.Text = strings.GetString(prefix, "Face");

                JustifyGroup.Text = strings.GetString(prefix, "Justify");
                JustifyFitButton.Text = strings.GetString(prefix, "Fit");
                TreatAsOneCheckbox.Text = strings.GetString(prefix, "TreatAsOne");

                HideMaskCheckbox.Text = strings.GetString(prefix, "HideMask");

                LeftClick = strings.GetString(prefix, "LeftClick");
                RightClick = strings.GetString(prefix, "RightClick");
                ActionLift = strings.GetString(prefix, "ActionLift");
                ActionSelect = strings.GetString(prefix, "ActionSelect");
                ActionApply = strings.GetString(prefix, "ActionApply");
                ActionValues = strings.GetString(prefix, "ActionValues");
                ActionAxis = strings.GetString(prefix, "ActionAxis");
                ActionAlignToView = strings.GetString(prefix, "ActionAlignToView");

                LeftClickActionButton.Text = $@"{LeftClick}: {ActionLift}+{ActionSelect}";
                LeftClickActionMenu.Items.Clear();
                LeftClickActionMenu.Items.Add(new ToolStripMenuItem($"{ActionLift}+{ActionSelect}") { Tag = ClickAction.Lift | ClickAction.Select });
                LeftClickActionMenu.Items.Add(new ToolStripMenuItem(ActionLift) { Tag = ClickAction.Lift });
                LeftClickActionMenu.Items.Add(new ToolStripMenuItem(ActionSelect) { Tag = ClickAction.Select });

                RightClickActionButton.Text = $@"{RightClick}: {ActionApply}+{ActionValues}";
                RightClickActionMenu.Items.Clear();
                RightClickActionMenu.Items.Add(new ToolStripMenuItem(ActionApply) { Tag = ClickAction.Apply });
                RightClickActionMenu.Items.Add(new ToolStripMenuItem($"{ActionApply}+{ActionValues}") { Tag = ClickAction.Apply | ClickAction.Values });
                RightClickActionMenu.Items.Add(new ToolStripMenuItem($"{ActionApply}+{ActionValues}+{ActionAxis}") { Tag = ClickAction.Apply | ClickAction.AlignToSample });
                RightClickActionMenu.Items.Add(new ToolStripMenuItem(ActionAlignToView) { Tag = ClickAction.AlignToView });
            });
        }

        private void SetLeftClickAction(object sender, ToolStripItemClickedEventArgs e)
        {
            if (!(e.ClickedItem.Tag is ClickAction)) return;

            var action = (ClickAction)e.ClickedItem.Tag;
            LeftClickActionButton.Text = $@"{LeftClick}: {e.ClickedItem.Text}";
            Oy.Publish("BspEditor:TextureTool:SetLeftClickAction", action);
        }

        private void SetRightClickAction(object sender, ToolStripItemClickedEventArgs e)
        {
            if (!(e.ClickedItem.Tag is ClickAction)) return;

            var action = (ClickAction)e.ClickedItem.Tag;
            RightClickActionButton.Text = $@"{RightClick}: {e.ClickedItem.Text}";
            Oy.Publish("BspEditor:TextureTool:SetRightClickAction", action);
        }

        private void InitialiseTextureLists()
        {
            TexturesList = new TextureListPanel
            {
                AllowMultipleHighlighting = false,
                AllowHighlighting = true,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Black,
                EnableDrag = false,
                ImageSize = 128
            };

            TexturesList.TextureSelected += TexturesListTextureSelected;

            TextureViewerPanel.Controls.Add(TexturesList);
        }

        private async Task SetDocument(IDocument doc)
        {
            var md = doc as MapDocument;
            Document = md;
            if (md != null)
            {
                var tc = await md.Environment.GetTextureCollection();
                TexturesList.Collection = tc;
                this.Invoke(() =>
                {
                    this.lightmapGrp.Visible = md.Capabilities.Contains(TextureTool.TextureToolLightmapCapable);
                });
            }
            else
            {
                TexturesList.Collection = null;
            }
        }

        private async Task DocumentChanged(Change change)
        {
            if (_document.TryGetTarget(out MapDocument t) && change.Document == t)
            {
                if (change.HasDataChanges && change.AffectedData.Any(x => x is ActiveTexture))
                {
                    var at = t.Map.Data.GetOne<ActiveTexture>()?.Name;
                    ActiveTextureChanged(at);
                }
                else if (change.HasObjectChanges && change.Updated.Intersect(GetFaceSelection().GetSelectedParents()).Any())
                {
                    await FaceSelectionChanged();
                }
            }
        }

        public bool IsInContext(IContext context)
        {
            return context.TryGet("ActiveTool", out TextureTool _);
        }

        public void SetVisible(IContext context, bool visible)
        {
            if (Visible != visible)
            {
                if (visible)
                {
                    Show(_shell);
                    _shell?.Focus();
                }
                else
                {
                    Hide();
                }
            }
        }

        private string GetFirstSelectedTexture()
        {
            return TexturesList
                .GetHighlightedTextures()
                .FirstOrDefault();
        }

        private IEnumerable<string> GetSelectedTextures()
        {
            return TexturesList.GetHighlightedTextures();
        }

        public FaceSelection GetFaceSelection()
        {
            var fs = Document.Map.Data.GetOne<FaceSelection>();
            if (fs == null)
            {
                fs = new FaceSelection();
                Document.Map.Data.Add(fs);
            }
            return fs;
        }

        private async void TextureListHighlightedTexturesChanged(object sender, IEnumerable<string> sel)
        {
            if (_freeze) return;

            _freeze = true;

            var selection = sel.ToList();
            var item = selection.FirstOrDefault();

            if (!selection.Any())
            {
                item = TexturesList
                    .GetHighlightedTextures()
                    .FirstOrDefault();
            }

            var label = "";
            var d = Document;
            if (item != null && d != null)
            {
                var tex = await d.Environment.GetTextureCollection();
                var ti = await tex.GetTextureItem(item);
                if (ti != null)
                {
                    label = $"{ti.WadName}/{ti.Name} ({ti.Width} x {ti.Height})";
                }

                var at = new ActiveTexture { Name = item };
                await MapDocumentOperation.Perform(Document, new TrivialOperation(x => x.Map.Data.Replace(at), x => x.Update(at)));
            }

            TextureDetailsLabel.InvokeLater(() =>
            {
                TextureDetailsLabel.Text = label;
            });

            _freeze = false;
        }

        private void UpdateTextureList()
        {
            var selected = _selectedTextures.ToList();
            var recent = _recentTextures.Where(r => !selected.Any(x => String.Equals(x, r, StringComparison.InvariantCultureIgnoreCase)));
            TexturesList.SetTextureList(selected.Concat(recent).ToList());
        }

        private void ActiveTextureChanged(string item)
        {
            if (_freeze) return;

            if (item == null)
            {
                TexturesList.SetHighlightedTextures(new string[0]);
                return;
            }

            _recentTextures.Remove(item);
            _recentTextures.Insert(0, item);
            if (_recentTextures.Count > 10) _recentTextures.RemoveRange(10, _recentTextures.Count - 10);
            UpdateTextureList();

            // Highlight the active texture in the combined list
            var match = TexturesList.GetTextureList()
                .FirstOrDefault(x => String.Equals(x, item, StringComparison.InvariantCultureIgnoreCase)) ?? item;
            TexturesList.SetHighlightedTextures(new[] { match });
            TexturesList.ScrollToTexture(match);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            Focus();
            base.OnMouseEnter(e);
        }

        private async Task FaceSelectionChanged()
        {
            // The list of selected faces has changed - update the texture properties to match the selection
            _freeze = true;

            var faces = GetFaceSelection();
            _currentTextureProperties.Reset(faces);

            var textures = new List<string>();

            foreach (var face in faces)
            {
                var tex = face.Texture;

                var name = tex.Name;
                if (textures.Any(x => String.Equals(x, name, StringComparison.InvariantCultureIgnoreCase))) continue;

                textures.Add(name);
            }

            var labelText = "";
            var d = Document;
            if (textures.Any() && d != null)
            {
                var t = textures[0];
                var tc = await d.Environment.GetTextureCollection();
                var ti = await tc.GetTextureItem(t);
                labelText = ti == null ? $"{t}" : $"{ti.Name} ({ti.Width} x {ti.Height})";
            }

            this.InvokeLater(() =>
            {

                ScaleXValue.Value = (decimal)_currentTextureProperties.XScale;
                ScaleYValue.Value = (decimal)_currentTextureProperties.YScale;
                ShiftXValue.Value = (decimal)_currentTextureProperties.XShift;
                ShiftYValue.Value = (decimal)_currentTextureProperties.YShift;
                RotationValue.Value = (decimal)_currentTextureProperties.Rotation;
                LightmapValue.Value = (decimal)(_currentTextureProperties.LightmapScale ?? 1);

                if (_currentTextureProperties.DifferentXScaleValues) ScaleXValue.Text = "";
                if (_currentTextureProperties.DifferentYScaleValues) ScaleYValue.Text = "";
                if (_currentTextureProperties.DifferentXShiftValues) ShiftXValue.Text = "";
                if (_currentTextureProperties.DifferentYShiftValues) ShiftYValue.Text = "";
                if (_currentTextureProperties.DifferentRotationValues) RotationValue.Text = "";

                if (_currentTextureProperties.AllAlignedToFace) AlignToFaceCheckbox.CheckState = CheckState.Checked;
                else if (_currentTextureProperties.NoneAlignedToFace) AlignToFaceCheckbox.CheckState = CheckState.Unchecked;
                else AlignToFaceCheckbox.CheckState = CheckState.Indeterminate;

                if (_currentTextureProperties.AllAlignedToWorld) AlignToWorldCheckbox.CheckState = CheckState.Checked;
                else if (_currentTextureProperties.NoneAlignedToWorld) AlignToWorldCheckbox.CheckState = CheckState.Unchecked;
                else AlignToWorldCheckbox.CheckState = CheckState.Indeterminate;

                TextureDetailsLabel.Text = labelText;
                _selectedTextures = textures;
                UpdateTextureList();
                TexturesList.SetHighlightedTextures(textures);
                HideMaskCheckbox.Checked = Document.Map.Data.GetOne<HideFaceMask>()?.Hidden == true;

                _freeze = false;
            });

        }

        private void PropertiesChanged()
        {
            if (_freeze) return;

            if (!_currentTextureProperties.DifferentXScaleValues) _currentTextureProperties.XScale = (float)ScaleXValue.Value;
            if (!_currentTextureProperties.DifferentYScaleValues) _currentTextureProperties.YScale = (float)ScaleYValue.Value;
            if (!_currentTextureProperties.DifferentXShiftValues) _currentTextureProperties.XShift = (float)ShiftXValue.Value;
            if (!_currentTextureProperties.DifferentYShiftValues) _currentTextureProperties.YShift = (float)ShiftYValue.Value;
            if (!_currentTextureProperties.DifferentRotationValues) _currentTextureProperties.Rotation = (float)RotationValue.Value;
            if (!_currentTextureProperties.DifferentLightmapValues) _currentTextureProperties.LightmapScale = (float)LightmapValue.Value;

            ApplyPropertyChanges(true);
            System.Threading.Interlocked.Exchange(ref _pendingCommit, 1);
            DebouncedPropertiesChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Commits the pending scale/shift/rotation change to the undo history (once).
        /// </summary>
        private void CommitPendingChanges()
        {
            if (System.Threading.Interlocked.Exchange(ref _pendingCommit, 0) == 0) return;
            ApplyPropertyChanges(false);
        }

        private static bool IsUndoRedoKey(Keys keyData)
        {
            if (!keyData.HasFlag(Keys.Control) || keyData.HasFlag(Keys.Alt)) return false;
            var key = keyData & Keys.KeyCode;
            return key == Keys.Z || key == Keys.Y;
        }

        protected override bool ProcessCmdKey(ref System.Windows.Forms.Message msg, Keys keyData)
        {
            // A change still inside the debounce window is committed first, so undo/redo sees it in the history
            if (IsUndoRedoKey(keyData)) CommitPendingChanges();
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool CheckIgnoreHotkey(Control source, Keys keyData)
        {
            // The numeric fields are text boxes, which normally keep Ctrl+Z / Ctrl+Y for their own text undo.
            // Here they should act on the map's undo history instead, without having to click outside first.
            if (IsUndoRedoKey(keyData)) return false;
            return base.CheckIgnoreHotkey(source, keyData);
        }

        private void ApplyFaceValues(Face target)
        {
            // apply values
            if (!_currentTextureProperties.DifferentXScaleValues) target.Texture.XScale = _currentTextureProperties.XScale;
            if (!_currentTextureProperties.DifferentXShiftValues) target.Texture.XShift = _currentTextureProperties.XShift;
            if (!_currentTextureProperties.DifferentYScaleValues) target.Texture.YScale = _currentTextureProperties.YScale;
            if (!_currentTextureProperties.DifferentYShiftValues) target.Texture.YShift = _currentTextureProperties.YShift;
            if (!_currentTextureProperties.DifferentRotationValues) target.Texture.SetRotation(_currentTextureProperties.Rotation);
            if (!_currentTextureProperties.DifferentLightmapValues) target.Texture.LightmapScale = _currentTextureProperties.LightmapScale;
        }

        private Dictionary<Face, Primitives.Texture> _memoTextures;

        private async void ApplyPropertyChanges(bool trivial)
        {
            await _applyLock.WaitAsync();
            try
            {
                await ApplyPropertyChangesCore(trivial);
            }
            finally
            {
                _applyLock.Release();
            }
        }

        // Caller must hold _applyLock
        private async Task ApplyPropertyChangesCore(bool trivial)
        {
            var edit = new Transaction();

            var sel = GetFaceSelection();
            if (trivial)
            {
                // Remember the state before the last change
                if (_memoTextures == null) _memoTextures = sel.ToDictionary(x => x, x => x.Texture.Clone());

                // Once a trivial change is started we know that there will definitely be a matching nontrivial task
                // We aggregate changes so they don't spam the undo stack
                edit.Add(new TrivialOperation(
                    x =>
                    {
                        foreach (var it in sel.GetSelectedFaces())
                        {
                            ApplyFaceValues(it.Value);
                        }
                    },
                    x =>
                    {
                        foreach (var p in sel.GetSelectedParents())
                        {
                            x.Update(p);
                        }
                    }
                ));
            }
            else
            {
                foreach (var it in sel.GetSelectedFaces())
                {
                    // Restore the last committed values
                    if (_memoTextures != null && _memoTextures.ContainsKey(it.Value))
                    {
                        var k = _memoTextures[it.Value];
                        it.Value.Texture.Unclone(k);
                    }

                    var clone = (Face)it.Value.Clone();
                    ApplyFaceValues(clone);

                    edit.Add(new RemoveMapObjectData(it.Key.ID, it.Value));
                    edit.Add(new AddMapObjectData(it.Key.ID, clone));
                }

                // Reset the memory
                _memoTextures = null;
            }

            await MapDocumentOperation.Perform(Document, edit);
        }

        private async void ApplyButtonClicked(object sender, EventArgs e)
        {
            var item = GetFirstSelectedTexture();
            await ApplyTexture(item);
        }

        private async Task ApplyChanges(Func<IMapObject, Face, Task<bool>> apply)
        {
            var found = false;

            await _applyLock.WaitAsync();
            try
            {
                // Commit any pending (debounced) property edit first so it can't run on top of this change later
                if (System.Threading.Interlocked.Exchange(ref _pendingCommit, 0) == 1)
                {
                    await ApplyPropertyChangesCore(false);
                }

                var sel = GetFaceSelection();
                var edit = new Transaction();

                // Materialise now: the faces are looked up by ID, so this must see the current (post-commit) faces
                foreach (var it in sel.GetSelectedFaces().ToList())
                {
                    var clone = (Face)it.Value.Clone();
                    var result = await apply(it.Key, clone);
                    if (!result) continue;

                    found = true;

                    edit.Add(new RemoveMapObjectData(it.Key.ID, it.Value));
                    edit.Add(new AddMapObjectData(it.Key.ID, clone));
                }

                if (found)
                {
                    await MapDocumentOperation.Perform(Document, edit);
                }
            }
            finally
            {
                _applyLock.Release();
            }

            if (found) await FaceSelectionChanged();
        }

        private async Task ApplyTexture(string item)
        {
            if (String.IsNullOrWhiteSpace(item)) return;

            await ApplyChanges((mo, f) =>
            {
                f.Texture.Name = item;
                ApplyFaceValues(f);
                return Task.FromResult(true);
            });
        }

        private void ScaleXValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentXScaleValues = false;
            PropertiesChanged();
        }

        private void ScaleYValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentYScaleValues = false;
            PropertiesChanged();
        }

        private void ShiftXValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentXShiftValues = false;
            PropertiesChanged();
        }

        private void ShiftYValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentYShiftValues = false;
            PropertiesChanged();
        }

        private void RotationValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentRotationValues = false;
            PropertiesChanged();
        }

        private void LightmapValueChanged(object sender, EventArgs e)
        {
            if (_freeze) return;
            _currentTextureProperties.DifferentLightmapValues = false;
            PropertiesChanged();
        }

        private async void JustifyTopClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Top, false);
        }

        private async Task Justify(BoxAlignMode mode, bool fit)
        {
            var sel = GetFaceSelection();
            if (sel.IsEmpty) return;

            Cloud cloud = null;
            if (ShouldTreatAsOne()) cloud = new Cloud(sel.GetSelectedFaces().SelectMany(x => x.Value.Vertices));

            var tc = await Document.Environment.GetTextureCollection();
            if (tc == null) return;

            await ApplyChanges(async (mo, f) =>
            {
                var tex = await tc.GetTextureItem(f.Texture.Name);
                if (tex == null) return false;

                if (fit) f.Texture.FitToPointCloud(tex.Width, tex.Height, cloud ?? new Cloud(f.Vertices), 1, 1);
                else f.Texture.AlignWithPointCloud(tex.Width, tex.Height, cloud ?? new Cloud(f.Vertices), mode);

                return true;
            });
        }

        private async void JustifyLeftClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Left, false);
        }

        private async void JustifyCenterClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Center, false);
        }

        private async void JustifyRightClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Right, false);
        }

        private async void JustifyBottomClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Bottom, false);
        }

        private async void JustifyFitClicked(object sender, EventArgs e)
        {
            await Justify(BoxAlignMode.Center, true);
        }

        private async void HideMaskCheckboxToggled(object sender, EventArgs e)
        {
            if (_freeze) return;
            var data = Document.Map.Data.GetOne<HideFaceMask>() ?? new HideFaceMask();
            data = new HideFaceMask { Hidden = !data.Hidden };
            await MapDocumentOperation.Perform(Document, new TrivialOperation(x => x.Map.Data.Replace(data), x => x.Update(data)));
        }

        private async void AlignToWorldClicked(object sender, EventArgs e)
        {
            await ApplyChanges((mo, f) =>
            {
                f.Texture.AlignToNormal(f.Plane.GetClosestAxisToNormal());
                return Task.FromResult(true);
            });
        }

        private async void AlignToFaceClicked(object sender, EventArgs e)
        {
            await ApplyChanges((mo, f) =>
            {
                f.Texture.AlignToNormal(f.Plane.Normal);
                return Task.FromResult(true);
            });
        }

        private void BrowseButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("Command:Run", new CommandMessage("BspEditor:BrowseActiveTexture"));
        }

        private async void TexturesListTextureSelected(object sender, string item)
        {
            await ApplyTexture(item);
        }

        private void TreatAsOneCheckboxToggled(object sender, EventArgs e)
        {
            if (_freeze) return;
            // Nothing required here
        }

        private void MarkButtonClicked(object sender, EventArgs e)
        {
            var doc = Document;
            if (doc == null) return;

            // Use the texture highlighted in the viewer, falling back to the active texture
            var name = GetFirstSelectedTexture() ?? doc.Map.Data.GetOne<ActiveTexture>()?.Name;
            if (String.IsNullOrWhiteSpace(name)) return;

            bool Matches(Face f) => String.Equals(f.Texture.Name, name, StringComparison.InvariantCultureIgnoreCase);

            // Mark (face-select) every face in the map that uses this texture
            var sel = GetFaceSelection();
            sel.Clear();
            foreach (var obj in doc.Map.Root.Find(x => x.Data.OfType<Face>().Any(Matches)).ToList())
            {
                sel.Add(obj, obj.Data.OfType<Face>().Where(Matches).ToArray());
            }

            Oy.Publish("TextureTool:SelectionChanged", sel);
        }

        private void ReplaceButtonClicked(object sender, EventArgs e)
        {
            Oy.Publish("Command:Run", new CommandMessage("BspEditor:ReplaceTextures"));
        }

        private void SmoothingGroupsButtonClicked(object sender, EventArgs e)
        {
            // TODO Source: Texture Smoothing Groups
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Oy.Publish("ActivateTool", "SelectTool");
            }
            this.Owner.Focus();
        }

        private void FocusTextInControl(object sender, EventArgs e)
        {
            var nud = sender as NumericUpDown;
            nud?.Select(0, nud.Text.Length);
        }

        public bool ShouldTreatAsOne()
        {
            return TreatAsOneCheckbox.Checked;
        }

        private class CurrentTextureProperties : Primitives.Texture
        {
            public bool DifferentXScaleValues { get; set; }
            public bool DifferentYScaleValues { get; set; }

            public bool DifferentXShiftValues { get; set; }
            public bool DifferentYShiftValues { get; set; }

            public bool DifferentRotationValues { get; set; }

            public bool AllAlignedToFace { get; set; }
            public bool NoneAlignedToFace { get; set; }

            public bool AllAlignedToWorld { get; set; }
            public bool NoneAlignedToWorld { get; set; }
            public bool DifferentLightmapValues { get; set; }

            public CurrentTextureProperties()
            {
                Reset();
            }
            public void ResetTexture(IEnumerable<Face> faces)
            {
                Reset();
                foreach (var face in faces)
                {
                    face.Texture.XScale = face.Texture.YScale = 1;
                    face.Texture.XShift = face.Texture.YShift = 0;
                    face.Texture.SetRotation(0);
                }
            }
            public void Reset()
            {
                Rotation = XShift = YShift = 0;
                XScale = YScale = 1;
                DifferentXScaleValues = DifferentYScaleValues = DifferentXShiftValues = DifferentYShiftValues = false;
                AllAlignedToFace = AllAlignedToWorld = false;
                NoneAlignedToFace = NoneAlignedToWorld = true;
            }

            public void Reset(IEnumerable<Face> faces)
            {
                Reset();
                var num = 0;
                AllAlignedToWorld = NoneAlignedToWorld = AllAlignedToFace = NoneAlignedToFace = true;
                foreach (var face in faces)
                {
                    if (face.Texture.IsAlignedToNormal(face.Plane.Normal)) NoneAlignedToFace = false;
                    else AllAlignedToFace = false;

                    if (face.Texture.IsAlignedToNormal(face.Plane.GetClosestAxisToNormal())) NoneAlignedToWorld = false;
                    else AllAlignedToWorld = false;

                    if (num == 0)
                    {
                        if (!float.IsNaN(face.Texture.XScale)) XScale = face.Texture.XScale;
                        if (!float.IsNaN(face.Texture.YScale)) YScale = face.Texture.YScale;
                        if (!float.IsNaN(face.Texture.XShift)) XShift = face.Texture.XShift;
                        if (!float.IsNaN(face.Texture.YShift)) YShift = face.Texture.YShift;
                        if (!float.IsNaN(face.Texture.Rotation)) Rotation = face.Texture.Rotation;
                        if (float.IsNaN(face.Texture.LightmapScale ?? float.NaN)) LightmapScale = 1;
                        if (!float.IsNaN(face.Texture.LightmapScale ?? float.NaN)) LightmapScale = face.Texture.LightmapScale;
                    }
                    else
                    {
                        if (face.Texture.XScale != XScale) DifferentXScaleValues = true;
                        if (face.Texture.YScale != YScale) DifferentYScaleValues = true;
                        if (face.Texture.XShift != XShift) DifferentXShiftValues = true;
                        if (face.Texture.YShift != YShift) DifferentYShiftValues = true;
                        if (face.Texture.Rotation != Rotation) DifferentRotationValues = true;
                        if (face.Texture.LightmapScale != LightmapScale) DifferentLightmapValues = true;
                    }
                    num++;
                }

                // WinForms hack: use a tiny decimal place so that the NumericUpDown controls work when the value is typed into the box
                // E.g. Different X scale defaults to value of 1, but if 1 is typed in the box, the ValueChanged event won't fire since the backing value hasn't changed
                // Setting the value to 1.000001 instead triggers the change event properly, and since the NUD rounds to 4 decimal places, pressing the up/down buttons will start from the rounded value.
                if (DifferentXScaleValues) XScale = 1.000001f;
                if (DifferentYScaleValues) YScale = 1.000001f;
                if (DifferentXShiftValues) XShift = 0.000001f;
                if (DifferentYShiftValues) YShift = 0.000001f;
                if (DifferentRotationValues) Rotation = 0.000001f;

                if (XScale < -4096 || XScale > 4096) XScale = 1;
                if (YScale < -4096 || YScale > 4096) YScale = 1;
                if (XShift < -4096 || XShift > 4096) XShift = 1;
                if (YShift < -4096 || YShift > 4096) YShift = 1;
                Rotation = (Rotation % 360 + 360) % 360;
            }
        }

        private async void ResetButton_Click(object sender, EventArgs e)
        {
            // Frozen so setting the controls doesn't fire five PropertiesChanged events (and a second debounced commit)
            var wasFrozen = _freeze;
            _freeze = true;
            RotationValue.Value = 0;
            ScaleXValue.Value = ScaleYValue.Value = 1;
            ShiftXValue.Value = ShiftYValue.Value = 0;
            _freeze = wasFrozen;

            // Reset only the clones inside ApplyChanges - the live faces are never edited outside an operation
            await ApplyChanges((mo, f) =>
            {
                _currentTextureProperties.Reset();
                ApplyFaceValues(f);
                return Task.FromResult(true);
            });
        }

        private async void RotateButton_Click(object sender, EventArgs e)
        {
            if (sender is Button b && b.Tag is float degrees) await RotateFaceTexture(degrees);
        }

        private async Task RotateFaceTexture(float degree)
        {
            var faces = GetFaceSelection();
            _currentTextureProperties.Rotation += degree;
            foreach (var face in faces)
            {
                face.Texture.SetRotation(face.Texture.Rotation + degree);
            }
            await ApplyChanges((mo, f) =>
            {
                ApplyFaceValues(f);
                return Task.FromResult(true);
            });
        }

		private async void apply_null_Click(object sender, EventArgs e)
		{
			await ApplyTexture("null");
		}
	}
}
