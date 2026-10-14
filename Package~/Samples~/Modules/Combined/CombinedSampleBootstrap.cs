using System;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Input;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

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
                _ = Observe(services.OpenPauseOrSettingsAsync());
            else if (input.Snapshot.IsPressed(new InputActionId("UI.Back")))
                _ = Observe(services.BackAsync());
            if (input.Snapshot.IsPressed(new InputActionId("Gameplay.Return")))
                _ = Observe(services.ConfirmReturnAsync());
            if (input.Snapshot.IsPressed(new InputActionId("Gameplay.Success"))) _ = Observe(services.ShowResultAsync(true));
            if (input.Snapshot.IsPressed(new InputActionId("Gameplay.Failure"))) _ = Observe(services.ShowResultAsync(false));
        }

        private void OnDestroy() { _ready = false; }
        private static async Task Observe(Task task)
        { try { await task; } catch (OperationCanceledException) { } catch (Exception error) { Debug.LogException(error); } }
    }
}
