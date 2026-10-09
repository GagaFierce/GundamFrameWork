using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace WFrameWork.UI
{
    /// <summary>Base for UI state objects. It owns only UI-lifetime cancellation and subscriptions.</summary>
    public abstract class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _disposed;

        public event PropertyChangedEventHandler PropertyChanged;
        public bool IsDisposed => _disposed;
        public CancellationToken LifetimeToken => _lifetime.Token;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (_disposed) return false;
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (_disposed) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected virtual void OnDispose() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            OnDispose();
            _lifetime.Dispose();
            PropertyChanged = null;
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
        private bool _disposed;

        public UiCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute => !_disposed && (_canExecute == null || _canExecute());
        public bool IsExecuting => false;
        public Exception Error { get; private set; }
        public event EventHandler StateChanged;

        public void Execute()
        {
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
            try { Execute(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }

        public void Cancel() { }
        public void NotifyCanExecuteChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
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
        private CancellationTokenSource _cancellation;
        private Task _running;
        private bool _isExecuting;
        private bool _disposed;

        public AsyncUiCommand(Func<CancellationToken, Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute
        {
            get
            {
                lock (_gate) return !_disposed && !_isExecuting && (_canExecute == null || _canExecute());
            }
        }

        public bool IsExecuting { get { lock (_gate) return _isExecuting; } }
        public Exception Error { get; private set; }
        public event EventHandler StateChanged;

        public Task ExecuteAsync()
        {
            Task result;
            lock (_gate)
            {
                if (_disposed) return Task.FromException(new ObjectDisposedException(nameof(AsyncUiCommand)));
                if (_isExecuting) return _running;
                if (_canExecute != null && !_canExecute()) return Task.CompletedTask;
                _cancellation = new CancellationTokenSource();
                _isExecuting = true;
                Error = null;
                _running = RunAsync(_cancellation);
                result = _running;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
            return result;
        }

        private async Task RunAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await _execute(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                Error = error;
                StateChanged?.Invoke(this, EventArgs.Empty);
                throw;
            }
            finally
            {
                lock (_gate)
                {
                    _isExecuting = false;
                    if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Cancel()
        {
            CancellationTokenSource cancellation;
            lock (_gate) cancellation = _cancellation;
            if (cancellation == null) return;
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void NotifyCanExecuteChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

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
            StateChanged = null;
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
            for (int i = _bindings.Count - 1; i >= 0; i--) _bindings[i].Dispose();
            _bindings.Clear();
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
            PropertyChangedEventHandler changed = (sender, args) =>
            {
                if (string.IsNullOrEmpty(args.PropertyName) || string.Equals(args.PropertyName, propertyName, StringComparison.Ordinal))
                    write(read());
            };
            source.PropertyChanged += changed;
            write(read());
            return new ActionDisposable(() => source.PropertyChanged -= changed);
        }

        public static IDisposable TwoWay<T>(INotifyPropertyChanged source, string propertyName,
            Func<T> read, Action<T> write, IUiValueAdapter<T> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            bool updating = false;
            IDisposable oneWay = OneWay(source, propertyName, read, value =>
            {
                if (updating) return;
                updating = true;
                try { target.SetValueWithoutNotify(value); } finally { updating = false; }
            });
            Action<T> changed = value =>
            {
                if (updating) return;
                updating = true;
                try { write(value); } finally { updating = false; }
            };
            target.ValueChanged += changed;
            var unsubscribe = new ActionDisposable(() => target.ValueChanged -= changed);
            var disposableTarget = target as IDisposable;
            return disposableTarget == null
                ? new CompositeDisposable(oneWay, unsubscribe)
                : new CompositeDisposable(oneWay, unsubscribe, disposableTarget);
        }

        public static IDisposable Collection<T>(UiObservableList<T> source, Action refresh)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (refresh == null) throw new ArgumentNullException(nameof(refresh));
            EventHandler<UiCollectionChangedEventArgs<T>> changed = (sender, args) => refresh();
            source.Changed += changed;
            refresh();
            return new ActionDisposable(() => source.Changed -= changed);
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
            EventHandler stateChanged = (sender, args) => refresh();
            Action clicked = () => _ = ExecuteAndObserveAsync(command);
            command.StateChanged += stateChanged;
            target.Clicked += clicked;
            refresh();
            return new CompositeDisposable(
                new ActionDisposable(() => command.StateChanged -= stateChanged),
                new ActionDisposable(() => target.Clicked -= clicked), target);
        }

        private static async Task ExecuteAndObserveAsync(IUiCommand command)
        {
            try { await command.ExecuteAsync().ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch { /* AsyncUiCommand exposes Error; the view model owns user-facing text. */ }
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action _dispose;
            public ActionDisposable(Action dispose) { _dispose = dispose; }
            public void Dispose() { var action = Interlocked.Exchange(ref _dispose, null); action?.Invoke(); }
        }

        private sealed class CompositeDisposable : IDisposable
        {
            private readonly IDisposable[] _items;
            private int _disposed;
            public CompositeDisposable(params IDisposable[] items) { _items = items; }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                for (int i = _items.Length - 1; i >= 0; i--) _items[i]?.Dispose();
            }
        }
    }
}
