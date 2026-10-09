using System;
using System.Collections.Generic;
using WFrameWork.Diagnostics;

namespace WFrameWork.Input
{
    public sealed class InputService : IDisposable
    {
        private sealed class ContextState
        {
            internal readonly string Id;
            internal int Priority;
            internal bool BlocksLowerPriority;
            internal int LeaseCount;
            internal bool EnabledByDefault;
            internal bool ExplicitlyEnabled = true;
            internal ContextState(string id, int priority, bool blocksLowerPriority, bool enabledByDefault)
            { Id = id; Priority = priority; BlocksLowerPriority = blocksLowerPriority; EnabledByDefault = enabledByDefault; }
            internal bool Enabled => ExplicitlyEnabled && (EnabledByDefault || LeaseCount > 0);
        }

        private sealed class ActionSlot
        {
            internal readonly InputActionDefinition Definition;
            internal readonly List<InputSubscription> Subscriptions = new List<InputSubscription>(2);
            internal readonly List<List<InputSubscription>> DispatchScratch = new List<List<InputSubscription>>(1);
            internal int DispatchDepth;
            internal InputValue RawValue;
            internal bool RawHeld;
            internal bool RawCanceled;
            internal InputActionState Delivered;
            internal ActionSlot(InputActionDefinition definition) { Definition = definition; }
        }

        private readonly struct BufferedEvent
        {
            internal readonly long Serial;
            internal readonly long RenderFrameId;
            internal readonly InputActionId ActionId;
            internal readonly InputActionType ActionType;
            internal readonly InputFixedEventPhase Phase;
            internal readonly InputValue Value;
            internal readonly bool Valid;
            internal BufferedEvent(long serial, long renderFrameId, InputActionId actionId,
                InputActionType actionType, InputFixedEventPhase phase, InputValue value)
            {
                Serial = serial; RenderFrameId = renderFrameId; ActionId = actionId;
                ActionType = actionType; Phase = phase; Value = value; Valid = true;
            }
            private BufferedEvent(long serial, long renderFrameId, InputActionId actionId,
                InputActionType actionType, InputFixedEventPhase phase, InputValue value, bool valid)
            {
                Serial = serial; RenderFrameId = renderFrameId; ActionId = actionId;
                ActionType = actionType; Phase = phase; Value = value; Valid = valid;
            }
            internal BufferedEvent Invalidate() => new BufferedEvent(Serial, RenderFrameId, ActionId, ActionType, Phase, Value, false);
        }

        private readonly Dictionary<InputActionId, ActionSlot> _actions = new Dictionary<InputActionId, ActionSlot>();
        private readonly List<ActionSlot> _actionOrder = new List<ActionSlot>();
        private readonly Dictionary<string, ContextState> _contexts = new Dictionary<string, ContextState>(StringComparer.Ordinal);
        private readonly BufferedEvent[] _fixedEvents;
        private readonly long _fixedEventLifetimeFrames;
        private readonly IInputBackend _backend;
        private readonly InputSampleWriter _writer;
        private readonly DiagnosticLogger _diagnostics;
        private long _nextFixedEventSerial;
        private long _firstFixedEventSerial = 1;
        private int _fixedEventCount;
        private bool _disposed;
        private bool _focused = true;
        private bool _applicationPaused;
        private long _sequence;
        private long _renderFrameId;
        private InputSnapshot _snapshot;
        private bool _suppressUntilReleased;

        public InputService(IInputBackend backend, InputServiceConfig config = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            config = config ?? new InputServiceConfig();
            config.Validate();
            _fixedEvents = new BufferedEvent[config.FixedEventCapacity];
            _fixedEventLifetimeFrames = config.FixedEventLifetimeFrames;
            _writer = new InputSampleWriter(this);
            _diagnostics = new DiagnosticLogger("Input", config.Diagnostics);
        }

        public bool IsDisposed => _disposed;
        public long CurrentSequence => _sequence;
        public long CurrentRenderFrameId => _renderFrameId;
        public InputSnapshot Snapshot => _snapshot;
        public int RegisteredActionCount => _actionOrder.Count;
        public long DroppedFixedEventCount { get; private set; }
        public bool IsFocused => _focused;
        public bool IsApplicationPaused => _applicationPaused;
        public bool IsDeliverySuppressed => !_focused || _applicationPaused || _suppressUntilReleased;

        public void RegisterContext(string contextId, int priority = 0, bool blocksLowerPriority = false,
            bool enabledByDefault = true)
        {
            EnsureUsable();
            ValidateContextId(contextId);
            if (_contexts.TryGetValue(contextId, out var existing))
            {
                if (existing.Priority != priority || existing.BlocksLowerPriority != blocksLowerPriority ||
                    existing.EnabledByDefault != enabledByDefault)
                    throw new InvalidOperationException("The input context is already registered with another configuration.");
                return;
            }
            _contexts.Add(contextId, new ContextState(contextId, priority, blocksLowerPriority, enabledByDefault));
        }

        public InputContextToken AcquireContext(string contextId)
        {
            EnsureUsable();
            ValidateContextId(contextId);
            if (!_contexts.TryGetValue(contextId, out var state))
            {
                state = new ContextState(contextId, 0, false, false);
                _contexts.Add(contextId, state);
            }
            bool[] before = CaptureVisibility();
            state.LeaseCount++;
            InvalidateChangedEvents(before);
            ReconcileVisibility(_renderFrameId);
            return new InputContextToken(this, contextId);
        }

        public void SetContextEnabled(string contextId, bool enabled)
        {
            EnsureUsable();
            if (!_contexts.TryGetValue(contextId, out var state)) throw new KeyNotFoundException(contextId);
            bool[] before = CaptureVisibility();
            state.ExplicitlyEnabled = enabled;
            InvalidateChangedEvents(before);
            ReconcileVisibility(_renderFrameId);
        }

        public bool IsContextEnabled(string contextId)
        {
            return _contexts.TryGetValue(contextId, out var state) && state.Enabled;
        }

        public void RegisterAction(InputActionDefinition definition)
        {
            EnsureUsable();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (_actions.TryGetValue(definition.Id, out var existing))
            {
                if (!existing.Definition.Equals(definition)) throw new InvalidOperationException("The action id is already registered with another definition.");
                return;
            }
            if (!_contexts.ContainsKey(definition.ContextId))
                _contexts.Add(definition.ContextId, new ContextState(definition.ContextId, 0, false, true));
            var slot = new ActionSlot(definition);
            _actions.Add(definition.Id, slot);
            _actionOrder.Add(slot);
        }

        public InputSubscription Subscribe(InputActionId actionId, Action<InputActionEvent> callback)
        {
            EnsureUsable();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!_actions.TryGetValue(actionId, out var slot)) throw new KeyNotFoundException(actionId.ToString());
            var subscription = new InputSubscription(this, actionId, callback);
            slot.Subscriptions.Add(subscription);
            return subscription;
        }

        public InputFixedEventReader CreateFixedEventReader(string consumerName = null)
        {
            EnsureUsable();
            // Each reader owns its cursor; readers never compete or consume a shared cursor.
            return new InputFixedEventReader(this, _nextFixedEventSerial + 1);
        }

        public InputSnapshot Update(long renderFrameId)
        {
            EnsureUsable();
            if (renderFrameId < _renderFrameId) throw new ArgumentOutOfRangeException(nameof(renderFrameId), "Render frame id must be monotonic.");
            _renderFrameId = renderFrameId;
            _sequence++;
            _backend.Sample(_writer);
            if (!_focused || _applicationPaused)
            {
                ClearRawSamples();
            }
            else if (_suppressUntilReleased)
            {
                if (!HasRawInput()) _suppressUntilReleased = false;
                else ClearRawSamples();
            }
            ReconcileVisibility(renderFrameId);
            _snapshot = new InputSnapshot(this, _sequence);
            return _snapshot;
        }

        public void ClearInput()
        {
            if (_disposed) return;
            InvalidatePendingFixedEvents();
            for (int i = 0; i < _actionOrder.Count; i++)
            {
                var slot = _actionOrder[i];
                if (slot.Delivered.IsHeld)
                    Emit(slot, InputFixedEventPhase.Canceled, slot.Delivered.Value);
                slot.RawValue = InputValue.Zero;
                slot.RawHeld = false;
                slot.RawCanceled = false;
                slot.Delivered = new InputActionState(slot.Definition.Id, slot.Definition.Type, InputValue.Zero, InputButtonPhase.Canceled);
            }
            _suppressUntilReleased = true;
            _backend.Reset();
        }

        public void SetApplicationFocus(bool focused)
        {
            if (_disposed || _focused == focused) return;
            _focused = focused;
            if (!focused) ClearInput();
            else _suppressUntilReleased = true;
        }

        public void SetApplicationPaused(bool paused)
        {
            if (_disposed || _applicationPaused == paused) return;
            _applicationPaused = paused;
            if (paused) ClearInput();
            else _suppressUntilReleased = true;
        }

        internal void SetRawButton(InputActionId id, bool held)
        {
            var slot = GetSlot(id);
            slot.RawValue = new InputValue(held ? 1 : 0);
            slot.RawHeld = held;
            slot.RawCanceled = false;
        }

        internal void SetRawAxis(InputActionId id, float x, float y)
        {
            var slot = GetSlot(id);
            slot.RawValue = new InputValue(x, y);
            slot.RawHeld = Math.Abs(x) > 0.00001f || Math.Abs(y) > 0.00001f;
            slot.RawCanceled = false;
        }

        internal void SetRawCanceled(InputActionId id)
        {
            var slot = GetSlot(id);
            slot.RawValue = InputValue.Zero;
            slot.RawHeld = false;
            slot.RawCanceled = true;
        }

        internal bool TryGetState(InputActionId id, long sequence, out InputActionState state)
        {
            if (sequence != _sequence || _disposed || !_actions.TryGetValue(id, out var slot))
            {
                state = default(InputActionState);
                return false;
            }
            state = slot.Delivered;
            return true;
        }

        internal void RemoveSubscription(InputActionId id, InputSubscription subscription)
        {
            if (_actions.TryGetValue(id, out var slot)) slot.Subscriptions.Remove(subscription);
        }

        internal void ReleaseContext(string contextId)
        {
            if (_disposed || !_contexts.TryGetValue(contextId, out var state)) return;
            bool[] before = CaptureVisibility();
            if (state.LeaseCount > 0) state.LeaseCount--;
            InvalidateChangedEvents(before);
            ReconcileVisibility(_renderFrameId);
        }

        internal bool TryReadFixedEvent(ref long cursor, long currentRenderFrameId,
            ref long missedCount, out InputActionEvent inputEvent)
        {
            while (true)
            {
                if (_fixedEventCount == 0 || cursor > _nextFixedEventSerial)
                {
                    inputEvent = default(InputActionEvent);
                    return false;
                }
                if (cursor < _firstFixedEventSerial)
                {
                    missedCount += _firstFixedEventSerial - cursor;
                    cursor = _firstFixedEventSerial;
                }
                if (cursor > _nextFixedEventSerial)
                {
                    inputEvent = default(InputActionEvent);
                    return false;
                }
                var item = _fixedEvents[(int)((cursor - 1) % _fixedEvents.Length)];
                cursor++;
                if (!item.Valid || item.Serial != cursor - 1) continue;
                if (_fixedEventLifetimeFrames > 0 && currentRenderFrameId - item.RenderFrameId > _fixedEventLifetimeFrames)
                    continue;
                inputEvent = new InputActionEvent(item.Serial, item.RenderFrameId, item.ActionId,
                    item.ActionType, item.Phase, item.Value);
                return true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _backend.Dispose();
            _actions.Clear();
            _actionOrder.Clear();
            _contexts.Clear();
            _fixedEventCount = 0;
        }

        private void ReconcileVisibility(long renderFrameId)
        {
            for (int i = 0; i < _actionOrder.Count; i++)
            {
                var slot = _actionOrder[i];
                bool visible = IsActionVisible(slot.Definition.ContextId);
                bool previousHeld = slot.Delivered.IsHeld;
                bool wasCanceled = slot.Delivered.IsCanceled;
                bool currentHeld = visible && slot.RawHeld && !slot.RawCanceled;
                InputValue value = visible ? Normalize(slot.Definition, slot.RawValue) : InputValue.Zero;
                if (!visible && previousHeld)
                    Emit(slot, InputFixedEventPhase.Canceled, slot.Delivered.Value);
                else if (slot.RawCanceled && previousHeld)
                    Emit(slot, InputFixedEventPhase.Canceled, slot.Delivered.Value);
                else if (currentHeld && !previousHeld)
                    Emit(slot, InputFixedEventPhase.Pressed, value);
                else if (!currentHeld && previousHeld)
                    Emit(slot, InputFixedEventPhase.Released, slot.Delivered.Value);
                else if (currentHeld && previousHeld)
                    Notify(slot, InputFixedEventPhase.Pressed, value, 0, false);
                InputButtonPhase phase = currentHeld
                    ? (previousHeld ? InputButtonPhase.Held : InputButtonPhase.Pressed)
                    : ((!visible || slot.RawCanceled) && (previousHeld || wasCanceled)
                        ? InputButtonPhase.Canceled
                        : (previousHeld ? InputButtonPhase.Released : (slot.RawCanceled ? InputButtonPhase.Canceled : InputButtonPhase.None)));
                slot.Delivered = new InputActionState(slot.Definition.Id, slot.Definition.Type, value, phase);
                slot.RawCanceled = false;
            }
        }

        private bool IsActionVisible(string contextId)
        {
            if (!_contexts.TryGetValue(contextId, out var own) || !own.Enabled) return false;
            foreach (var pair in _contexts)
            {
                var other = pair.Value;
                if (other.Enabled && other.BlocksLowerPriority && other.Priority > own.Priority) return false;
            }
            return true;
        }

        private static InputValue Normalize(InputActionDefinition definition, InputValue raw)
        {
            float x = definition.InvertX ? -raw.X : raw.X;
            float y = definition.InvertY ? -raw.Y : raw.Y;
            if (definition.Type == InputActionType.Button) x = raw.X > 0.5f ? 1 : 0;
            else
            {
                float magnitude = (float)Math.Sqrt(x * x + y * y);
                if (magnitude < definition.DeadZone) { x = 0; y = 0; }
                else if (definition.DeadZone > 0)
                {
                    float normalized = (magnitude - definition.DeadZone) / (1 - definition.DeadZone);
                    float factor = magnitude <= 0 ? 0 : normalized / magnitude;
                    x *= factor; y *= factor;
                }
            }
            return new InputValue(x * definition.Scale, y * definition.Scale);
        }

        private void Emit(ActionSlot slot, InputFixedEventPhase phase, InputValue value)
        {
            long serial = AppendFixedEvent(slot, phase, value);
            Notify(slot, phase, value, serial, true);
        }

        private void Notify(ActionSlot slot, InputFixedEventPhase phase, InputValue value, long serial, bool includeRelease)
        {
            if (!includeRelease && phase == InputFixedEventPhase.Pressed) return;
            var inputEvent = new InputActionEvent(serial, _renderFrameId, slot.Definition.Id,
                slot.Definition.Type, phase, value);
            int depth = slot.DispatchDepth++;
            List<InputSubscription> scratch;
            if (depth >= slot.DispatchScratch.Count)
            {
                scratch = new List<InputSubscription>(slot.Subscriptions.Count);
                slot.DispatchScratch.Add(scratch);
            }
            else scratch = slot.DispatchScratch[depth];
            scratch.Clear();
            for (int i = 0; i < slot.Subscriptions.Count; i++)
                if (slot.Subscriptions[i].IsActive) scratch.Add(slot.Subscriptions[i]);
            for (int i = 0; i < scratch.Count; i++)
            {
                var subscription = scratch[i];
                if (!subscription.IsActive) continue;
                try { subscription.Callback(inputEvent); }
                catch (Exception error) { _diagnostics.Error("Input subscriber threw for " + slot.Definition.Id, error); }
            }
            scratch.Clear();
            slot.DispatchDepth--;
        }

        private bool HasRawInput()
        {
            for (int i = 0; i < _actionOrder.Count; i++) if (_actionOrder[i].RawHeld || _actionOrder[i].RawCanceled) return true;
            return false;
        }

        private void ClearRawSamples()
        {
            for (int i = 0; i < _actionOrder.Count; i++)
            {
                _actionOrder[i].RawValue = InputValue.Zero;
                _actionOrder[i].RawHeld = false;
                _actionOrder[i].RawCanceled = false;
            }
        }

        private void InvalidatePendingFixedEvents()
        {
            InvalidatePendingFixedEvents(_ => true);
        }

        private bool[] CaptureVisibility()
        {
            var result = new bool[_actionOrder.Count];
            for (int i = 0; i < _actionOrder.Count; i++) result[i] = IsActionVisible(_actionOrder[i].Definition.ContextId);
            return result;
        }

        private void InvalidateChangedEvents(bool[] before)
        {
            bool changed = false;
            for (int i = 0; i < _actionOrder.Count; i++)
            {
                if (before[i] != IsActionVisible(_actionOrder[i].Definition.ContextId)) { changed = true; break; }
            }
            if (!changed) return;
            InvalidatePendingFixedEvents(actionId =>
            {
                for (int i = 0; i < _actionOrder.Count; i++)
                    if (_actionOrder[i].Definition.Id == actionId)
                        return before[i] != IsActionVisible(_actionOrder[i].Definition.ContextId);
                return false;
            });
        }

        private void InvalidatePendingFixedEvents(Func<InputActionId, bool> shouldInvalidate)
        {
            int remaining = 0;
            for (long serial = _firstFixedEventSerial; serial <= _nextFixedEventSerial; serial++)
            {
                int index = (int)((serial - 1) % _fixedEvents.Length);
                BufferedEvent item = _fixedEvents[index];
                if (item.Valid && item.Serial == serial && shouldInvalidate(item.ActionId))
                {
                    _fixedEvents[index] = item.Invalidate();
                    item = _fixedEvents[index];
                }
                if (item.Valid && item.Serial == serial) remaining++;
            }
            _fixedEventCount = remaining;
            while (_firstFixedEventSerial <= _nextFixedEventSerial)
            {
                BufferedEvent item = _fixedEvents[(int)((_firstFixedEventSerial - 1) % _fixedEvents.Length)];
                if (item.Valid && item.Serial == _firstFixedEventSerial) break;
                _firstFixedEventSerial++;
            }
            if (_fixedEventCount == 0) _firstFixedEventSerial = _nextFixedEventSerial + 1;
        }

        private long AppendFixedEvent(ActionSlot slot, InputFixedEventPhase phase, InputValue value)
        {
            long serial = ++_nextFixedEventSerial;
            if (_fixedEventCount == _fixedEvents.Length)
            {
                _firstFixedEventSerial++;
                DroppedFixedEventCount++;
            }
            else _fixedEventCount++;
            _fixedEvents[(int)((serial - 1) % _fixedEvents.Length)] =
                new BufferedEvent(serial, _renderFrameId, slot.Definition.Id, slot.Definition.Type, phase, value);
            return serial;
        }

        private ActionSlot GetSlot(InputActionId id)
        {
            if (!_actions.TryGetValue(id, out var slot)) throw new KeyNotFoundException(id.ToString());
            return slot;
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputService));
        }

        private static void ValidateContextId(string contextId)
        {
            if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("Context id is required.", nameof(contextId));
        }
    }

    /// <summary>Deterministic test backend and a useful adapter for tools that already own input state.</summary>
    public sealed class InjectedInputBackend : IInputBackend
    {
        private readonly Dictionary<InputActionId, InputValue> _values = new Dictionary<InputActionId, InputValue>();
        private readonly HashSet<InputActionId> _canceled = new HashSet<InputActionId>();

        public void SetButton(InputActionId id, bool held) => _values[id] = new InputValue(held ? 1 : 0);
        public void SetAxis(InputActionId id, float x, float y = 0) => _values[id] = new InputValue(x, y);
        public void Cancel(InputActionId id) => _canceled.Add(id);
        public void Sample(InputSampleWriter writer)
        {
            foreach (var pair in _values) writer.Set(pair.Key, pair.Value);
            foreach (var id in _canceled) writer.Cancel(id);
            _canceled.Clear();
        }
        public void Reset() { _values.Clear(); _canceled.Clear(); }
        public void Dispose() { _values.Clear(); _canceled.Clear(); }
    }

    public sealed class InputSampleWriter
    {
        private readonly InputService _service;
        internal InputSampleWriter(InputService service) { _service = service; }
        public void SetButton(InputActionId id, bool held) => _service.SetRawButton(id, held);
        public void SetAxis1D(InputActionId id, float value) => _service.SetRawAxis(id, value, 0);
        public void SetAxis2D(InputActionId id, float x, float y) => _service.SetRawAxis(id, x, y);
        public void Set(InputActionId id, InputValue value) => _service.SetRawAxis(id, value.X, value.Y);
        public void Cancel(InputActionId id) => _service.SetRawCanceled(id);
    }
}
