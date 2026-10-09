using System;
using UnityEngine;
using WFrameWork.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>
    /// View-only lifecycle host. A new ViewModel and binding set is created for every panel
    /// generation and both are disposed before the Unity object can be reused.
    /// </summary>
    public abstract class UiPanelViewBehaviour<TViewModel> : MonoBehaviour, IUGuiPanelLifecycle
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

        public virtual void OnPanelShown() { }
        public virtual void OnPanelHidden() { }
        public virtual void OnPanelUpdate(in UiPanelUpdateContext context) { }

        public void OnPanelClosed()
        {
            try { OnViewModelClosed(); }
            finally { DisposeGeneration(); }
        }

        private void DisposeGeneration()
        {
            var bindings = _bindings; _bindings = null;
            var viewModel = _viewModel; _viewModel = null;
            bindings?.Dispose();
            viewModel?.Dispose();
        }

        protected virtual void OnDestroy() { DisposeGeneration(); }
    }
}
