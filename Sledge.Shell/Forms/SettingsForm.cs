using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Settings;
using Sledge.Common.Translations;
using Sledge.Shell.Properties;
using Sledge.Shell.Settings.Editors;

namespace Sledge.Shell.Forms
{
    [Export(typeof(IDialog))]
    [AutoTranslate]
	public partial class SettingsForm : Form, IDialog
    {
        private readonly IEnumerable<Lazy<ISettingEditorFactory>> _editorFactories;
        private readonly IEnumerable<Lazy<ISettingsContainer>> _settingsContainers;
        private readonly Lazy<ITranslationStringProvider> _translations;
        private readonly Lazy<Form> _parent;

        private Dictionary<ISettingsContainer, List<SettingKey>> _keys;
        private Dictionary<ISettingsContainer, JsonSettingsStore> _values;
        private Dictionary<ISettingsContainer, JsonSettingsStore> _originalValues;
		private bool _darktheme;

		public string Title
        {
            get => Text;
            set => this.InvokeLater(() => Text = value);
        }

        public string OK
        {
            get => OKButton.Text;
            set => this.InvokeLater(() => OKButton.Text = value);
        }

        public string Cancel
        {
            get => CancelButton.Text;
            set => this.InvokeLater(() => CancelButton.Text = value);
        }

        [ImportingConstructor]
        public SettingsForm(
            [ImportMany] IEnumerable<Lazy<ISettingEditorFactory>> editorFactories, 
            [ImportMany] IEnumerable<Lazy<ISettingsContainer>> settingsContainers, 
            [Import] Lazy<ITranslationStringProvider> translations, 
            [Import("Shell")] Lazy<Form> parent
        )
        {
            _editorFactories = editorFactories;
            _settingsContainers = settingsContainers;
            _translations = translations;
            _parent = parent;

            InitializeComponent();
            Icon = Icon.FromHandle(Resources.Menu_Options.GetHicon());
            CreateHandle();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            if (Visible)
            {
                _keys = _settingsContainers.ToDictionary(x => x.Value, x =>
                {
                    var keys = x.Value.GetKeys().ToList();
                    keys.ForEach(k => k.Container = x.Value.Name);
                    return keys;
                });
                _values = _settingsContainers.ToDictionary(x => x.Value, x =>
                {
                    var fss = new JsonSettingsStore();
                    x.Value.StoreValues(fss);
                    return fss;
                });
                // Keep a pristine copy of the values the dialog opened with, so that OK
                // can tell which containers the user actually changed.
                _originalValues = _values.ToDictionary(x => x.Key, x => new JsonSettingsStore(x.Value.ToJson()));
                LoadGroupList();
            }
            base.OnVisibleChanged(e);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Oy.Publish("Context:Remove", new ContextInfo("SettingsForm"));
            this.Owner.Focus();
		}

        private void LoadGroupList()
        {
            GroupList.BeginUpdate();

            var nodes = new Dictionary<string, TreeNode>();

            GroupList.Nodes.Clear();
            foreach (var k in _keys.SelectMany(x => x.Value).GroupBy(x => x.Group).OrderBy(x => x.Key))
            {
                var gh = new GroupHolder(k.Key, _translations.Value.GetSetting("@Group." + k.Key) ?? k.Key);

                TreeNode parentNode = null;
                var par = k.Key.LastIndexOf('/');
                if (par > 0)
                {
                    var sub = k.Key.Substring(0, par);
                    if (nodes.ContainsKey(sub))
                    {
                        parentNode = nodes[sub];
                    }
                    else
                    {
                        var pgh = new GroupHolder(sub, _translations.Value.GetSetting("@Group." + sub) ?? sub);
                        parentNode = new TreeNode(pgh.Label) {Tag = pgh};
                        GroupList.Nodes.Add(parentNode);
                        nodes.Add(sub, parentNode);
                    }
                }
                var node = new TreeNode(gh.Label) { Tag = gh };
                if (parentNode != null) parentNode.Nodes.Add(node);
                else GroupList.Nodes.Add(node);
                nodes.Add(k.Key, node);
            }

            GroupList.ExpandAll();

            GroupList.EndUpdate();
        }
        
        protected override void OnMouseEnter(EventArgs e)
        {
            Focus();
            base.OnMouseEnter(e);
        }

        private readonly List<ISettingEditor> _editors = new List<ISettingEditor>();

        private void LoadEditorList()
        {
            _editors.ForEach(x => x.OnValueChanged -= OnValueChanged);
            _editors.Clear();

            SettingsPanel.SuspendLayout();
            SettingsPanel.Controls.Clear();
            
            SettingsPanel.RowStyles.Clear();

            if (GroupList?.SelectedNode?.Tag is GroupHolder gh)
            {
                var group = gh.Key;
                foreach (var kv in _keys)
                {
                    var container = kv.Key;
                    var keys = kv.Value.Where(x => x.Group == group);
                    var values = _values[container];
                    foreach (var key in keys)
                    {
                        var editor = GetEditor(key);
                        editor.UseDarkTheme(_darktheme);
						editor.Key = key;
                        editor.Label = _translations.Value.GetSetting($"{kv.Key.Name}.{key.Key}") ?? key.Key;
                        editor.Value = values.Get(key.Type, key.Key);

                        if (SettingsPanel.Controls.Count > 0)
                        {
                            // Add a separator
                            var line = new Label
                            {
                                Height = 1,
                                BackColor = Color.FromArgb(128, Color.Black),
                                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
                            };
                            SettingsPanel.Controls.Add(line);
                        }

                        var ctrl = (Control) editor.Control;
                        ctrl.Anchor |= AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                        SettingsPanel.Controls.Add(ctrl);

                        if (ctrl.Anchor.HasFlag(AnchorStyles.Bottom))
                        {
                            SettingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                        }

                        _editors.Add(editor);
                    }
                }
            }

            SettingsPanel.ResumeLayout();

            _editors.ForEach(x => x.OnValueChanged += OnValueChanged);
        }

        private void OnValueChanged(object sender, SettingKey key)
        {
            var se = sender as ISettingEditor;
            var store = _values.Where(x => x.Key.Name == key.Container).Select(x => x.Value).FirstOrDefault();
            if (store != null && se != null)
            {
                store.Set(key.Key, se.Value);
            }
        }

        private ISettingEditor GetEditor(SettingKey key)
        {
            foreach (var ef in _editorFactories.OrderBy(x => x.Value.OrderHint))
            {
                if (ef.Value.Supports(key)) return ef.Value.CreateEditorFor(key);
            }
            return new DefaultSettingEditor();
        }

        public bool IsInContext(IContext context)
        {
            return context.HasAny("SettingsForm");
        }

        public void SetVisible(IContext context, bool visible)
        {
            this.InvokeLater(() =>
            {
                if (visible)
                {
                    if (!Visible) Show(_parent.Value);
                }
                else
                {
                    if (Visible) Hide();
                }
            });
        }

        private void GroupListSelectionChanged(object sender, TreeViewEventArgs e)
        {
            LoadEditorList();
        }

        private void OkClicked(object sender, EventArgs e)
        {
            // Work out what the user actually changed in this dialog. LoadValues can have
            // side effects (window and dock layout, engine state, environments, ...), so
            // containers that didn't change must not be touched at all.
            var changes = CalculateChanges();

            foreach (var kv in _values)
            {
                if (changes.MayHaveChanged(kv.Key.Name)) kv.Key.LoadValues(kv.Value);
            }

            Oy.Publish("SettingPreChanged", changes);
            Oy.Publish("Settings:Save");
            Oy.Publish("SettingsChanged", changes);
            Close();
        }

        /// <summary>
        /// Diff the values the dialog opened with against the values the user left behind,
        /// and return the containers that actually changed.
        /// </summary>
        private SettingsChangeSet CalculateChanges()
        {
            var changed = new List<string>();
            foreach (var kv in _values)
            {
                // No snapshot to compare against: assume the container changed
                if (_originalValues == null || !_originalValues.TryGetValue(kv.Key, out var original))
                {
                    changed.Add(kv.Key.Name);
                    continue;
                }

                if (original.GetDifferentKeys(kv.Value).Any())
                {
                    changed.Add(kv.Key.Name);
                }
            }
            return new SettingsChangeSet(changed);
        }

        private void CancelClicked(object sender, EventArgs e)
        {
            Close();
        }

		public void UseDarkTheme(bool dark)
        {
            _darktheme = dark;
		}
		private class GroupHolder
        {
            public string Key { get; set; }
            public string Label { get; set; }

            public GroupHolder(string key, string label)
            {
                Key = key;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }
    }
}
