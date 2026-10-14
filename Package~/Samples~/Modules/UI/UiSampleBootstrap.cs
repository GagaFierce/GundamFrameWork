using UnityEngine;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.UI
{
    /// <summary>
    /// Configure one UIRoot, one EventSystem and UiPanelDefinitionAsset entries in the sample
    /// scene, then add this component. Missing EventSystem is reported instead of auto-created.
    /// </summary>
    public sealed class UiSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private UiPanelManagerBehaviour panelManager;
        private FrameUpdateManager _manager;
        private UnityFrameUpdateHost _host;

        private void Awake()
        {
            if (panelManager == null) throw new System.InvalidOperationException("Assign UiPanelManagerBehaviour before starting the UI sample.");
            _manager = new FrameUpdateManager(FrameUpdateConfig.Default);
            _host = UnityFrameUpdateHost.Install(_manager);
            panelManager.Initialize(_manager, _host.Loops);
        }

        private void OnDestroy()
        {
            _host?.Dispose(); _manager?.Dispose();
        }
    }
}
