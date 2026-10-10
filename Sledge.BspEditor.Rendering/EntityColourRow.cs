using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sledge.BspEditor.Rendering
{
    /// <summary>
    /// A single custom entity colour override row: remove/close button on the left,
    /// entity name with an autocomplete of common FGD entity classes, and a colour
    /// swatch on the right.
    /// </summary>
    public partial class EntityColourRow : UserControl
    {
        public event EventHandler RemoveRequested;
        public event EventHandler ColourChanged;
        public event EventHandler NameChanged;

        public EntityColourRow()
        {
            InitializeComponent();
            ColorPanel.Click += ColorPanel_Click;
            RemoveButton.Click += RemoveButton_Click;
            EntityNameTextBox.TextChanged += EntityNameTextBox_TextChanged;

            // Autocomplete of common entity class names so users match the right FGD class.
            EntityNameTextBox.AutoCompleteMode = AutoCompleteMode.Suggest;
            EntityNameTextBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            EntityNameTextBox.AutoCompleteCustomSource = EntityNameSuggestions;
        }

        private static readonly AutoCompleteStringCollection EntityNameSuggestions = CreateEntityNameSuggestions();

        private static AutoCompleteStringCollection CreateEntityNameSuggestions()
        {
            var c = new AutoCompleteStringCollection();
            var names = new[]
            {
                "info_player_start", "info_player_deathmatch", "info_player_coop",
                "info_player_teamspawn", "info_target", "info_teleport_destination",
                "info_node", "info_node_hint", "info_landmark", "info_intermission",
                "func_door", "func_door_rotating", "func_breakable", "func_push",
                "func_train", "func_plat", "func_illusionary", "func_wall", "func_illusionary",
                "trigger_multiple", "trigger_once", "trigger_teleport", "trigger_hurt",
                "trigger_push", "trigger_gravity", "trigger_counter", "trigger_relay",
                "trigger_script", "trigger_changelevel", "trigger_transition",
                "env_fade", "env_fog_controller", "env_message", "env_spark",
                "env_sprite", "env_soundscape", "env_tonemapcontroller", "env_wind",
                "env_rain", "env_funnel", "env_shake", "env_laser",
                "path_track", "path_corner", "path_node", "path_corner_hint",
                "env_blood", "env_bloodprint",
                "item_health", "item_battery", "item_ammo_pistol", "item_ammo_357",
                "item_ammo_AR", "item_ammo_crossbow", "item_ammo_rpg_round",
                "item_armorvest", "item_suit", "item_suitcharger",
                "weapon_crowbar", "weapon_9mmhandgun", "weapon_357", "weapon_shotgun",
                "weapon_9mmAR", "weapon_crossbow", "weapon_gauss", "weapon_egon",
                "weapon_rpg", "weapon_satchel", "weapon_tripmine", "weapon_snark",
                "monster_barney", "monster_scientist", "monster_alien_grunt",
                "monster_alien_slave", "monster_headcrab", "monster_zombie",
                "monster_bullchicken", "monster_nihilanth", "monster_gargantua",
                "monster_babycrab", "monster_zombie_barney", "monster_scientist",
                "npc_player_template_maker"
            };
            foreach (var n in names.Distinct()) c.Add(n);
            return c;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EntityName
        {
            get => EntityNameTextBox.Text;
            set => EntityNameTextBox.Text = value;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Colour
        {
            get => ColorPanel.BackColor;
            set => ColorPanel.BackColor = Color.FromArgb(255, value.R, value.G, value.B); // saved colours can have alpha 0 (blank swatch)
        }

        private void ColorPanel_Click(object sender, EventArgs e)
        {
            using (var cp = new ColorDialog { Color = ColorPanel.BackColor, SolidColorOnly = true })
            {
                if (cp.ShowDialog() == DialogResult.OK)
                {
                    ColorPanel.BackColor = cp.Color;
                    ColourChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            RemoveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EntityNameTextBox_TextChanged(object sender, EventArgs e)
        {
            NameChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}