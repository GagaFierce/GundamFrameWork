using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
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
        [SerializeField] private DirectReferenceUiResourceProvider directReferenceProvider;
        private UiPanelManager _manager;

        public UiPanelManager Manager => _manager;

        public void Initialize(FrameUpdateManager frameUpdateManager, UnityFrameUpdateLoops loops,
            IUiFocusService focus = null, IUiModalInputBlocker modalBlocker = null, ResourceService resourceService = null)
        {
            if (_manager != null) throw new InvalidOperationException("UI manager is already initialized.");
            string validationError;
            if (root == null) throw new InvalidOperationException("A UIRoot is required.");
            if (!root.TryValidate(out validationError)) throw new InvalidOperationException(validationError ?? "Invalid UIRoot.");
            IUiResourceProvider resources = resourceService != null
                ? (IUiResourceProvider)new AddressablesUiResourceProvider(resourceService)
                : directReferenceProvider;
            if (resources == null) throw new InvalidOperationException("A direct-reference UI resource provider or resource service is required.");
            focus = focus ?? new EventSystemUiFocusService();
            if (modalBlocker == null) modalBlocker = new UnityUiModalInputBlocker(modalBarrier);
            else if (modalBarrier != null && !(modalBlocker is UnityUiModalInputBlocker))
                modalBlocker = new UnityUiModalInputBlocker(modalBarrier, modalBlocker);
            _manager = new UiPanelManager(resources, new UGuiPanelFactory(root), focus, modalBlocker);
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
            catch { _manager.Dispose(); _manager = null; throw; }
        }

        public Task<UiPanelHandle> OpenAsync(UiPanelId id, object argument = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_manager == null) throw new InvalidOperationException("Initialize the UI manager first.");
            return _manager.OpenAsync(id, argument, cancellationToken);
        }

        public Task CloseTopModalAsync() => _manager == null ? Task.CompletedTask : _manager.CloseTopModalAsync();
        public Task RequestCloseTopModalAsync(CancellationToken token = default(CancellationToken)) =>
            _manager == null ? Task.CompletedTask : _manager.RequestCloseTopModalAsync(token);

        public Task InitializeAsync(FrameUpdateManager frameUpdateManager, UnityFrameUpdateLoops loops,
            IUiFocusService focus = null, IUiModalInputBlocker modalBlocker = null, ResourceService resourceService = null)
        {
            Initialize(frameUpdateManager, loops, focus, modalBlocker, resourceService);
            return Task.CompletedTask;
        }

        private void OnDestroy() { _manager?.Dispose(); _manager = null; }
    }
}
