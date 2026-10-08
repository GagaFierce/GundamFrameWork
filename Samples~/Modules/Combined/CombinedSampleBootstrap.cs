using System;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Input;
using WFrameWork.UI;

namespace WFrameWork.Samples.Combined
{
    /// <summary>Business input bridge only; service creation is owned by CombinedSampleRuntimeServices.</summary>
    public sealed class CombinedSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private CombinedSampleRuntimeServices services;
        private bool _ready;

        private void Start() { _ = StartAsync(); }

        private async Task StartAsync()
        {
            if (services == null) services = FindObjectOfType<CombinedSampleRuntimeServices>();
            if (services == null) { Debug.LogError("CombinedSampleRuntimeServices is required.", this); return; }
            try { await services.ReadyTask; _ready = true; }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private void Update()
        {
            if (!_ready || services == null || !services.IsReady) return;
            InputService input = services.Input;
            UiPanelManagerBehaviour ui = services.UiBehaviour;
            if (input == null || ui == null) return;
            if (input.Snapshot.IsPressed(new InputActionId("Gameplay.OpenSettings")))
                _ = OpenSettingsAsync(ui);
            if (input.Snapshot.IsPressed(new InputActionId("UI.Back")))
                _ = ui.CloseTopModalAsync();
            if (input.Snapshot.IsPressed(new InputActionId("Gameplay.Return")))
                _ = ReturnToMenuAsync();
        }

        private async Task ReturnToMenuAsync()
        {
            try { await services.ReturnToMenuAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private static async Task OpenSettingsAsync(WFrameWork.UI.Unity.UiPanelManagerBehaviour ui)
        {
            try { await ui.OpenAsync(new UiPanelId("Settings")); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void OnDestroy() { _ready = false; }
    }
}
