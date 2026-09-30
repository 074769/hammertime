using System;
using System.Drawing;
using System.Windows.Forms;

namespace Sledge.BspEditor.Providers
{
	/// <summary>
	/// Small options dialog shown after the user picks an .obj file name.
	/// </summary>
	public class ObjExportOptionsForm : Form
	{
		// Remember the last choices for the rest of the session
		private static readonly ObjExportOptions Last = new ObjExportOptions();

		private readonly CheckBox _selectedOnly;
		private readonly ComboBox _axis;
		private readonly CheckBox _zeroOrigin;

		public ObjExportOptions Options { get; private set; }

		private ObjExportOptionsForm(bool hasSelection)
		{
			Text = "OBJ Export Options";
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterParent;
			MinimizeBox = false;
			MaximizeBox = false;
			ShowInTaskbar = false;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			Padding = new Padding(12);

			var layout = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				Dock = DockStyle.Fill
			};

			_selectedOnly = new CheckBox
			{
				Text = hasSelection ? "Export selected objects only" : "Export selected objects only (nothing is selected)",
				AutoSize = true,
				Enabled = hasSelection,
				Checked = hasSelection && Last.SelectedOnly,
				Margin = new Padding(3, 3, 3, 8)
			};

			var axisLabel = new Label { Text = "Axis conversion:", AutoSize = true, Margin = new Padding(3, 3, 3, 2) };
			_axis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(3, 0, 3, 8) };
			_axis.Items.Add("None (keep editor axes, Z-up)");
			_axis.Items.Add("Blender (Y-up, -Z forward: default OBJ import)");
			_axis.Items.Add("3ds Max (Z-up: import with \"Flip ZY-axis\" off)");
			_axis.SelectedIndex = (int) Last.Axis;

			_zeroOrigin = new CheckBox
			{
				Text = "Zero out the origin (centre the geometry on 0, 0, 0)",
				AutoSize = true,
				Checked = Last.ZeroOrigin,
				Margin = new Padding(3, 3, 3, 12)
			};

			var ok = new Button { Text = "Export", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
			var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 26) };
			var buttons = new FlowLayoutPanel
			{
				FlowDirection = FlowDirection.RightToLeft,
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				Dock = DockStyle.Fill,
				Margin = new Padding(0)
			};
			buttons.Controls.Add(cancel);
			buttons.Controls.Add(ok);

			layout.Controls.Add(_selectedOnly);
			layout.Controls.Add(axisLabel);
			layout.Controls.Add(_axis);
			layout.Controls.Add(_zeroOrigin);
			layout.Controls.Add(buttons);
			Controls.Add(layout);

			AcceptButton = ok;
			CancelButton = cancel;
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (DialogResult == DialogResult.OK)
			{
				Options = new ObjExportOptions
				{
					SelectedOnly = _selectedOnly.Enabled && _selectedOnly.Checked,
					Axis = (ObjAxisMode) Math.Max(0, _axis.SelectedIndex),
					ZeroOrigin = _zeroOrigin.Checked
				};

				Last.SelectedOnly = Options.SelectedOnly;
				Last.Axis = Options.Axis;
				Last.ZeroOrigin = Options.ZeroOrigin;
			}
			base.OnFormClosing(e);
		}

		/// <summary>
		/// Show the dialog. Returns null if the user cancelled.
		/// </summary>
		public static ObjExportOptions Ask(IWin32Window owner, bool hasSelection)
		{
			using (var form = new ObjExportOptionsForm(hasSelection))
			{
				return form.ShowDialog(owner) == DialogResult.OK ? form.Options : null;
			}
		}
	}
}
