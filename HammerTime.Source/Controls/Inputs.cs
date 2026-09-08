using HammerTime.Source.Primitives.MapObjectData;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using System.ComponentModel;
using System.ComponentModel.Composition;

namespace HammerTime.Source.Controls
{
    [Export(typeof(IObjectPropertyEditorTab))]

    public partial class Inputs : UserControl, IObjectPropertyEditorTab
    {
        public Inputs()
        {
            InitializeComponent();
        }
        public static readonly Capability InputsCapable = Capability.Create("ObjectProperty.Inputs");
        private List<IMapObject> objects;
        private IEnumerable<IEnumerable<Connections>> _connections;

        public string OrderHint => "D0";

        public Control Control => this;

        public bool HasChanges => false;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IEnumerable<IOperation> GetChanges(MapDocument document, List<IMapObject> objects)
        {
            throw new NotImplementedException();
        }

        public bool IsInContext(IContext context, List<IMapObject> list)
        {
            return context.TryGet("ActiveDocument", out MapDocument md) && md.Capabilities.Contains(InputsCapable) &&
                   objects.Any(x => x.Data.GetOne<EntityData>() != null);
        }

        public Task SetObjects(MapDocument document, List<IMapObject> objects)
        {
            if (!objects.Any()) return Task.CompletedTask;

            var objectsNames = objects.Select(x => x as Entity).Where(x => x != null).Select(x => x.EntityData.GetStringProperty("targetname", String.Empty));

            var objectsConnections = document.Map.Root.Hierarchy
                .Where(x => x is Entity)
                .Select(x => x as Entity)
                .Where(x => x.Data.GetOne<Connections>() != null);
            var inputs = objectsConnections
                .Where(x =>
                {
                    var entityConnections = x.Data.GetOne<Connections>();
                    var cons = entityConnections.EntityConnections.Select(x => objectsNames.Contains(x.TargetEntity));
                    return cons.Any() ? true : false;
                });
            this.objects = objects;
            connectionsView.Items.Clear();
            connectionsView.Items.AddRange(inputs
                .SelectMany(x => x.Data.GetOne<Connections>()
                    .EntityConnections.Where(y => objectsNames.Contains(y.TargetEntity)) //This required to avoid including connections, which are not pointed to current object/s
                    .Select(y => new ListViewItem(new string[] { x.EntityData.Name, y.Name, y.TargetAction, y.Parameter, y.Delay.ToString(), y.Once ? "Yes" : "No" }))).ToArray());
            return Task.CompletedTask;
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }
    }
}
