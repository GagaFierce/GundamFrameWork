using System;
using WFrameWork.Input;
using WFrameWork.Physics;

namespace WFrameWork.PhysicsInputBridge
{
    /// <summary>Consumes one-shot input events once across zero, one or many fixed steps.</summary>
    public sealed class FixedInputParticipant : IPhysicsFixedStepParticipant, IDisposable
    {
        private readonly InputService _input;
        private readonly InputFixedEventReader _reader;
        private readonly Action<InputActionEvent> _onEvent;
        private bool _disposed;

        public FixedInputParticipant(InputService input, Action<InputActionEvent> onEvent)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _onEvent = onEvent ?? throw new ArgumentNullException(nameof(onEvent));
            _reader = input.CreateFixedEventReader("Physics");
        }

        public long MissedEventCount => _reader.MissedEventCount;
        public InputSnapshot CurrentSnapshot => _input.Snapshot;

        public void OnPhysicsStep(in PhysicsStepContext context)
        {
            if (_disposed) return;
            long renderFrame = _input.CurrentRenderFrameId;
            while (_reader.TryRead(renderFrame, out var inputEvent)) _onEvent(inputEvent);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reader.Dispose();
        }
    }
}

