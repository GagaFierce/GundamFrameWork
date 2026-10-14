using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Threading;

namespace WFrameWork.UI
{
    /// <summary>Base for UI state objects. It owns only UI-lifetime cancellation and subscriptions.</summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly CancellationToken _lifetimeToken;
        private readonly UiExecutionContext _context;
        private bool _disposed;

        protected ViewModelBase(IMainThreadDispatcher dispatcher = null)
        { _context = new UiExecutionContext(dispatcher); _lifetimeToken = _lifetime.Token; }

        public event PropertyChangedEventHandler PropertyChanged;
        public bool IsDisposed => _disposed;
        public CancellationToken LifetimeToken => _lifetimeToken;
        protected Task OnUiAsync(Action action) => _context.RunAsync(action);
        protected Task<T> OnUiAsync<T>(Func<T> action) => _context.RunAsync(action);
        protected void VerifyAccess() => _context.VerifyAccess();

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (_disposed) return false;
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (_disposed) return;
            _context.Post(() => { if (!_disposed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); });
        }

        protected virtual void OnDispose() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UiCleanup.All(() => _lifetime.Cancel(), OnDispose,
                () => _lifetime.Dispose(), () => PropertyChanged = null);
        }
    }

    public enum UiCollectionChangeAction
    {
        Add,
        Remove,
        Replace,
        Reset
    }

    public sealed class UiCollectionChangedEventArgs<T> : EventArgs
    {
        public UiCollectionChangeAction Action { get; }
        public int Index { get; }
        public T Item { get; }

        public UiCollectionChangedEventArgs(UiCollectionChangeAction action, int index, T item = default(T))
        { Action = action; Index = index; Item = item; }
    }

    /// <summary>Small observable list for explicit list bindings; it never polls the view model.</summary>
    public sealed class UiObservableList<T> : IReadOnlyList<T>
    {
        private readonly List<T> _items = new List<T>();
        public event EventHandler<UiCollectionChangedEventArgs<T>> Changed;
        public int Count => _items.Count;
        public T this[int index] => _items[index];

        public void Add(T item)
        {
            _items.Add(item);
            Changed?.Invoke(this, new UiCollectionChangedEventArgs<T>(UiCollectionChangeAction.Add, _items.Count - 1, item));
        }

        public bool Remove(T item)
        {
            int index = _items.IndexOf(item);
            if (index < 0) return false;
            T removed = _items[index];
            _items.RemoveAt(index);
            Changed?.Invoke(this, new UiCollectionChangedEventArgs<T>(UiCollectionChangeAction.Remove, index, removed));
            return true;
        }

        public void ReplaceAt(int index, T item)
        {
            if (index < 0 || index >= _items.Count) throw new ArgumentOutOfRangeException(nameof(index));
            _items[index] = item;
            Changed?.Invoke(this, new UiCollectionChangedEventArgs<T>(UiCollectionChangeAction.Replace, index, item));
        }

        public void ReplaceAll(IEnumerable<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items.Clear();
            _items.AddRange(items);
            Changed?.Invoke(this, new UiCollectionChangedEventArgs<T>(UiCollectionChangeAction.Reset, -1));
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            Changed?.Invoke(this, new UiCollectionChangedEventArgs<T>(UiCollectionChangeAction.Reset, -1));
        }

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface IUiCommand
    {
        bool CanExecute { get; }
        bool IsExecuting { get; }
        Exception Error { get; }
        event EventHandler StateChanged;
        Task ExecuteAsync();
        void Cancel();
        void NotifyCanExecuteChanged();
    }

    /// <summary>Immediate command with an explicit CanExecute predicate.</summary>
    public sealed class UiCommand : IUiCommand, IDisposable
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        private readonly UiExecutionContext _context;
        private bool _disposed;

        public UiCommand(Action execute, Func<bool> canExecute = null, IMainThreadDispatcher dispatcher = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _context = new UiExecutionContext(dispatcher);
        }

        public bool CanExecute => !_disposed && (_canExecute == null || _canExecute());
        public bool IsExecuting => false;
        public Exception Error { get; private set; }
        public event EventHandler StateChanged;

        public void Execute()
        {
            _context.VerifyAccess();
            if (!CanExecute) return;
            try
            {
                Error = null;
                _execute();
            }
            catch (Exception error)
            {
                Error = error;
                StateChanged?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }

        public Task ExecuteAsync()
        {
            return _context.RunAsync(Execute);
        }

        public void Cancel() { }
        public void NotifyCanExecuteChanged() => _context.Post(() => { if (!_disposed) StateChanged?.Invoke(this, EventArgs.Empty); });
        public void Dispose() { _disposed = true; StateChanged = null; }
    }

    /// <summary>
    /// Async command with one in-flight execution per instance. Calling ExecuteAsync again while
    /// running returns the same task, so a double click cannot start a second application action.
    /// </summary>
    public sealed class AsyncUiCommand : IUiCommand, IDisposable
    {
        private readonly object _gate = new object();
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool> _canExecute;
        private readonly UiExecutionContext _context;
        private CancellationTokenSource _cancellation;
        private Task _running;
        private bool _isExecuting;
        private bool _disposed;

        public AsyncUiCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null, IMainThreadDispatcher dispatcher = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _context = new UiExecutionContext(dispatcher);
        }

        public bool CanExecute
        {
            get
            {
                lock (_gate) return !_disposed && !_isExecuting && (_canExecute == null || _canExecute());
            }
        }

        public bool IsExecuting { get { lock (_gate) return _isExecuting; } }
        public Task Execution { get { lock (_gate) return _running ?? Task.CompletedTask; } }
        public Exception Error { get; private set; }
        public event EventHandler StateChanged;

        public Task ExecuteAsync()
        {
            TaskCompletionSource<bool> completion;
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(AsyncUiCommand)));
                if (_isExecuting) return _running;
                if (_canExecute != null && !_canExecute()) return Task.CompletedTask;
                cancellation = _cancellation = new CancellationTokenSource();
                _isExecuting = true;
                Error = null;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _running = completion.Task;
            }
            NotifyCanExecuteChanged();
            _ = RunAsync(cancellation, completion);
            return completion.Task;
        }

        private async Task RunAsync(CancellationTokenSource cancellation, TaskCompletionSource<bool> completion)
        {
            Exception failure = null;
            try
            {
                await _context.RunAsync(() =>
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    return _execute(cancellation.Token);
                }).Unwrap().ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                lock (_gate)
                {
                    _isExecuting = false;
                    if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                    Error = failure is OperationCanceledException ? null : failure;
                }
                cancellation.Dispose();
            }
            try { await _context.RunAsync(RaiseStateChanged).ConfigureAwait(false); }
            catch (Exception error) { if (failure == null) failure = error; }
            if (failure is OperationCanceledException) completion.TrySetCanceled();
            else if (failure != null) completion.TrySetException(failure);
            else completion.TrySetResult(true);
        }

        public void Cancel()
        {
            CancellationTokenSource cancellation;
            lock (_gate) cancellation = _cancellation;
            if (cancellation == null) return;
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void NotifyCanExecuteChanged() => _context.Post(RaiseStateChanged);

        private void RaiseStateChanged()
        {
            if (_disposed) return;
            var handlers = StateChanged;
            if (handlers == null) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception error) { System.Diagnostics.Trace.TraceError(error.ToString()); }
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                cancellation = _cancellation;
            }
            try { cancellation?.Cancel(); } catch (ObjectDisposedException) { }
            finally { StateChanged = null; }
        }
    }

    public interface IUiValueAdapter<T>
    {
        T Value { get; }
        event Action<T> ValueChanged;
        void SetValueWithoutNotify(T value);
    }

    public interface IUiCommandAdapter : IDisposable
    {
        event Action Clicked;
        void SetInteractable(bool interactable);
        void SetBusy(bool busy);
    }

    public sealed class UiBindingSet : IDisposable
    {
        private readonly List<IDisposable> _bindings = new List<IDisposable>();
        private bool _disposed;

        public T Add<T>(T binding) where T : IDisposable
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (_disposed) { binding.Dispose(); return binding; }
            _bindings.Add(binding);
            return binding;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var actions = new Action[_bindings.Count];
            for (int i = 0; i < actions.Length; i++) actions[i] = _bindings[_bindings.Count - 1 - i].Dispose;
            _bindings.Clear();
            UiCleanup.All(actions);
        }
    }

    public static class UiBinding
    {
        public static IDisposable OneWay<T>(INotifyPropertyChanged source, string propertyName,
            Func<T> read, Action<T> write)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("Property name is required.", nameof(propertyName));
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (write == null) throw new ArgumentNullException(nameof(write));
            var lease = new BindingLease();
            PropertyChangedEventHandler changed = (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.PropertyName) || string.Equals(args.PropertyName, propertyName, StringComparison.Ordinal))
                    lease.Run(() => write(read()));
            };
            lease.Initialize(() =>
            {
                lease.Own(() => source.PropertyChanged -= changed);
                source.PropertyChanged += changed;
                write(read());
            });
            return lease;
        }

        public static IDisposable TwoWay<T>(INotifyPropertyChanged source, string propertyName,
            Func<T> read, Action<T> write, IUiValueAdapter<T> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var lease = new BindingLease();
            bool updating = false;
            lease.Initialize(() =>
            {
                if (target is IDisposable ownedTarget) lease.Own(ownedTarget.Dispose);
                IDisposable oneWay = OneWay(source, propertyName, read, value =>
                {
                    if (updating) return;
                    updating = true;
                    try { target.SetValueWithoutNotify(value); } finally { updating = false; }
                });
                lease.Own(oneWay.Dispose);
                Action<T> changed = value => lease.Run(() =>
                {
                    if (updating) return;
                    updating = true;
                    try { write(value); } finally { updating = false; }
                    target.SetValueWithoutNotify(read()); // Reflect validation/normalization.
                });
                lease.Own(() => target.ValueChanged -= changed);
                target.ValueChanged += changed;
            });
            return lease;
        }

        public static IDisposable Collection<T>(UiObservableList<T> source, Action refresh)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));
            var lease = new BindingLease();
            EventHandler<UiCollectionChangedEventArgs<T>> changed = (sender, args) => lease.Run(refresh);
            lease.Initialize(() =>
            {
                lease.Own(() => source.Changed -= changed);
                source.Changed += changed;
                refresh();
            });
            return lease;
        }

        public static IDisposable Command(IUiCommand command, IUiCommandAdapter target)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (target == null) throw new ArgumentNullException(nameof(target));
            Action refresh = () =>
            {
                target.SetInteractable(command.CanExecute);
                target.SetBusy(command.IsExecuting);
            };
            var lease = new BindingLease();
            EventHandler stateChanged = (sender, args) => lease.Run(refresh);
            Action clicked = () => lease.Run(() => _ = ExecuteAndObserveAsync(command));
            lease.Initialize(() =>
            {
                lease.Own(target.Dispose);
                lease.Own(() => command.StateChanged -= stateChanged);
                command.StateChanged += stateChanged;
                lease.Own(() => target.Clicked -= clicked);
                target.Clicked += clicked;
                refresh();
            });
            return lease;
        }

        private static async Task ExecuteAndObserveAsync(IUiCommand command)
        {
            try { await command.ExecuteAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { /* AsyncUiCommand exposes Error; the view model owns user-facing text. */ }
        }

        private sealed class BindingLease : IDisposable
        {
            private readonly UiExecutionContext _context = new UiExecutionContext();
            private readonly List<Action> _cleanup = new List<Action>();
            private int _disposed;
            internal void Own(Action action) { _cleanup.Add(action); }
            internal void Run(Action action) => _context.Post(() => { if (Volatile.Read(ref _disposed) == 0) action(); });
            internal void Initialize(Action action)
            {
                _context.VerifyAccess();
                try { action(); }
                catch (Exception initial)
                {
                    try { Dispose(); } catch (Exception cleanup) { throw new AggregateException(initial, cleanup); }
                    throw;
                }
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _cleanup.Reverse();
                var actions = _cleanup.ToArray(); _cleanup.Clear();
                if (_context.HasAccess) UiCleanup.All(actions);
                else _context.Post(() => UiCleanup.All(actions));
            }
        }
    }
}
