using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.StateMachine
{
    public interface IState<TContext>
    {
        void Enter(TContext context);
        void Tick(TContext context, double deltaSeconds);
        void Exit(TContext context);
    }

    /// <summary>Convenient state definition when a dedicated state class is unnecessary.</summary>
    public sealed class DelegateState<TContext> : IState<TContext>
    {
        private readonly Action<TContext> _enter;
        private readonly Action<TContext, double> _tick;
        private readonly Action<TContext> _exit;
        public DelegateState(Action<TContext> enter = null, Action<TContext, double> tick = null, Action<TContext> exit = null)
        { _enter = enter; _tick = tick; _exit = exit; }
        public void Enter(TContext context) { _enter?.Invoke(context); }
        public void Tick(TContext context, double deltaSeconds) { _tick?.Invoke(context, deltaSeconds); }
        public void Exit(TContext context) { _exit?.Invoke(context); }
    }

    /// <summary>
    /// Main-thread state machine. Conditions run in registration order before Tick; first match wins.
    /// Changes requested from callbacks are deferred until that callback returns (last request wins).
    /// A callback failure stops the machine, attempts Exit once, records Error and propagates it.
    /// </summary>
    public sealed class StateMachine<TKey, TContext> : IFrameUpdate, IUpdateLifetime, IDisposable
    {
        private sealed class Transition
        {
            internal TKey To;
            internal Func<TContext, bool> Condition;
        }
        private readonly Dictionary<TKey, IState<TContext>> _states = new Dictionary<TKey, IState<TContext>>();
        private readonly Dictionary<TKey, List<Transition>> _transitions = new Dictionary<TKey, List<Transition>>();
        private bool _busy;
        private bool _disposed;
        private bool _pending;
        private bool _stop;
        private TKey _next;
        private TKey _current;
        private IState<TContext> _state;
        public StateMachine(TContext context) { Context = context; }
        public TContext Context { get; }
        public bool HasCurrent => _state != null;
        public TKey Current => HasCurrent ? _current : throw new InvalidOperationException("No active state.");
        public bool IsAlive => !_disposed;
        public Exception Error { get; private set; }

        public void AddState(TKey key, IState<TContext> state)
        {
            EnsureConfigurable();
            _states.Add(key, state ?? throw new ArgumentNullException(nameof(state)));
        }
        public void AddTransition(TKey from, TKey to, Func<TContext, bool> condition)
        {
            EnsureConfigurable();
            RequireState(from); RequireState(to);
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!_transitions.TryGetValue(from, out var transitions))
            { transitions = new List<Transition>(); _transitions.Add(from, transitions); }
            transitions.Add(new Transition { To = to, Condition = condition });
        }
        public void ChangeState(TKey key)
        {
            EnsureOpen(); RequireState(key);
            _next = key; _pending = true;
            if (_busy) return;
            Error = null;
            Execute(false, 0);
        }
        public void OnFrameUpdate(in FrameUpdateContext context) { Tick(context.DeltaTime); }
        public void Tick(double deltaSeconds)
        {
            EnsureOpen();
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (_busy) throw new InvalidOperationException("Recursive state machine Tick is not supported.");
            Execute(true, deltaSeconds);
        }
        public void Stop()
        {
            if (_disposed) return;
            _stop = true; _pending = false;
            if (!_busy) Execute(false, 0);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _stop = true; _pending = false;
            if (!_busy) Execute(false, 0);
        }
        private void Execute(bool tick, double delta)
        {
            _busy = true;
            try
            {
                if (tick && HasCurrent && _transitions.TryGetValue(_current, out var transitions))
                {
                    for (int i = 0; i < transitions.Count; i++)
                    {
                        var transition = transitions[i];
                        bool match = transition.Condition(Context);
                        if (_stop || _pending) break;
                        if (match) { _next = transition.To; _pending = true; break; }
                    }
                }
                Pump();
                if (tick && HasCurrent && !_disposed) { _state.Tick(Context, delta); Pump(); }
            }
            catch (Exception error)
            {
                try { Leave(); }
                catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
                Error = error;
                _pending = false; _stop = false;
                throw Error;
            }
            finally
            {
                _busy = false;
                if (_disposed) { _states.Clear(); _transitions.Clear(); }
            }
        }
        private void Pump()
        {
            int changes = 0;
            while (_stop || _pending)
            {
                if (_stop)
                {
                    _pending = false;
                    Leave();
                    _stop = false; _pending = false;
                    return;
                }
                if (++changes > 32) throw new InvalidOperationException("State transition cycle exceeded 32 changes.");
                TKey next = _next;
                _pending = false;
                if (HasCurrent && EqualityComparer<TKey>.Default.Equals(_current, next)) continue;
                Leave();
                if (_stop) continue;
                _current = next;
                _state = _states[next];
                _state.Enter(Context);
            }
        }
        private void Leave()
        {
            var previous = _state;
            _state = null;
            _current = default;
            previous?.Exit(Context);
        }
        private void RequireState(TKey key)
        { if (!_states.ContainsKey(key)) throw new KeyNotFoundException("Register the state before using it: " + key); }
        private void EnsureOpen() { if (_disposed) throw new ObjectDisposedException(nameof(StateMachine<TKey, TContext>)); }
        private void EnsureConfigurable()
        {
            EnsureOpen();
            if (_busy || HasCurrent) throw new InvalidOperationException("Stop the machine before changing its definitions.");
        }
    }
}
