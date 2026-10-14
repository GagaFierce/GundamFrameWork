using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>
    /// View-only lifecycle host. A new ViewModel and binding set is created for every panel
    /// generation and both are disposed before the Unity object can be reused.
    /// </summary>
    public abstract class UiPanelViewBehaviour<TViewModel> : MonoBehaviour, IUGuiPanelLifecycle, IUiCloseRequest
        where TViewModel : ViewModelBase
    {
        private UiBindingSet _bindings;
        private TViewModel _viewModel;
        private int _generation;

        protected TViewModel ViewModel => _viewModel;
        protected UiBindingSet Bindings => _bindings;
        protected int Generation => _generation;
        public virtual bool RequiresContinuousUpdate => false;

        public void OnPanelOpened(object argument)
        {
            DisposeGeneration();
            _generation++;
            TViewModel next = CreateViewModel(argument);
            if (next == null) throw new InvalidOperationException(GetType().Name + " returned a null ViewModel.");
            _viewModel = next;
            _bindings = new UiBindingSet();
            try
            {
                Bind(next, _bindings);
                OnViewModelOpened(next, argument);
            }
            catch
            {
                DisposeGeneration();
                throw;
            }
        }

        protected abstract TViewModel CreateViewModel(object argument);
        protected abstract void Bind(TViewModel viewModel, UiBindingSet bindings);
        protected virtual void OnViewModelOpened(TViewModel viewModel, object argument) { }
        protected virtual void OnViewModelClosed() { }

        public virtual void OnPanelShown()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return;
            foreach (var selectable in GetComponentsInChildren<UnityEngine.UI.Selectable>())
                if (selectable.IsInteractable() && selectable.gameObject.activeInHierarchy)
                { eventSystem.SetSelectedGameObject(selectable.gameObject); break; }
        }
        public virtual void OnPanelHidden() { }
        public virtual void OnPanelUpdate(in UiPanelUpdateContext context) { }
        public Task<bool> CanCloseAsync(CancellationToken token) => _viewModel is IUiCloseRequest guard
            ? guard.CanCloseAsync(token) : Task.FromResult(true);

        public void OnPanelClosed()
        {
            try { OnViewModelClosed(); }
            finally { DisposeGeneration(); }
        }

        private void DisposeGeneration()
        {
            var bindings = _bindings; _bindings = null;
            var viewModel = _viewModel; _viewModel = null;
            try { bindings?.Dispose(); }
            finally { viewModel?.Dispose(); }
        }

        protected virtual void OnDestroy() { DisposeGeneration(); }
    }
}
