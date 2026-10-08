using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleMenuPanel : MonoBehaviour, IUGuiPanelLifecycle
    {
        [SerializeField] private Button startButton;
        private CombinedSampleRuntimeServices _services;
        public bool RequiresContinuousUpdate => false;

        public void OnPanelOpened(object argument)
        {
            _services = FindObjectOfType<CombinedSampleRuntimeServices>();
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        }

        public void OnPanelShown() { }
        public void OnPanelHidden() { }
        public void OnPanelClosed() { if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked); }
        public void OnPanelUpdate(in UiPanelUpdateContext context) { }

        private void OnStartClicked() { _ = StartAsync(); }

        private async Task StartAsync()
        {
            try { if (_services != null) await _services.StartGameAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
