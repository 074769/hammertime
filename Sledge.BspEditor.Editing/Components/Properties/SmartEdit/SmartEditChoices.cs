using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Forms;
using Sledge.BspEditor.Documents;
using Sledge.Common.Logging;
using Sledge.DataStructures.GameData;

namespace Sledge.BspEditor.Editing.Components.Properties.SmartEdit
{
    [Export(typeof(IObjectPropertyEditor))]
    public class SmartEditChoices : SmartEditControl
    {
        private readonly ComboBox _comboBox;
        private readonly SoundPreviewPanel _preview;

        public SmartEditChoices()
        {
            _comboBox = new ComboBox { Width = 250 };
            _comboBox.TextChanged += (sender, e) =>
            {
                _preview.StopPlaying();
                OnValueChanged();
                UpdatePreviewState();
            };
            Controls.Add(_comboBox);
            SetFlowBreak(_comboBox, true);

            // Preview button + volume slider, only shown for choices that select a built-in sound
            _preview = new SoundPreviewPanel { Visible = false };
            _preview.PreviewClicked += PreviewClicked;
            Controls.Add(_preview);
        }

        private string CurrentKeyName => Property?.Name ?? OriginalName;

        private void UpdatePreviewState()
        {
            var supported = Property != null && EntitySoundChoices.Supports(ClassName, CurrentKeyName);
            _preview.Visible = supported;
            if (!supported)
            {
                _preview.PreviewEnabled = false;
                return;
            }

            var value = GetValue();
            if (EntitySoundChoices.IsAmbientPreset(ClassName, CurrentKeyName))
            {
                int preset;
                _preview.PreviewEnabled = Int32.TryParse(value, out preset) && preset > 0 && preset <= AmbientPresetSimulator.PresetCount;
            }
            else
            {
                string path;
                _preview.PreviewEnabled = EntitySoundChoices.TryGetSound(ClassName, CurrentKeyName, value, out path);
            }
        }

        private void PreviewClicked(object sender, EventArgs e)
        {
            try
            {
                var value = GetValue();
                byte[] source;
                byte[] wav;
                double seconds;

                if (EntitySoundChoices.IsAmbientPreset(ClassName, CurrentKeyName))
                {
                    int preset;
                    if (!Int32.TryParse(value, out preset)) return;

                    // The presets modulate the entity's own sound ("WAV Name" / message)
                    var message = GetSiblingValue?.Invoke("message");
                    if (String.IsNullOrWhiteSpace(message))
                    {
                        _preview.Status = "Set the WAV Name (message) first, the preset is applied to that sound.";
                        return;
                    }

                    var envelope = AmbientPresetSimulator.Simulate(preset);
                    if (envelope == null) return;

                    source = SoundFileLoader.Load(Document, message);
                    wav = WavPreviewRenderer.Render(source, _preview.Volume, envelope, true);
                    seconds = envelope.Duration;
                    _preview.Status = "Simulated from the engine's preset, sound looped.";
                }
                else
                {
                    string path;
                    if (!EntitySoundChoices.TryGetSound(ClassName, CurrentKeyName, value, out path)) return;

                    source = SoundFileLoader.Load(Document, path);
                    wav = WavPreviewRenderer.Render(source, _preview.Volume, null, false);
                    seconds = (wav.Length - 44) / (double) Math.Max(1, BitConverter.ToInt32(wav, 28));
                    _preview.Status = path;
                }

                SoundPreviewPlayer.Play(wav);
                _preview.BeginPlaying(seconds);
            }
            catch (Exception ex)
            {
                _preview.Status = ex.Message;
                Log.Error(nameof(SmartEditChoices), "Unable to preview sound", ex);
            }
        }

        public override string PriorityHint => "H";

        public override bool SupportsType(VariableType type)
        {
            return type == VariableType.Choices;
        }

        protected override string GetName()
        {
            return OriginalName;
        }

        protected override string GetValue()
        {
            if (Property != null)
            {
                var opt = Property.Options.FirstOrDefault(x => x.Description == _comboBox.Text);
                if (opt != null) return opt.Key;
                opt = Property.Options.FirstOrDefault(x => x.Key == _comboBox.Text);
                if (opt != null) return opt.Key;
            }
            return _comboBox.Text;
        }

        private IEnumerable<Option> GetSortedOptions()
        {
            int key;
            if (Property.Options.All(x => int.TryParse(x.Key, out key)))
            {
                return Property.Options.OrderBy(x => int.Parse(x.Key));
            }
            var emptyExist = Property.Options.FirstOrDefault(x => String.IsNullOrEmpty(x.Key)) != null;
			return Property.Options.DistinctBy(x => x.Key).Where(x => x.Key != "0" || (x.Key == "0" && !emptyExist)).OrderBy(x => x.Key.ToLowerInvariant());
		}

        protected override void OnSetProperty(MapDocument document)
        {
            _preview.StopPlaying();
            _comboBox.Items.Clear();
            if (Property != null)
            {
                var options = GetSortedOptions().ToList();
                _comboBox.Items.AddRange(options.Select(x => x.DisplayText()).OfType<object>().ToArray());
                var index = options.FindIndex(x => String.Equals(x.Key, PropertyValue, StringComparison.InvariantCultureIgnoreCase));
                if (index >= 0)
                {
                    _comboBox.SelectedIndex = index;
                    _preview.Status = "";
                    UpdatePreviewState();
                    return;
                }
            }
            _comboBox.Text = PropertyValue;
            _preview.Status = "";
            UpdatePreviewState();
        }
    }
}