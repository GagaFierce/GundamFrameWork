using System;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Core.ResLoad.Unity;
using WFrameWork.Core.ResLoad;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;

namespace WFrameWork.UI.Unity
{
    [DisallowMultipleComponent]
    public sealed class UiPanelManagerBehaviour : MonoBehaviour
    {
        [SerializeField] private UIRoot root;
        [SerializeField] private UiPanelDefinitionAsset[] definitions;
        [SerializeField] private CanvasGroup modalBarrier;
        private UiPanelManager _manager;
        private AddressablesResourceService _addressables;
        private Task _addressablesInitialization;

        public UiPanelManager Manager => _manager;

        public void Initialize(FrameUpdateManager frameUpdateManager, UnityFrameUpdateLoops loops,
            IUiFocusService focus = null, IUiModalInputBlocker modalBlocker = null, ResourceService resourceService = null)
        {
            if (_manager != null) throw new InvalidOperationException("UI manager is already initialized.");
            string validationError;
            if (root == null) throw new InvalidOperationException("A UIRoot is required.");
            if (!root.TryValidate(out validationError)) throw new InvalidOperationException(validationError ?? "Invalid UIRoot.");
            bool ownsResources = resourceService == null;
            if (ownsResources)
            {
                _addressables = new AddressablesResourceService();
                _addressablesInitialization = _addressables.InitializeAsync();
                resourceService = _addressables.Service;
            }
            focus = focus ?? new EventSystemUiFocusService();
            if (modalBlocker == null) modalBlocker = new UnityUiModalInputBlocker(modalBarrier);
            else if (modalBarrier != null && !(modalBlocker is UnityUiModalInputBlocker))
                modalBlocker = new UnityUiModalInputBlocker(modalBarrier, modalBlocker);
            _manager = new UiPanelManager(new AddressablesUiResourceProvider(resourceService, _addressablesInitialization),
                new UGuiPanelFactory(root), focus, modalBlocker);
            try
            {
                if (definitions == null) throw new InvalidOperationException("At least one UI panel definition is required.");
                for (int i = 0; i < definitions.Length; i++)
                {
                    if (definitions[i] == null) throw new InvalidOperationException("UI panel definition cannot be null.");
                    _manager.Register(definitions[i].ToDefinition());
                }
                _manager.AttachToFrameUpdate(frameUpdateManager, loops.Presentation);
            }
            catch { _manager.Dispose(); _manager = null; _addressables?.Dispose(); _addressables = null; throw; }
        }

        public Task<UiPanelHandle> OpenAsync(UiPanelId id, object argument = null)
        {
            if (_manager == null) throw new InvalidOperationException("Initialize the UI manager first.");
            return _manager.OpenAsync(id, argument);
        }

        public Task CloseTopModalAsync() => _manager == null ? Task.CompletedTask : _manager.CloseTopModalAsync();

        public async Task InitializeAsync(FrameUpdateManager frameUpdateManager, UnityFrameUpdateLoops loops,
            IUiFocusService focus = null, IUiModalInputBlocker modalBlocker = null, ResourceService resourceService = null)
        {
            Initialize(frameUpdateManager, loops, focus, modalBlocker, resourceService);
            if (_addressablesInitialization != null) await _addressablesInitialization;
        }

        private void OnDestroy() { _manager?.Dispose(); _manager = null; _addressables?.Dispose(); _addressables = null; }
    }
}
