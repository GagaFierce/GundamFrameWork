using System;
using UnityEngine;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    /// <summary>
    /// Optional convenience base. Inject while disabled, then enable to register.
    /// Override the named hooks rather than declaring Unity OnEnable/OnDisable/OnDestroy methods.
    /// Composition through UnityFrameRegistration is equally supported.
    /// </summary>
    public abstract class FrameUpdateBehaviour : MonoBehaviour, IFrameUpdate
    {
        private FrameUpdateManager _manager;
        private FrameUpdateOptions _options;
        private UnityFrameBinding _binding;

        public UpdateHandle UpdateHandle => _binding == null ? default : _binding.Handle;

        public void Configure(FrameUpdateManager manager, in FrameUpdateOptions options)
        {
            if (isActiveAndEnabled)
                throw new InvalidOperationException("Disable this component before injecting its manager.");
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (manager.IsDisposed) throw new ObjectDisposedException(nameof(manager));
            _options = options;
        }

        public abstract void OnFrameUpdate(in FrameUpdateContext context);
        protected virtual void OnFrameUpdateEnabled() { }
        protected virtual void OnFrameUpdateDisabled() { }

        private void OnEnable()
        {
            if (_manager == null) return;
            _binding = UnityFrameRegistration.BindHandle(_manager, this, this, _options);
            try { OnFrameUpdateEnabled(); }
            catch { ReleaseBinding(); throw; }
        }

        private void OnDisable()
        {
            bool wasBound = _binding != null;
            ReleaseBinding();
            if (wasBound) OnFrameUpdateDisabled();
        }

        private void OnDestroy() { ReleaseBinding(); }
        private void ReleaseBinding() { _binding?.Dispose(); _binding = null; }

        public bool RebindScope(UpdateScope scope)
        {
            if (_manager == null) return false;
            if (_binding != null && !_binding.RebindScope(scope)) return false;
            _options.Scope = scope;
            return true;
        }
    }
}
