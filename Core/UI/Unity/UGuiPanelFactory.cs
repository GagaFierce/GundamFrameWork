using System;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using WFrameWork.Threading.Unity;

namespace WFrameWork.UI.Unity
{
    public interface IUGuiPanelLifecycle
    {
        void OnPanelOpened(object argument);
        void OnPanelShown();
        void OnPanelHidden();
        void OnPanelClosed();
        void OnPanelUpdate(in UiPanelUpdateContext context);
        bool RequiresContinuousUpdate { get; }
    }

    public sealed class UGuiPanelFactory : IUiPanelFactory
    {
        private readonly UIRoot _root;
        public UGuiPanelFactory(UIRoot root) { _root = root ?? throw new ArgumentNullException(nameof(root)); }
        public IUiPanelInstance Create(UiPanelDefinition definition, UiResourceHandle resource)
        {
            var prefab = resource?.Asset as GameObject;
            if (prefab == null) throw new InvalidOperationException("UI resource is not a GameObject prefab.");
            var instance = UnityEngine.Object.Instantiate(prefab, _root.ResolveLayer(definition.Layer));
            return new UGuiPanelInstance(instance);
        }
    }

    public sealed class UGuiPanelInstance : IUiPanelInstance, IUiAsyncPanelInstance
    {
        private readonly GameObject _gameObject;
        private readonly List<IUGuiPanelLifecycle> _lifecycles = new List<IUGuiPanelLifecycle>();
        public UGuiPanelInstance(GameObject gameObject)
        {
            _gameObject = gameObject ?? throw new ArgumentNullException(nameof(gameObject));
            var behaviours = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IUGuiPanelLifecycle lifecycle) _lifecycles.Add(lifecycle);
        }
        public bool IsAlive => _gameObject != null;
        public bool RequiresContinuousUpdate
        {
            get { for (int i = 0; i < _lifecycles.Count; i++) if (_lifecycles[i].RequiresContinuousUpdate) return true; return false; }
        }
        public void SetVisible(bool visible) { if (_gameObject != null) _gameObject.SetActive(visible); }
        public void OnOpened(object argument) { for (int i = 0; i < _lifecycles.Count; i++) _lifecycles[i].OnPanelOpened(argument); }
        public void OnShown() { for (int i = 0; i < _lifecycles.Count; i++) _lifecycles[i].OnPanelShown(); }
        public void OnHidden() { for (int i = 0; i < _lifecycles.Count; i++) _lifecycles[i].OnPanelHidden(); }
        public void OnClosed() { for (int i = 0; i < _lifecycles.Count; i++) _lifecycles[i].OnPanelClosed(); }
        public void OnUpdate(in UiPanelUpdateContext context) { for (int i = 0; i < _lifecycles.Count; i++) _lifecycles[i].OnPanelUpdate(in context); }
        public void Dispose()
        {
            if (_gameObject == null) return;
            UnityEngine.Object.Destroy(_gameObject);
            _lifecycles.Clear();
        }

        public Task DisposeAsync()
        {
            if (_gameObject == null) return Task.CompletedTask;
            _lifecycles.Clear();
            return UnityObjectLifetime.DestroyAndWait(_gameObject);
        }
    }
}
