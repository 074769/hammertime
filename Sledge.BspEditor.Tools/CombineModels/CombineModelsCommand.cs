using Sledge.BspEditor.Commands;
using Sledge.BspEditor.Documents;
using Sledge.BspEditor.Modification;
using Sledge.BspEditor.Modification.Operations.Tree;
using Sledge.BspEditor.Primitives.MapObjectData;
using Sledge.BspEditor.Primitives.MapObjects;
using Sledge.Common;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Menu;
using Sledge.Common.Translations;
using Sledge.FileSystem;
using Sledge.Providers.Model.Mdl10.Combine;
using Sledge.Providers.Model.Mdl10.Format;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using MapEntity = Sledge.BspEditor.Primitives.MapObjects.Entity;

namespace Sledge.BspEditor.Tools.CombineModels
{
	/// <summary>
	/// Bakes the selected model entities (cycler / cycler_sprite with a .mdl) into one or more
	/// combined .mdl files, and places one combined entity per file at the selection's pivot.
	/// The original entities are left in place.
	/// </summary>
	[AutoTranslate]
	[Export(typeof(ICommand))]
	[CommandID("Tools:CombineModels")]
	[MenuItem("Tools", "", "CombineModels", "")]
	public class CombineModelsCommand : BaseCommand
	{
		public override string Name { get; set; } = "Combine models";
		public override string Details { get; set; } = "Bake selected model entities into one combined .mdl";

		private string _lastDirectory;

		protected async override Task Invoke(MapDocument document, CommandParameters parameters)
		{
			var entities = document.Selection.OfType<MapEntity>().Where(IsModelEntity).ToList();
			if (entities.Count < 2)
			{
				MessageBox.Show("Select at least two model entities (cycler or cycler_sprite using a .mdl) to combine.",
					"Combine models", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			var env = document.Environment;
			var baseDir = env.BaseDirectory;

			// Pivot: the average of the selected entities' origins. Members are baked relative to it.
			var origins = entities.Select(EntityOrigin).ToList();
			var pivot = new Vector3(origins.Average(o => o.X), origins.Average(o => o.Y), origins.Average(o => o.Z));

			var models = new Dictionary<string, MdlFile>(StringComparer.InvariantCultureIgnoreCase);
			var sources = new Dictionary<string, byte[]>(StringComparer.InvariantCultureIgnoreCase);
			var prepared = new List<PreparedMember>();
			var missing = new List<string>();

			foreach (var e in entities)
			{
				var data = e.EntityData;
				var modelName = data.Get<string>("model", null).Replace('\\', '/');
				var file = env.Root.TraversePath(modelName);
				if (file == null || !file.Exists) { missing.Add(modelName); continue; }

				if (!models.TryGetValue(modelName, out var mdl))
				{
					mdl = MdlFile.FromFile(file);
					models[modelName] = mdl;
					sources[modelName] = ReadAllBytes(file);
				}

				var body = data.Get<int>("body", 0);
				var skin = data.Get<int>("skin", 0);
				var placement = new Placement
				{
					ModelPath = modelName,
					Name = Shorten(Path.GetFileNameWithoutExtension(modelName), 63),
					Origin = EntityOrigin(e) - pivot,
					Angles = data.GetVector3("angles") ?? Vector3.Zero,
					Body = body,
					Skin = skin,
					RenderMode = data.Get<int>("rendermode", 0),
					RenderAmt = data.Get<int>("renderamt", 255),
					RenderFx = data.Get<int>("renderfx", 0),
					RenderColor = data.GetVector3("rendercolor") ?? Vector3.Zero,
				};

				var local = BindPoseExtractor.Extract(mdl, body, skin);
				var placed = PlacementMath.Place(local, placement.Origin, placement.Angles);
				prepared.Add(new PreparedMember
				{
					Placement = placement,
					Mesh = placed,
					SrcTex = placed.TextureIndices.Distinct().ToList(),
					UniqueVertices = placed.Vertices.Select(v => v.Position).Distinct().Count(),
				});
			}

			if (prepared.Count < 2)
			{
				MessageBox.Show("Fewer than two of the selected models could be loaded.\n" + string.Join("\n", missing),
					"Combine models", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			List<CombinedModelPlan> plans;
			try
			{
				plans = CombinePlanner.Plan(prepared);
			}
			catch (InvalidOperationException ex)
			{
				MessageBox.Show(ex.Message, "Combine models", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			// Each combined model must be saved inside the game folder so the entity can reference it.
			var dialog = new FolderBrowserDialog
			{
				Description = $"Choose the folder for the combined model(s). It must be inside {baseDir}",
				SelectedPath = string.IsNullOrEmpty(_lastDirectory) ? baseDir : _lastDirectory,
			};
			if (dialog.ShowDialog() != DialogResult.OK) return;
			_lastDirectory = dialog.SelectedPath;

			var outputs = new List<(string modelName, string fullPath, CombinedModelPlan plan)>();
			for (int i = 0; i < plans.Count; i++)
			{
				var fileName = $"combined_{Shorten(plans[i].Members[0].Placement.Name, 40)}_{i:00}.mdl";
				var fullPath = Path.Combine(dialog.SelectedPath, fileName);
				var relative = Path.GetRelativePath(baseDir, fullPath).Replace('\\', '/');
				if (relative.StartsWith("..") || Path.IsPathRooted(relative))
				{
					MessageBox.Show($"'{fullPath}' is outside the game folder '{baseDir}'.", "Combine models",
						MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}
				outputs.Add((relative, fullPath, plans[i]));
			}

			foreach (var (modelName, fullPath, plan) in outputs)
			{
				var members = plan.Members.Select(m => new CombineMember
				{
					Path = m.Placement.ModelPath,
					Name = m.Placement.Name,
					Src = models[m.Placement.ModelPath],
					SourceBytes = sources[m.Placement.ModelPath],
					Mesh = m.Mesh,
					SrcTex = m.SrcTex,
				}).ToList();
				File.WriteAllBytes(fullPath, CombineWriter.Build(members, Path.GetFileName(fullPath)));
			}

			// One combined entity per file, at the pivot, with the render settings shared by its members.
			var classNames = entities.Select(e => e.EntityData.Name.ToLowerInvariant()).Distinct().ToList();
			var className = classNames.Count == 1 ? classNames[0] : "cycler";
			foreach (var (modelName, fullPath, plan) in outputs)
			{
				var first = plan.Members[0].Placement;
				var entity = new MapEntity(document.Map.NumberGenerator.Next("MapObject"))
				{
					Data =
					{
						new EntityData { Name = className },
						new ObjectColor(Colour.GetDefaultEntityColour()),
						new Origin(pivot),
					},
					IsSelected = false,
				};
				entity.EntityData.Set("model", modelName);
				if (first.RenderMode != 0) entity.EntityData.Set("rendermode", first.RenderMode);
				if (first.RenderAmt != 255) entity.EntityData.Set("renderamt", first.RenderAmt);
				if (first.RenderFx != 0) entity.EntityData.Set("renderfx", first.RenderFx);
				if (first.RenderColor != Vector3.Zero)
					entity.EntityData.Set("rendercolor", string.Format(System.Globalization.CultureInfo.InvariantCulture,
						"{0} {1} {2}", first.RenderColor.X, first.RenderColor.Y, first.RenderColor.Z));

				await MapDocumentOperation.Perform(document, new Attach(document.Map.Root.ID, entity));
			}

			var names = string.Join("\n", outputs.Select(o => o.modelName));
			MessageBox.Show($"Wrote {outputs.Count} combined model(s):\n{names}\n\nThe original entities were left in place.",
				"Combine models", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private static bool IsModelEntity(MapEntity e)
		{
			var data = e.EntityData;
			if (data == null) return false;
			var name = data.Name?.ToLowerInvariant();
			if (name != "cycler" && name != "cycler_sprite") return false;
			var model = data.Get<string>("model", null);
			return !string.IsNullOrWhiteSpace(model) && model.EndsWith(".mdl", StringComparison.InvariantCultureIgnoreCase);
		}

		private static Vector3 EntityOrigin(MapEntity e)
		{
			return e.Data.GetOne<Origin>()?.Location ?? e.BoundingBox.Center;
		}

		private static string Shorten(string s, int max)
		{
			return s.Length <= max ? s : s.Substring(0, max);
		}

		private static byte[] ReadAllBytes(IFile file)
		{
			using (var stream = file.Open())
			using (var ms = new MemoryStream())
			{
				stream.CopyTo(ms);
				return ms.ToArray();
			}
		}
	}
}
