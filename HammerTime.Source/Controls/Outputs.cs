using HammerTime.Source.Primitives.MapObjectData;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Documents;
using Sledge.DataStructures.GameData;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Data;

namespace HammerTime.Source.Controls
{
    [Export(typeof(IObjectPropertyEditorTab))]
    public partial class Outputs : UserControl, IObjectPropertyEditorTab
    {
        public static readonly Capability OutputsFeature = Capability.Create("ObjectProperty.Outputs");
        private MapDocument _document;
        private IOrderedEnumerable<string> _entities;
        private GameData _gameData;
        private IEnumerable<Entity> entities;
        private IEnumerable<Entity> objects;

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
            return context.TryGet("ActiveDocument", out MapDocument md) && md.Capabilities.Contains(OutputsFeature) &&
                   objects.Any(x => x.Data.GetOne<EntityData>() != null);
        }

        public async Task SetObjects(MapDocument document, List<IMapObject> objects)
        {
            _document = document;
            _entities = document.Map.Root.Find(x => x.Data.GetOne<EntityData>() != null)
                .Select(x => x.Data.GetOne<EntityData>().Get<string>("targetname"))
                .Where(x => !String.IsNullOrWhiteSpace(x))
                .Distinct()
                .OrderBy(x => x.ToLowerInvariant());

            entityBox.Items.Clear();
            entityBox.Items.AddRange(_entities.ToArray());

            this.objects = objects.OfType<Entity>();

            connectionsView.Items.Clear();
            connectionsView.Items.AddRange(objects
                .SelectMany(x => x.Data.Get<Connections>())
                .SelectMany(x => x.EntityConnections)
                .Select(x => new ListViewItem(new string[] { x.Name, x.TargetEntity, x.TargetAction, x.Parameter, x.Delay.ToString(), x.Once ? "Yes" : "No" }) { Tag = x })
                .ToArray());

            _gameData = await document.Environment.GetGameData();
            entities = _document.Map.Root.Hierarchy.OfType<Entity>();
        }

        private void connectionsView_SelectedIndexChanged(object sender, EventArgs e)
        {
            outputBox.Items.Clear();
            inputBox.Items.Clear();

            if (connectionsView.SelectedItems.Count < 1) return;
            var selected = connectionsView.SelectedItems[0].Tag as Connections.Connection;
            outputBox.Text = selected.Name;
            entityBox.Text = selected.TargetEntity;
            inputBox.Text = selected.TargetAction;
            parameterBox.Text = selected.Parameter;
            delayBox.Text = selected.Delay.ToString();
            chkFireOnce.Checked = selected.Once;

            outputBox.Items.AddRange(_gameData.Classes.First(c => c.Name.Equals(objects.First().EntityData.Name)).InOuts.Select(x => x.Name).ToArray());
            var target = entities.FirstOrDefault(x => x.EntityData.GetStringProperty("targetname", String.Empty).Equals(entityBox.Text));
            if (target != null)
            {
                inputBox.Items.AddRange(_gameData.Classes.First(c => c.Name.Equals(target.EntityData.Name)).InOuts.Select(x => x.Name).ToArray());
            }
        }
    }
}
