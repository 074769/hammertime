using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Context;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HammerTime.Source.Controls
{
    [Export(typeof(IObjectPropertyEditorTab))]
    public partial class Outputs : UserControl, IObjectPropertyEditorTab
    {
        private IOrderedEnumerable<string> _entities;

        public Outputs()
        {
            InitializeComponent();
        }

        public string OrderHint => "D1";

        public Control Control => this;

        public bool HasChanges => false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IEnumerable<IOperation> GetChanges(MapDocument document, List<IMapObject> objects)
        {
            throw new NotImplementedException();
        }

        public bool IsInContext(IContext context, List<IMapObject> list)
        {
            return true;
        }

        public Task SetObjects(MapDocument document, List<IMapObject> objects)
        {
            _entities = document.Map.Root.Find(x => x.Data.GetOne<EntityData>() != null)
                .Select(x => x.Data.GetOne<EntityData>().Get<string>("targetname"))
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x.ToLowerInvariant());

            entityBox.Items.Clear();
            entityBox.Items.AddRange(_entities.ToArray());

            return Task.CompletedTask;
        }
    }
}
