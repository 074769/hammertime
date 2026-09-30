using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Shell.Hotkeys;
using Sledge.Common.Shell.Settings;
using Sledge.Shell.Forms;
using Sledge.Shell.Input;
using Sledge.Shell.Registers;
using Sledge.Shell.Settings;

namespace Sledge.Shell.Settings.Editors
{
	public partial class HotkeysEditor : UserControl, ISettingEditor
	{
		public event EventHandler<SettingKey> OnValueChanged;

		string ISettingEditor.Label { get; set; }

		private HotkeyRegister.HotkeyBindings _bindings;
		private static bool _useDarkMode;

		public object Value
		{
			get => _bindings;
			set
			{
				_bindings = ((HotkeyRegister.HotkeyBindings)value).Clone();
				ApplySavedColumnWidths();
				UpdateHotkeyList();
			}
		}

		public object Control => this;
		public SettingKey Key { get; set; }

		public HotkeysEditor()
		{
			InitializeComponent();

			// Action and Description use the user's saved widths, Hotkey always fills the remaining space
			ApplySavedColumnWidths();
			HotkeyList.ColumnWidthChanged += HotkeyList_ColumnWidthChanged;
			HotkeyList.SizeChanged += (s, e) => FitLastColumn();

			Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
			Oy.Subscribe<bool>("Theme:Changed", (useDark) => UseDarkTheme(useDark));

		}

		private bool _updating;

		private string GetBinding(IHotkey hotkey)
		{
			return _bindings.ContainsKey(hotkey.ID) ? _bindings[hotkey.ID] : hotkey.DefaultHotkey;
		}

		/// <summary>
		/// Rebuilds the list and the action dropdown (used after bindings change).
		/// </summary>
		private void UpdateHotkeyList()
		{
			RebuildList();

			var register = BaseForm.HotkeyRegister;
			HotkeyActionList.BeginUpdate();
			var idx = HotkeyActionList.SelectedIndex;
			HotkeyActionList.Items.Clear();
			foreach (var hotkey in register.GetHotkeys().OrderBy(x => x.Name))
			{
				HotkeyActionList.Items.Add(new HotkeyWrapper(hotkey));
			}
			if (idx < 0 || idx >= HotkeyActionList.Items.Count) idx = HotkeyActionList.Items.Count - 1;
			HotkeyActionList.SelectedIndex = idx;
			HotkeyActionList.EndUpdate();
		}

		/// <summary>
		/// Rebuilds only the (filtered) list. Does not touch the action dropdown or the hotkey box.
		/// </summary>
		private void RebuildList()
		{
			// Technically a hack, but meh we're going to be using a singleton here anyway because we're lazy.
			// The whole thing's internal so stop judging me
			var register = BaseForm.HotkeyRegister;

			var prevSelected = HotkeyList.SelectedItems.Count == 0 ? null : HotkeyList.SelectedItems[0].Tag as IHotkey;
			var filter = FilterBox.Text;

			_updating = true;
			HotkeyList.BeginUpdate();
			try
			{
				HotkeyList.Items.Clear();
				var hotkeys = FilterHotkeys(register.GetHotkeys(), filter)
					.OrderByDescending(x => IsExactBinding(GetBinding(x), filter))
					.ThenBy(x => x.Name);
				foreach (var hotkey in hotkeys)
				{
					HotkeyList.Items.Add(new ListViewItem(new[] { hotkey.Name ?? "", hotkey.Description ?? "", GetBinding(hotkey) ?? "" }) { Tag = hotkey });
				}

				if (HotkeyList.Items.Count > 0)
				{
					ListViewItem toSelect = null;
					if (prevSelected != null) toSelect = HotkeyList.Items.Cast<ListViewItem>().FirstOrDefault(x => Equals(((IHotkey)x.Tag).ID, prevSelected.ID));
					if (toSelect == null && prevSelected == null && String.IsNullOrWhiteSpace(filter)) toSelect = HotkeyList.Items[0];
					if (toSelect != null) toSelect.Selected = true;
				}
			}
			finally
			{
				HotkeyList.EndUpdate();
				_updating = false;
			}
		}

		private static string NormaliseHotkey(string s)
		{
			if (String.IsNullOrWhiteSpace(s)) return "";
			return s.Replace(" ", "")
				.Replace("Control", "Ctrl", StringComparison.InvariantCultureIgnoreCase)
				.Replace("Ctl+", "Ctrl+", StringComparison.InvariantCultureIgnoreCase)
				.ToLowerInvariant();
		}

		private static bool IsExactBinding(string binding, string filter)
		{
			var f = NormaliseHotkey(filter);
			return f.Length > 0 && NormaliseHotkey(binding) == f;
		}

		// Matches "ctrl+b", "b+ctrl", "CTRL + B", and partial input such as "ctrl+" or "ctrl+sh"
		private static bool IsBindingMatch(string binding, string filter)
		{
			var b = NormaliseHotkey(binding);
			var f = NormaliseHotkey(filter);
			if (b.Length == 0 || f.Length == 0) return false;
			if (b.Contains(f)) return true;

			var bTokens = b.Split('+');
			var fTokens = f.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
			return fTokens.Length > 0 && fTokens.All(ft => bTokens.Any(bt => bt.StartsWith(ft, StringComparison.Ordinal)));
		}

		private IEnumerable<IHotkey> FilterHotkeys(IEnumerable<IHotkey> hotkeys, string filter)
		{
			if (String.IsNullOrWhiteSpace(filter)) return hotkeys;
			var trimmed = filter.Trim();
			return hotkeys.Where(IsMatch).ToList();

			bool IsMatch(IHotkey h) => (h.Name ?? "").IndexOf(trimmed, StringComparison.InvariantCultureIgnoreCase) >= 0 ||
									   (h.Description ?? "").IndexOf(trimmed, StringComparison.InvariantCultureIgnoreCase) >= 0 ||
									   IsBindingMatch(GetBinding(h), trimmed);
		}

		private void DeleteHotkey(IHotkey hk)
		{
			_bindings[hk.ID] = "";
			OnValueChanged?.Invoke(this, Key);
			UpdateHotkeyList();
		}

		private void HotkeyCombinationKeyDown(object sender, KeyEventArgs e)
		{
			e.SuppressKeyPress = true;
			e.Handled = true;
			HotkeyCombination.Text = KeyboardState.KeysToString(e.KeyData);
		}

		private void HotkeySetButtonClicked(object sender, EventArgs e)
		{
			var key = HotkeyCombination.Text;
			if (HotkeyActionList.SelectedIndex < 0 || String.IsNullOrWhiteSpace(key)) return;

			if (_bindings.ContainsValue(key))
			{
				// if (MessageBox.Show(key + " is already assigned to \"" + Hotkeys.GetHotkeyDefinition(conflict.ID) + "\".\n" +
				//                     "Continue anyway?", "Conflict Detected", MessageBoxButtons.YesNo) == DialogResult.No)
				// {
				//     return;
				// }
			}

			var def = ((HotkeyWrapper)HotkeyActionList.SelectedItem).Hotkey;
			_bindings[def.ID] = key;
			HotkeyCombination.Text = "";

			OnValueChanged?.Invoke(this, Key);
			UpdateHotkeyList();
		}

		private void HotkeyUnsetButtonClicked(object sender, EventArgs e)
		{
			if (HotkeyActionList.SelectedIndex < 0) return;

			var def = ((HotkeyWrapper)HotkeyActionList.SelectedItem).Hotkey;
			_bindings[def.ID] = "";
			HotkeyCombination.Text = "";

			OnValueChanged?.Invoke(this, Key);
			UpdateHotkeyList();
		}

		private void HotkeyResetButtonClicked(object sender, EventArgs e)
		{
			_bindings.Clear();
			foreach (var hk in BaseForm.HotkeyRegister.GetHotkeys())
			{
				if (!String.IsNullOrWhiteSpace(hk.DefaultHotkey))
				{
					_bindings[hk.ID] = hk.DefaultHotkey;
				}
			}
			OnValueChanged?.Invoke(this, Key);
			UpdateHotkeyList();
		}

		private void HotkeyListSelectionChanged(object sender, EventArgs e)
		{
			if (_updating) return;
			if (HotkeyList.SelectedItems.Count == 1)
			{
				var hk = (IHotkey)HotkeyList.SelectedItems[0].Tag;
				var str = _bindings.ContainsKey(hk.ID) ? _bindings[hk.ID] : "";

				HotkeyActionList.SelectedItem = new HotkeyWrapper(hk);
				HotkeyCombination.Text = str;
			}
			else
			{
				HotkeyActionList.SelectedIndex = -1;
				HotkeyCombination.Text = "";
			}
		}

		private void HotkeyListKeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Delete && HotkeyList.SelectedItems.Count == 1)
			{
				DeleteHotkey((IHotkey)HotkeyList.SelectedItems[0].Tag);
			}
		}

		private class HotkeyWrapper
		{
			public IHotkey Hotkey { get; }

			public HotkeyWrapper(IHotkey hotkey)
			{
				Hotkey = hotkey;
			}

			private bool Equals(HotkeyWrapper other)
			{
				return Equals(Hotkey, other.Hotkey);
			}

			public override bool Equals(object obj)
			{
				if (ReferenceEquals(null, obj)) return false;
				if (ReferenceEquals(this, obj)) return true;
				if (obj.GetType() != this.GetType()) return false;
				return Equals((HotkeyWrapper)obj);
			}

			public override int GetHashCode()
			{
				return (Hotkey != null ? Hotkey.GetHashCode() : 0);
			}

			public override string ToString()
			{
				return Hotkey.Name;
			}
		}

		private void UpdateFilter(object sender, EventArgs e)
		{
			RebuildList();
		}
		public void UseDarkTheme(bool dark)
		{
			_useDarkMode = dark;

			HotkeyList.ForeColor = Color.Black;

			DialogRegister.ColorControlsRecursively(this, dark);
		}

		private bool _fittingColumns;

		private void ApplySavedColumnWidths()
		{
			_fittingColumns = true;
			try
			{
				chAction.Width = HotkeyColumnSettings.ActionWidth;
				chDescription.Width = HotkeyColumnSettings.DescriptionWidth;
			}
			finally
			{
				_fittingColumns = false;
			}
			FitLastColumn();
		}

		/// <summary>
		/// Stretch the last column to the right edge of the list so it never ends before the border
		/// </summary>
		private void FitLastColumn()
		{
			if (_fittingColumns || HotkeyList.Columns.Count == 0) return;
			_fittingColumns = true;
			try
			{
				var used = 0;
				for (var i = 0; i < HotkeyList.Columns.Count - 1; i++) used += HotkeyList.Columns[i].Width;
				var last = HotkeyList.Columns[HotkeyList.Columns.Count - 1];
				last.Width = Math.Max(80, HotkeyList.ClientSize.Width - used);
			}
			finally
			{
				_fittingColumns = false;
			}
		}

		private void HotkeyList_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
		{
			if (_fittingColumns) return;

			// The user dragged a column: remember it (written to disk with the rest of the settings)
			HotkeyColumnSettings.ActionWidth = Math.Max(HotkeyColumnSettings.MinWidth, chAction.Width);
			HotkeyColumnSettings.DescriptionWidth = Math.Max(HotkeyColumnSettings.MinWidth, chDescription.Width);
			FitLastColumn();
		}

		private void HotkeyList_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
		{
			e.Graphics.DrawRectangle(Pens.LightGray, new Rectangle(new Point(e.Bounds.Location.X - 1, e.Bounds.Location.Y - 1), e.Bounds.Size));

			if (_useDarkMode)
			{
				e.Graphics.FillRectangle(Brushes.DarkGray, e.Bounds);
				e.Graphics.DrawRectangle(Pens.DimGray, e.Bounds);
			}
			e.DrawText();
		}

		private void HotkeyList_DrawItem(object sender, DrawListViewItemEventArgs e)
		{
			return;
		}

		private void HotkeyList_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
		{
			HotkeyList.ForeColor = Color.Black;

			if (_useDarkMode)
			{
				e.Graphics.FillRectangle(Brushes.Gray, e.Bounds);
				e.Graphics.DrawRectangle(Pens.DarkGray, e.Bounds);
			}
			if (e.Item.Selected)
			{
				if (_useDarkMode)
				{
					e.Graphics.FillRectangle(Brushes.DimGray, e.Bounds);
				}
				else
				{
					e.Graphics.FillRectangle(Brushes.DarkGray, e.Bounds);
				}
			}
			e.DrawText();
		}
	}
}
