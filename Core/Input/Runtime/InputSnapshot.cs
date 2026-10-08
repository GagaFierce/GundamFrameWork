using System;

namespace WFrameWork.Input
{
    /// <summary>
    /// Read-only view of one sampled render frame. It is never destructive: repeated reads
    /// return the same state until the service publishes the next frame.
    /// </summary>
    public readonly struct InputSnapshot
    {
        private readonly InputService _service;
        private readonly long _sequence;

        internal InputSnapshot(InputService service, long sequence)
        {
            _service = service;
            _sequence = sequence;
        }

        public long Sequence => _sequence;
        public bool IsValid => _service != null && !_service.IsDisposed && _service.CurrentSequence == _sequence;

        public bool TryGetState(InputActionId id, out InputActionState state)
        {
            if (_service == null) { state = default(InputActionState); return false; }
            return _service.TryGetState(id, _sequence, out state);
        }

        public bool IsPressed(InputActionId id) => TryGetState(id, out var state) && state.IsPressed;
        public bool IsHeld(InputActionId id) => TryGetState(id, out var state) && state.IsHeld;
        public bool IsReleased(InputActionId id) => TryGetState(id, out var state) && state.IsReleased;
        public bool IsCanceled(InputActionId id) => TryGetState(id, out var state) && state.IsCanceled;
        public InputValue ReadValue(InputActionId id)
        {
            return TryGetState(id, out var state) ? state.Value : InputValue.Zero;
        }
    }

    public sealed class InputSubscription : IDisposable
    {
        private readonly InputService _service;
        private readonly InputActionId _actionId;
        private readonly Action<InputActionEvent> _callback;
        private bool _disposed;

        internal InputSubscription(InputService service, InputActionId actionId, Action<InputActionEvent> callback)
        {
            _service = service;
            _actionId = actionId;
            _callback = callback;
        }

        internal Action<InputActionEvent> Callback => _callback;
        internal bool IsActive => !_disposed;
        public bool IsDisposed => _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.RemoveSubscription(_actionId, this);
        }
    }

    public sealed class InputContextToken : IDisposable
    {
        private readonly InputService _service;
        private readonly string _contextId;
        private bool _disposed;

        internal InputContextToken(InputService service, string contextId)
        {
            _service = service;
            _contextId = contextId;
        }

        public bool IsActive => !_disposed && _service != null && !_service.IsDisposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.ReleaseContext(_contextId);
        }
    }

    public sealed class InputFixedEventReader : IDisposable
    {
        private readonly InputService _service;
        private long _cursor;
        private bool _disposed;

        internal InputFixedEventReader(InputService service, long cursor)
        {
            _service = service;
            _cursor = cursor;
        }

        public bool IsDisposed => _disposed;
        public long MissedEventCount { get; private set; }

        public bool TryRead(long currentRenderFrameId, out InputActionEvent inputEvent)
        {
            if (_disposed || _service == null)
            {
                inputEvent = default(InputActionEvent);
                return false;
            }
            long missed = MissedEventCount;
            bool read = _service.TryReadFixedEvent(ref _cursor, currentRenderFrameId, ref missed, out inputEvent);
            MissedEventCount = missed;
            return read;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
