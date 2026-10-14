using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace GFramework.Starter
{
    [DisallowMultipleComponent]
    public sealed class StarterNavigation : MonoBehaviour
    {
        [SerializeField] private UiPanelManagerBehaviour panelManager;
        [SerializeField] private string initialPanelId = "Menu";
        private CancellationTokenSource _lifetime;

        public string InitialPanelId => initialPanelId;

        public void Configure(UiPanelManagerBehaviour value)
        {
            panelManager = value ?? throw new ArgumentNullException(nameof(value));
            if (_lifetime == null) _lifetime = new CancellationTokenSource();
        }

        public Task OpenInitialPanelAsync()
        {
            return OpenPanelAsync(initialPanelId, _lifetime == null ? CancellationToken.None : _lifetime.Token);
        }

        public Task OpenPanelAsync(string panelId, CancellationToken token = default(CancellationToken))
        {
            if (panelManager == null) throw new InvalidOperationException("StarterNavigation requires a panel manager.");
            if (string.IsNullOrWhiteSpace(panelId)) throw new ArgumentException("Panel id is required.", nameof(panelId));
            return panelManager.OpenAsync(new UiPanelId(panelId), cancellationToken: token);
        }

        public async void OpenPanel(string panelId)
        {
            try { await OpenPanelAsync(panelId, _lifetime == null ? CancellationToken.None : _lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public async void ClosePanel(string panelId)
        {
            try
            {
                if (panelManager == null || panelManager.Manager == null) return;
                await panelManager.Manager.CloseAsync(new UiPanelId(panelId));
            }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public async void CloseTopModal()
        {
            try { if (panelManager != null) await panelManager.CloseTopModalAsync(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public async void ShowToast()
        {
            try
            {
                await OpenPanelAsync("Toast", _lifetime == null ? CancellationToken.None : _lifetime.Token);
                await Task.Delay(TimeSpan.FromSeconds(1.5f), _lifetime == null ? CancellationToken.None : _lifetime.Token);
                if (panelManager != null && panelManager.Manager != null)
                    await panelManager.Manager.CloseAsync(new UiPanelId("Toast"));
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        internal void Shutdown()
        {
            if (_lifetime == null) return;
            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = null;
        }

        private void OnDestroy() { Shutdown(); }
    }
}
