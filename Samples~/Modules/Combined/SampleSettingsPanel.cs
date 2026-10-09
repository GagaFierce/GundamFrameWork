using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    /// <summary>Settings View: two-way controls are registered explicitly and disposed with the generation.</summary>
    public sealed class SampleSettingsPanel : UiPanelViewBehaviour<SettingsViewModel>
    {
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("volume")] private Slider masterVolume;
        [SerializeField] private Slider musicVolume;
        [SerializeField] private Slider sfxVolume;
        [SerializeField] private Slider uiVolume;
        [SerializeField] private Toggle muted;
        [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("save")] private Button apply;
        [SerializeField] private Button cancel;
        [SerializeField] private Button defaults;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private GameObject busyIndicator;

        protected override SettingsViewModel CreateViewModel(object argument)
        {
            var services = argument as CombinedSampleRuntimeServices ?? FindObjectOfType<CombinedSampleRuntimeServices>();
            if (services == null) throw new System.InvalidOperationException("CombinedSampleRuntimeServices is required by settings.");
            return new SettingsViewModel(services, services);
        }

        protected override void Bind(SettingsViewModel viewModel, UiBindingSet bindings)
        {
            if (masterVolume != null) bindings.Add(UiControlBindings.Slider(viewModel, nameof(viewModel.MasterVolume), () => viewModel.MasterVolume, value => viewModel.MasterVolume = value, masterVolume));
            if (musicVolume != null) bindings.Add(UiControlBindings.Slider(viewModel, nameof(viewModel.MusicVolume), () => viewModel.MusicVolume, value => viewModel.MusicVolume = value, musicVolume));
            if (sfxVolume != null) bindings.Add(UiControlBindings.Slider(viewModel, nameof(viewModel.SfxVolume), () => viewModel.SfxVolume, value => viewModel.SfxVolume = value, sfxVolume));
            if (uiVolume != null) bindings.Add(UiControlBindings.Slider(viewModel, nameof(viewModel.UiVolume), () => viewModel.UiVolume, value => viewModel.UiVolume = value, uiVolume));
            if (muted != null) bindings.Add(UiControlBindings.Toggle(viewModel, nameof(viewModel.Muted), () => viewModel.Muted, value => viewModel.Muted = value, muted));
            if (apply != null) bindings.Add(UiControlBindings.Button(apply, viewModel.ApplyCommand, busyIndicator));
            if (cancel != null) bindings.Add(UiControlBindings.Button(cancel, viewModel.DiscardAndCloseCommand));
            if (defaults != null) bindings.Add(UiControlBindings.Button(defaults, viewModel.DefaultsCommand));
            if (errorText != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.SaveError), () => viewModel.SaveError, value => errorText.text = value ?? string.Empty));
        }
    }
}
