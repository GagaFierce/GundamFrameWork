using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SamplePausePanel : UiPanelViewBehaviour<PauseViewModel>
    {
        [SerializeField] private Button resume, settings, help, restart, lobby;
        [SerializeField] private TMP_Text error;
        protected override PauseViewModel CreateViewModel(object argument)
        {
            var services = argument as CombinedSampleRuntimeServices ?? throw new System.ArgumentException("Sample services required.");
            return new PauseViewModel(services, services, services, services);
        }
        protected override void Bind(PauseViewModel vm, UiBindingSet bindings)
        {
            if (resume != null) bindings.Add(UiControlBindings.Button(resume, vm.ContinueCommand));
            if (settings != null) bindings.Add(UiControlBindings.Button(settings, vm.SettingsCommand));
            if (help != null) bindings.Add(UiControlBindings.Button(help, vm.HelpCommand));
            if (restart != null) bindings.Add(UiControlBindings.Button(restart, vm.RestartCommand));
            if (lobby != null) bindings.Add(UiControlBindings.Button(lobby, vm.ReturnToLobbyCommand));
            if (error != null) bindings.Add(UiControlBindings.Text(vm, nameof(vm.Error), () => vm.Error, error));
        }
    }
}
