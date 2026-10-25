using System;
using System.ComponentModel.Composition;
using Sledge.Common.Translations;
using Sledge.Common.Shell.Settings;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// Provides a custom editor for the <see cref="EntityBrushColorSettings.CustomEntityColours"/> setting.
    /// </summary>
    [Export(typeof(ISettingEditorFactory))]
    public class EntityBrushColorEditorFactory : ISettingEditorFactory
    {
        #pragma warning disable CS0649
        [Import] private Lazy<TranslationStringsCatalog> _catalog;
        #pragma warning restore CS0649
        #pragma warning disable CS0649
        [Import] private Lazy<ITranslationStringProvider> _strings;
        #pragma warning restore CS0649

        // OrderHint "X" sorts after the default ShellEditorsFactory ("W") so any key whose
        // EditorType is "EntityBrushColorEditor" is claimed by this factory.
        public string OrderHint => "X";

        public bool Supports(SettingKey key)
        {
            return key.EditorType == "EntityBrushColorEditor";
        }

        public ISettingEditor CreateEditorFor(SettingKey key)
        {
            return new EntityBrushColorEditor(_catalog.Value, _strings.Value);
        }
    }
}
