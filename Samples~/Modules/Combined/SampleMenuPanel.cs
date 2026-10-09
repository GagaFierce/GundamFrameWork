using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    /// <summary>Menu View: serialized controls only; state and actions live in LobbyViewModel.</summary>
    public sealed class SampleMenuPanel : UiPanelViewBehaviour<LobbyViewModel>
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private Button aboutButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text errorText;
        [SerializeField] private GameObject busyIndicator;

        protected override LobbyViewModel CreateViewModel(object argument)
        {
            var services = argument as CombinedSampleRuntimeServices;
            if (services == null) throw new System.InvalidOperationException("CombinedSampleRuntimeServices is required by the sample menu.");
            return new LobbyViewModel(services, services, services, services.IsReady, services);
        }

        protected override void Bind(LobbyViewModel viewModel, UiBindingSet bindings)
        {
            if (startButton != null) bindings.Add(UiControlBindings.Button(startButton, viewModel.StartCommand, busyIndicator));
            if (settingsButton != null) bindings.Add(UiControlBindings.Button(settingsButton, viewModel.SettingsCommand));
            if (helpButton != null) bindings.Add(UiControlBindings.Button(helpButton, viewModel.HelpCommand));
            if (aboutButton != null) bindings.Add(UiControlBindings.Button(aboutButton, viewModel.AboutCommand));
            if (exitButton != null) bindings.Add(UiControlBindings.Button(exitButton, viewModel.ExitCommand));
            if (statusText != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Status), () => viewModel.Status, statusText));
            if (errorText != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Error), () => viewModel.Error, errorText));
        }
    }
}
