using System;
using UnityEngine;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.UI.Unity;

namespace GFramework.Starter
{
    [DisallowMultipleComponent]
    public sealed class StarterRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private UiPanelManagerBehaviour panelManager;
        [SerializeField] private StarterNavigation navigation;
        [SerializeField] private StarterThemeRuntime themeRuntime;
        [SerializeField] private bool openInitialPanel = true;

        private FrameUpdateManager _frameUpdate;
        private UnityFrameUpdateHost _host;
        private bool _initialized;

        public StarterThemeRuntime ThemeRuntime => themeRuntime;

        private void Awake()
        {
            if (panelManager == null) throw new InvalidOperationException("StarterRuntimeBootstrap requires a panel manager.");
            if (navigation == null) throw new InvalidOperationException("StarterRuntimeBootstrap requires navigation.");

            _frameUpdate = new FrameUpdateManager(FrameUpdateConfig.Default);
            _host = UnityFrameUpdateHost.Install(_frameUpdate);
            panelManager.Initialize(_frameUpdate, _host.Loops);
            navigation.Configure(panelManager);
            _initialized = true;
        }

        private async void Start()
        {
            if (!_initialized || !openInitialPanel) return;
            try { await navigation.OpenInitialPanelAsync(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        public void ToggleTheme()
        {
            if (themeRuntime == null) return;
            themeRuntime.ToggleTheme();
        }

        private void OnDestroy()
        {
            navigation?.Shutdown();
            if (panelManager != null && panelManager.Manager != null) panelManager.Manager.Dispose();
            if (_host != null) _host.Dispose();
            if (_frameUpdate != null) _frameUpdate.Dispose();
            _host = null;
            _frameUpdate = null;
        }
    }
}
