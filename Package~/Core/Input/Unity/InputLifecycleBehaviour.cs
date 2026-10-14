using System;
using UnityEngine;

namespace WFrameWork.Input.Unity
{
    /// <summary>
    /// Lifecycle-only bridge. It does not sample input or create a second Update driver; it
    /// clears transient state when Unity stops invoking the normal input loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InputLifecycleBehaviour : MonoBehaviour
    {
        private InputService _service;
        private bool _ownsService;

        public void Attach(InputService service, bool ownsService = false)
        {
            if (_service != null && !ReferenceEquals(_service, service))
            {
                _service.ClearInput();
                if (_ownsService) _service.Dispose();
            }
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ownsService = ownsService;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) _service?.ClearInput(); _service?.SetApplicationFocus(focused); }
        private void OnApplicationPause(bool paused) { if (paused) _service?.ClearInput(); _service?.SetApplicationPaused(paused); }
        private void OnDisable() { _service?.ClearInput(); }

        private void OnDestroy()
        {
            if (_ownsService) _service?.Dispose();
            _service = null;
        }
    }
}
