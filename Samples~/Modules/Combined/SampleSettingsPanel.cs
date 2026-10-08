using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.Audio;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleSettingsPanel : MonoBehaviour, IUGuiPanelLifecycle
    {
        [SerializeField] private Slider volume;
        [SerializeField] private Toggle muted;
        [SerializeField] private Button save;
        private CombinedSampleRuntimeServices _services;

        public bool RequiresContinuousUpdate => false;

        public void OnPanelOpened(object argument)
        {
            _services = FindObjectOfType<CombinedSampleRuntimeServices>();
            if (_services?.Audio != null) { if (volume != null) volume.value = _services.Audio.GetVolume(AudioBus.Ui); if (muted != null) muted.isOn = _services.Audio.IsMuted; }
            if (save != null) save.onClick.AddListener(OnSaveClicked);
        }

        public void OnPanelShown() { }
        public void OnPanelHidden() { }
        public void OnPanelClosed() { if (save != null) save.onClick.RemoveListener(OnSaveClicked); }
        public void OnPanelUpdate(in UiPanelUpdateContext context) { }

        private void OnSaveClicked() { _ = SaveAsync(); }

        private async Task SaveAsync()
        {
            try { if (_services != null) await _services.SaveSettingsAsync(volume == null ? 1 : volume.value, muted != null && muted.isOn); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
