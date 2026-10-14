using System;
using UnityEngine;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.Input.Unity
{
    /// <summary>Samples InputService from the host's Input loop; no second Unity Update driver is installed.</summary>
    public sealed class InputFrameUpdateAdapter : IFrameUpdate, IDisposable
    {
        private readonly InputService _service;
        private readonly FrameUpdateManager _manager;
        private readonly UpdateHandle _handle;
        private bool _disposed;

        public InputFrameUpdateAdapter(InputService service, FrameUpdateManager manager, UpdateLoop inputLoop,
            UpdateGroup group = default(UpdateGroup), UpdateScope scope = default(UpdateScope))
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (!inputLoop.IsValid) throw new ArgumentException("A valid Input loop is required.", nameof(inputLoop));
            _handle = manager.Register(this, new FrameUpdateOptions
            {
                Loop = inputLoop, Phase = UpdatePhase.EarlyUpdate, Group = group, Scope = scope,
                Schedule = UpdateSchedule.EveryStep(), WorkClass = UpdateWorkClass.Required
            });
        }

        public void OnFrameUpdate(in FrameUpdateContext context)
        { if (!_disposed) _service.Update(Time.frameCount); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_manager.IsDisposed) _manager.Unregister(_handle);
        }
    }
}

