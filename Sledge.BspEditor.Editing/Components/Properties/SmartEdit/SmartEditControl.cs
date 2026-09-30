using System;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.DataStructures.GameData;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
    public abstract class SmartEditControl : FlowLayoutPanel, IObjectPropertyEditor
    {
        public Control Control => this;

        public string OriginalName { get; private set; }
        public string PropertyName { get; private set; }
        public string PropertyValue { get; private set; }
        public Property Property { get; private set; }
        public abstract string PriorityHint { get; }

        /// <summary>The document the properties belong to. Set when SetProperty is called.</summary>
        protected MapDocument Document { get; private set; }

        /// <summary>The class name of the entity being edited (set by the host before SetProperty).</summary>
        public string ClassName { get; set; }

        /// <summary>Gets the current value of another keyvalue on the same entity, or null if there isn't one (set by the host).</summary>
        public Func<string, string> GetSiblingValue { get; set; }

        public delegate void ValueChangedEventHandler(object sender, string propertyName, string propertyValue);
        public delegate void NameChangedEventHandler(object sender, string oldName, string newName);

        public event EventHandler<string> ValueChanged;
        public event EventHandler<string> NameChanged;

        protected virtual void OnValueChanged()
        {
            if (_setting) return;
            PropertyValue = GetValue();
            ValueChanged?.Invoke(this, PropertyValue);
        }
        
        protected virtual void OnNameChanged()
        {
            if (_setting) return;
            PropertyName = GetName();
            NameChanged?.Invoke(this, PropertyName);
        }

        protected SmartEditControl()
        {
            Dock = DockStyle.Fill;
        }

        private bool _setting;

        public void SetProperty(MapDocument document, string originalName, string newName, string currentValue, Property property)
        {
            _setting = true;
            Document = document;
            OriginalName = originalName;
            PropertyName = newName;
            PropertyValue = currentValue;
            Property = property;
            OnSetProperty(document);
            _setting = false;
        }

        public abstract bool SupportsType(VariableType type);
        protected abstract string GetName();
        protected abstract string GetValue();
        protected abstract void OnSetProperty(MapDocument document);
    }
}