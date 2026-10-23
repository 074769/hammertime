using LogicAndTrick.Oy;
using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Editing.Components.Properties.Tabs;
using Sledge.BspEditor.Editing.Properties;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Selection;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.DataStructures.GameData;
using Sledge.QuickForms;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sledge.BspEditor.Editing.Commands
{
	[AutoTranslate]
	[Export(typeof(ICommand))]
	[MenuItem("Tools", "", "Entity", "D")]
	[CommandID("BspEditor:Tools:TieToEntity")]
	[MenuImage(typeof(Resources), nameof(Resources.Menu_TieToEntity))]
	[DefaultHotkey("Ctrl+T")]
	public class TieToEntity : BaseCommand
	{
		public override string Name { get; set; } = "Tie to entity";
		public override string Details { get; set; } = "Create an entity out of the selection.";

		public string EntitySelectedTitle { get; set; }
		public string OneEntitySelectedMessage { get; set; }
		public string MultipleEntitiesSelectedMessage { get; set; }
		public string KeepExisting { get; set; }
		public string CreateNew { get; set; }
		public string OK { get; set; }
		public string Cancel { get; set; }

		protected override bool IsInContext(IContext context, MapDocument document)
		{
			return base.IsInContext(context, document) && !document.Selection.IsEmpty;
		}

		protected override async Task Invoke(MapDocument document, CommandParameters parameters)
		{
			// Entities that were selected directly. Only these are merged into the chosen entity and destroyed.
			var selectedEntities = document.Selection.OfType<Entity>().ToList();

			// Entities that can be chosen as the destination
			var existingEntities = selectedEntities.ToList();

			Entity existing = null;
			var confirmed = false;

			var tf = document.Map.Data.GetOne<SelectionOptions>();
			var ignoreGrouping = tf?.IgnoreGrouping ?? false;
			if (ignoreGrouping)
			{
				// With ignore grouping, the selected objects can come from several entities. If they do, the entities
				// they came from can be chosen as the destination. If they all come from one entity and no other entity
				// is selected, nothing is chosen and they are moved into a new entity.
				var parents = document.Selection.Select(x => x.Hierarchy.Parent).OfType<Entity>().Distinct().ToList();
				if (parents.Count > 1) existingEntities.AddRange(parents.Where(x => !existingEntities.Contains(x)));
			}

			if (!ignoreGrouping && existingEntities.Count == 0 && document.Selection.Count == 1 && document.Selection.First().Hierarchy.Parent is Entity ent)
			{
				await Oy.Publish("Context:Add", new ContextInfo("BspEditor:ObjectProperties"));
				return;
			}
			if (existingEntities.Count == 0)
			{
				// No entities selected, just create it straight up
				confirmed = true;
			}
			else if (existingEntities.Count == 1)
			{
				// One entity selected, user chooses to merge or create a new entity
				using (
					var qf = new QuickForm(EntitySelectedTitle) { Width = 400 }
						.Label(String.Format(OneEntitySelectedMessage, existingEntities[0].EntityData?.Name))
						.DialogButtons(
							(KeepExisting, DialogResult.Yes),
							(CreateNew, DialogResult.No),
							(Cancel, DialogResult.Cancel)
						)
				)
				{
					var result = await qf.ShowDialogAsync();
					if (result == DialogResult.Yes) existing = existingEntities[0];
					confirmed = result != DialogResult.Cancel;
				}
			}
			else if (existingEntities.Count > 1)
			{
				// Multiple entities selected, user chooses which one to keep
				using (
					var qf = new QuickForm(EntitySelectedTitle) { Width = 400 }
						.Label(MultipleEntitiesSelectedMessage)
						.ComboBox("Entity", "", existingEntities.Select(x => new EntityContainer { Entity = x }))
						.OkCancel(OK, Cancel)
				)
				{
					var result = await qf.ShowDialogAsync();
					if (result == DialogResult.OK) existing = (qf.Object("Entity") as EntityContainer)?.Entity;
					confirmed = result != DialogResult.Cancel;
				}
			}

			if (!confirmed) return;

			var ops = new List<IOperation>();

			var gameData = await document.Environment.GetGameData();
			var def = document.Environment.DefaultBrushEntity;
			var defaultEntityClass = (
				from g in gameData.Classes
				where g.ClassType == ClassType.Solid
				orderby String.Equals(g.Name, def, StringComparison.InvariantCultureIgnoreCase) ? 0 : 1,
						String.Equals(g.Name, "trigger_once", StringComparison.InvariantCultureIgnoreCase) ? 0 : 1,
						g.Description.ToLower()
				select g
			).FirstOrDefault() ?? new GameDataObject("trigger_once", "", ClassType.Solid);

			if (existing == null)
			{
				// If the entity doesn't exist we need to create it
				existing = new Entity(document.Map.NumberGenerator.Next("MapObject"))
				{
					Data =
					{
						new EntityData { Name = defaultEntityClass.Name },
						new ObjectColor(Colour.GetDefaultEntityColour())
					}
				};
				ops.Add(new Attach(document.Map.Root.ID, existing));
				var properties = defaultEntityClass.Properties.Select(x => (x.Name ?? "").ToLower());
				foreach (var property in properties)
				{
					var newKey = defaultEntityClass.Properties.FirstOrDefault(x => (x.Name ?? "").ToLower() == property);

					var value = ClassInfoTab.GetDefaultOption(newKey);

					existing.EntityData.Properties.Add(property, value);
				}
			}
			else
			{
				// If the entity is a descendant of the selection, it would cause havok
				ops.Add(new Detatch(existing.Hierarchy.Parent.ID, existing));
				ops.Add(new Attach(document.Map.Root.ID, existing));
			}

			// Any other selected entities should be destroyed
			foreach (var entity in selectedEntities.Where(x => !ReferenceEquals(x, existing)))
			{
				var children = entity.Hierarchy.Where(x => !(x is Entity)).ToList();
				ops.Add(new Detatch(entity.ID, children));
				ops.Add(new Attach(existing.ID, children));
				ops.Add(new Detatch(entity.Hierarchy.Parent.ID, entity));
			}

			// Selected objects that are not already in the entity are moved into it
			var moved = document.Selection.GetSelectedParents()
				.Except(selectedEntities)
				.Where(x => !ReferenceEquals(x.Hierarchy.Parent, existing))
				.ToList();
			foreach (var obj in moved)
			{
				ops.Add(new Detatch(obj.Hierarchy.Parent.ID, obj));
				ops.Add(new Attach(existing.ID, obj));
			}

			// An entity that has nothing left after the move is removed
			var movedSet = new HashSet<IMapObject>(moved);
			var sources = moved.Select(x => x.Hierarchy.Parent).OfType<Entity>()
				.Where(x => !ReferenceEquals(x, existing) && !selectedEntities.Contains(x))
				.Distinct()
				.ToList();
			foreach (var source in sources)
			{
				if (source.Hierarchy.Any(x => !movedSet.Contains(x))) continue;
				ops.Add(new Detatch(source.Hierarchy.Parent.ID, source));
			}

			if (ops.Any())
			{
				ops.Add(new Select(document.Selection.Except(selectedEntities).Union(new[] { existing })));
				await MapDocumentOperation.Perform(document, new Transaction(ops));
				await Oy.Publish("Context:Add", new ContextInfo("BspEditor:ObjectProperties"));
			}
		}

		private class EntityContainer
		{
			public Entity Entity { get; set; }
			public override string ToString()
			{
				if (Entity.EntityData == null) return Convert.ToString(Entity.ID);
				return Entity.EntityData.Properties.ContainsKey("targetname")
					? Entity.EntityData.Properties["targetname"] + " (" + Entity.EntityData.Name + ")"
					: Entity.EntityData.Name;
			}
		}
	}
}