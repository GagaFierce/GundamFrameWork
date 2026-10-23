using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Diagnostics;

namespace WFrameWork.Scheduling
{
    public enum ScheduledStatus { Running, Completed, Cancelled, Faulted }

    /// <summary>Main-thread handle. Dispose cancels and releases captured callbacks immediately.</summary>
    public abstract class ScheduledHandle : IDisposable
    {
        private IUpdateLifetime _owner;
        internal ScheduledHandle(IUpdateLifetime owner) { _owner = owner; }
        public ScheduledStatus Status { get; private set; } = ScheduledStatus.Running;
        public bool IsDone => Status != ScheduledStatus.Running;
        public bool IsPaused { get; private set; }
        public Exception Error { get; private set; }
        public void Pause() { if (!IsDone) IsPaused = true; }
        public void Resume() { if (!IsDone) IsPaused = false; }
        public void Cancel() { Finish(ScheduledStatus.Cancelled); }
        public void Dispose() { Cancel(); }
        internal void Advance(double delta)
        {
            if (IsDone) return;
            if (_owner != null && !_owner.IsAlive) { Cancel(); return; }
            if (!IsPaused) Tick(delta);
        }
        internal void Fail(Exception error) { Error = error; Finish(ScheduledStatus.Faulted); }
        protected void Complete() { Finish(ScheduledStatus.Completed); }
        private void Finish(ScheduledStatus status)
        {
            if (IsDone) return;
            Status = status;
            _owner = null;
            Release();
        }
        protected abstract void Tick(double delta);
        protected abstract void Release();
    }

    public sealed class TimerHandle : ScheduledHandle
    {
        private Action _callback;
        private readonly double _interval;
        private int _remainingCalls;
        internal TimerHandle(double delay, double interval, int count, Action callback, IUpdateLifetime owner) : base(owner)
        { RemainingSeconds = delay; _interval = interval; _remainingCalls = count; _callback = callback; }
        public double RemainingSeconds { get; private set; }
        protected override void Tick(double delta)
        {
            RemainingSeconds -= delta;
            if (RemainingSeconds > 0) return;
            _callback();
            if (IsDone) return;
            if (_remainingCalls > 0 && --_remainingCalls == 0) { Complete(); return; }
            // Skip missed repeats after a long frame; never burst an unbounded number of callbacks.
            RemainingSeconds = _interval - (-RemainingSeconds % _interval);
        }
        protected override void Release() { _callback = null; }
    }

    /// <summary>
    /// Register once with FrameUpdateManager. Its group determines scaled/unscaled time and group pause.
    /// All methods and handles are main-thread only. Newly scheduled work starts on the next Tick.
    /// </summary>
    public sealed class TimerScheduler : IFrameUpdate, IUpdateLifetime, IDisposable
    {
        private readonly List<ScheduledHandle> _operations = new List<ScheduledHandle>();
        private readonly DiagnosticLogger _log;
        private bool _ticking;
        private bool _disposed;
        public TimerScheduler(IDiagnosticSink diagnostics = null) { _log = new DiagnosticLogger("Scheduling", diagnostics); }
        public bool IsAlive => !_disposed;
        public int ActiveCount
        {
            get { int count = 0; for (int i = 0; i < _operations.Count; i++) if (!_operations[i].IsDone) count++; return count; }
        }
        public TimerHandle Delay(double seconds, Action callback, IUpdateLifetime owner = null)
        {
            ValidateSeconds(seconds, nameof(seconds));
            return Add(new TimerHandle(seconds, 0, 1, callback ?? throw new ArgumentNullException(nameof(callback)), owner));
        }
        /// <param name="count">Number of callbacks, or -1 for infinite repetition. First callback follows one interval.</param>
        public TimerHandle Repeat(double intervalSeconds, Action callback, int count = -1, IUpdateLifetime owner = null)
        {
            ValidateSeconds(intervalSeconds, nameof(intervalSeconds));
            if (intervalSeconds == 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            if (count != -1 && count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            return Add(new TimerHandle(intervalSeconds, intervalSeconds, count, callback ?? throw new ArgumentNullException(nameof(callback)), owner));
        }
        public SequenceHandle Start(TaskSequence sequence, IUpdateLifetime owner = null)
        {
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            return Add(new SequenceHandle(sequence.Snapshot(), owner));
        }
        private T Add<T>(T operation) where T : ScheduledHandle
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TimerScheduler));
            _operations.Add(operation);
            return operation;
        }
        public void OnFrameUpdate(in FrameUpdateContext context) { Tick(context.DeltaTime); }
        public void Tick(double deltaSeconds)
        {
            if (_disposed) return;
            ValidateSeconds(deltaSeconds, nameof(deltaSeconds));
            if (_ticking) throw new InvalidOperationException("Recursive scheduler Tick is not supported.");
            _ticking = true;
            int count = _operations.Count;
            try
            {
                for (int i = 0; i < count && !_disposed; i++)
                {
                    var operation = _operations[i];
                    try { operation.Advance(deltaSeconds); }
                    catch (Exception error) { operation.Fail(error); _log.Error("Scheduled operation failed.", error); }
                }
            }
            finally
            {
                _ticking = false;
                int write = 0;
                for (int i = 0; i < _operations.Count; i++)
                    if (!_operations[i].IsDone) _operations[write++] = _operations[i];
                if (write < _operations.Count) _operations.RemoveRange(write, _operations.Count - write);
            }
        }
        public void CancelAll()
        {
            for (int i = 0; i < _operations.Count; i++) _operations[i].Cancel();
            if (!_ticking) _operations.Clear();
        }
        public void Dispose() { if (_disposed) return; _disposed = true; CancelAll(); }
        internal static void ValidateSeconds(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }
    }
}
