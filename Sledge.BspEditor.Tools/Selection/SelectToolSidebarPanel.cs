using System;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.BspEditor.Primitives.MapData;
using Sledge.Common.Shell.Commands;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Translations;
using Sledge.Shell;
using Sledge.BspEditor.Documents;
using Sledge.Common.Shell.Documents;

namespace Sledge.BspEditor.Tools.Selection
{
	[Export(typeof(ISidebarComponent))]
	[OrderHint("F")]
	[AutoTranslate]
	public partial class SelectToolSidebarPanel : UserControl, ISidebarComponent, IManualTranslate
	{
		public string Title { get; set; } = "Selection Tool";
		public object Control => this;

		private WeakReference<MapDocument> _activeDocument;
		private bool _updatingWidgetToggles;

		public SelectToolSidebarPanel()
		{
			InitializeComponent();

			Oy.Subscribe<String>("SelectTool:TransformationModeChanged", x =>
			{
				if (Enum.TryParse(x, out SelectionBoxDraggableState.TransformationMode mode))
				{
					TransformationToolChanged(mode);
				}
			});

			Oy.Subscribe<String>("SelectTool:SetShow3DWidgets", x =>
			{
				Show3DWidgetsCheckbox.Checked = x == "1";
			});

			Oy.Subscribe<String>("SelectTool:SetWidgetToggles", x =>
			{
				if (string.IsNullOrEmpty(x) || x.Length < 3) return;
				_updatingWidgetToggles = true;
				try
				{
					MoveWidgetCheckbox.Checked = x[0] == '1';
					RotateWidgetCheckbox.Checked = x[1] == '1';
					EntityMotionWidgetCheckbox.Checked = x[2] == '1';
				}
				finally
				{
					_updatingWidgetToggles = false;
				}
			});
			Oy.Subscribe<IDocument>("Document:Activated", DocumentActivated);
		}

		public void Translate(ITranslationStringProvider strings)
		{
			CreateHandle();
			var prefix = GetType().FullName;
			this.InvokeLater(() =>
			{
				Title = strings.GetString(prefix, "Title");
				lblMode.Text = strings.GetString(prefix, "Mode");
				TranslateModeCheckbox.Text = strings.GetString(prefix, "Translate");
				RotateModeCheckbox.Text = strings.GetString(prefix, "Rotate");
				SkewModeCheckbox.Text = strings.GetString(prefix, "Skew");
				Show3DWidgetsCheckbox.Text = strings.GetString(prefix, "Show3DWidgets");
				MoveWidgetCheckbox.Text = strings.GetString(prefix, "MoveWidget") ?? MoveWidgetCheckbox.Text;
				RotateWidgetCheckbox.Text = strings.GetString(prefix, "RotateWidget") ?? RotateWidgetCheckbox.Text;
				EntityMotionWidgetCheckbox.Text = strings.GetString(prefix, "EntityMotionWidget") ?? EntityMotionWidgetCheckbox.Text;
				lblActions.Text = strings.GetString(prefix, "Actions");
				MoveToWorldButton.Text = strings.GetString(prefix, "MoveToWorld");
				MoveToEntityButton.Text = strings.GetString(prefix, "TieToEntity");
			});
		}

		public bool IsInContext(IContext context)
		{
			return context.TryGet("ActiveTool", out SelectTool _);
		}

		private SelectionBoxDraggableState.TransformationMode _selectedType;

		public void TransformationToolChanged(SelectionBoxDraggableState.TransformationMode tt)
		{
			_selectedType = tt;
			SetCheckState();
		}
		private delegate void SetCheckStateCallback(CheckBox checkBox, bool state);
		private void SetCheckState(CheckBox checkBox, bool state)
		{
			if (checkBox.InvokeRequired)
			{
				SetCheckStateCallback callback = new SetCheckStateCallback(SetCheckState);
				this.Invoke(callback, new object[] { checkBox, state });
			}
			else
			{
				checkBox.Checked = state;
			}
		}

		private void SetCheckState()
		{
			if (_selectedType == SelectionBoxDraggableState.TransformationMode.Resize)
			{
				SetCheckState(RotateModeCheckbox, false);
				SetCheckState(SkewModeCheckbox, false);
				SetCheckState(TranslateModeCheckbox, true);
			}
			else if (_selectedType == SelectionBoxDraggableState.TransformationMode.Rotate)
			{
				SetCheckState(TranslateModeCheckbox, false);
				SetCheckState(SkewModeCheckbox, false);
				SetCheckState(RotateModeCheckbox, true);
			}
			else if (_selectedType == SelectionBoxDraggableState.TransformationMode.Skew)
			{
				SetCheckState(RotateModeCheckbox, false);
				SetCheckState(TranslateModeCheckbox, false);
				SetCheckState(SkewModeCheckbox, true);
			}
		}

		private void TranslateModeChecked(object sender, EventArgs e)
		{
			if (TranslateModeCheckbox.Checked && _selectedType != SelectionBoxDraggableState.TransformationMode.Resize)
				Oy.Publish("SelectTool:TransformationModeChanged", "Resize");
			else
				SetCheckState();
		}

		private void RotateModeChecked(object sender, EventArgs e)
		{
			if (RotateModeCheckbox.Checked && _selectedType != SelectionBoxDraggableState.TransformationMode.Rotate)
				Oy.Publish("SelectTool:TransformationModeChanged", "Rotate");
			else
				SetCheckState();
		}

		private void SkewModeChecked(object sender, EventArgs e)
		{
			if (SkewModeCheckbox.Checked && _selectedType != SelectionBoxDraggableState.TransformationMode.Skew)
				Oy.Publish("SelectTool:TransformationModeChanged", "Skew");
			else
				SetCheckState();
		}

		private void Show3DWidgetsChecked(object sender, EventArgs e)
		{
			Oy.Publish("SelectTool:Show3DWidgetsChanged", Show3DWidgetsCheckbox.Checked ? "1" : "0");

			// The individual widget toggles only make sense while 3D widgets are on
			MoveWidgetCheckbox.Enabled = RotateWidgetCheckbox.Enabled = EntityMotionWidgetCheckbox.Enabled = Show3DWidgetsCheckbox.Checked;
		}

		private void WidgetToggleChecked(object sender, EventArgs e)
		{
			if (_updatingWidgetToggles) return;
			Oy.Publish("SelectTool:WidgetTogglesChanged",
				(MoveWidgetCheckbox.Checked ? "1" : "0") +
				(RotateWidgetCheckbox.Checked ? "1" : "0") +
				(EntityMotionWidgetCheckbox.Checked ? "1" : "0"));
		}

		private void MoveToWorldButtonClicked(object sender, EventArgs e)
		{
			Oy.Publish("Command:Run", new CommandMessage("BspEditor:Tools:MoveToWorld"));
		}

		private void TieToEntityButtonClicked(object sender, EventArgs e)
		{
			Oy.Publish("Command:Run", new CommandMessage("BspEditor:Tools:TieToEntity"));
		}
		private void KeepEntityAngleChecked(object sender, EventArgs e)
		{
			_activeDocument.TryGetTarget(out var doc);
			var flags = doc.Map.Data.GetOne<TransformationFlags>() ?? new TransformationFlags();
			flags.KeepRotationAngle = keepEntityAngle.Checked;
			doc.Map.Data.Replace(flags);
		}
		private void DocumentActivated(IDocument document)
		{
			var md = document as MapDocument;

			_activeDocument = new WeakReference<MapDocument>(md);
		}
	}
}
