using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;

namespace WFrameWork.Application
{
    public enum RuntimeScopeState { Open, Closing, Closed, Failed }
    public enum RuntimeOwnership { Borrowed, Owned }

    /// <summary>
    /// A business lifetime boundary. Registration order is dependency order: children and
    /// later registrations close first, so users leave before the resources they use.
    /// </summary>
    public sealed class RuntimeScope : IDisposable
    {
        private sealed class Cleanup
        {
            internal readonly string Name;
            internal readonly Func<Task> Close;
            internal readonly RuntimeOwnership Ownership;
            internal Cleanup(string name, Func<Task> close, RuntimeOwnership ownership)
            { Name = name; Close = close; Ownership = ownership; }
        }

        private readonly object _gate = new object();
        private readonly List<Cleanup> _cleanups = new List<Cleanup>();
        private readonly List<Task> _operations = new List<Task>();
        private readonly List<RuntimeScope> _children = new List<RuntimeScope>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly DiagnosticLogger _diagnostics;
        private readonly RuntimeScope _parent;
        private Task _closeTask;
        private RuntimeScopeState _state = RuntimeScopeState.Open;
        private Exception _failure;

        public RuntimeScope(string name, RuntimeScope parent = null, IDiagnosticSink diagnostics = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Scope name is required.", nameof(name));
            Name = name.Trim(); _parent = parent;
            _diagnostics = new DiagnosticLogger("Scope." + Name, diagnostics);
            if (parent != null) parent.AddChild(this);
        }

        public string Name { get; }
        public RuntimeScopeState State { get { lock (_gate) return _state; } }
        public Exception Failure { get { lock (_gate) return _failure; } }
        public CancellationToken CancellationToken => _cancellation.Token;
        public bool IsClosing => State == RuntimeScopeState.Closing;
        public int ChildCount { get { lock (_gate) return _children.Count; } }

        public void CopyChildren(List<RuntimeScope> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            lock (_gate) { results.Clear(); results.AddRange(_children); }
        }

        public RuntimeScope CreateChild(string name)
        {
            EnsureOpen();
            return new RuntimeScope(name, this, _diagnosticsSink());
        }

        public void Track(Task operation, string name = "operation")
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            lock (_gate)
            {
                EnsureOpenLocked();
                _operations.Add(operation);
            }
            _ = Observe(operation, name);
        }

        public void Own(IDisposable disposable, string name = null)
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            OwnAsync(name ?? disposable.GetType().Name, () =>
            {
                disposable.Dispose();
                return Task.CompletedTask;
            });
        }

        public void Borrow(IDisposable disposable, string name = null)
        {
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            Register(name ?? disposable.GetType().Name, () => Task.CompletedTask, RuntimeOwnership.Borrowed);
        }

        public void OwnAsync(string name, Func<Task> close)
        { Register(name, close, RuntimeOwnership.Owned); }

        public void BorrowAsync(string name, Func<Task> close)
        { Register(name, close, RuntimeOwnership.Borrowed); }

        public void Register(string name, Func<Task> close, RuntimeOwnership ownership)
        {
            if (close == null) throw new ArgumentNullException(nameof(close));
            lock (_gate)
            {
                EnsureOpenLocked();
                _cleanups.Add(new Cleanup(string.IsNullOrWhiteSpace(name) ? "cleanup" : name, close, ownership));
            }
        }

        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _state = RuntimeScopeState.Closing;
                _cancellation.Cancel();
                _closeTask = CloseCoreAsync();
                return _closeTask;
            }
        }

        private async Task CloseCoreAsync()
        {
            List<Task> operations;
            List<Cleanup> cleanups;
            List<RuntimeScope> children;
            lock (_gate)
            {
                operations = new List<Task>(_operations);
                cleanups = new List<Cleanup>(_cleanups);
                children = new List<RuntimeScope>(_children);
            }
            List<Exception> errors = new List<Exception>();

            await AwaitAll(operations, errors, "operation");
            for (int i = children.Count - 1; i >= 0; i--)
            {
                try { await children[i].CloseAsync(); }
                catch (Exception error) { errors.Add(error); _diagnostics.Error("Child scope cleanup failed: " + children[i].Name, error); }
            }
            for (int i = cleanups.Count - 1; i >= 0; i--)
            {
                Cleanup cleanup = cleanups[i];
                if (cleanup.Ownership != RuntimeOwnership.Owned) continue;
                try { await cleanup.Close(); }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    errors.Add(error);
                    _diagnostics.Error("Scope cleanup failed: " + cleanup.Name, error);
                }
            }
            _cancellation.Dispose();
            lock (_gate)
            {
                _cleanups.Clear(); _operations.Clear();
                if (errors.Count == 0) _state = RuntimeScopeState.Closed;
                else { _failure = new AggregateException("Scope '" + Name + "' closed with errors.", errors); _state = RuntimeScopeState.Failed; }
            }
            if (errors.Count > 0) throw _failure;
        }

        private async Task Observe(Task operation, string name)
        {
            try { await operation; }
            catch (OperationCanceledException) { }
            catch (Exception error) { _diagnostics.Error("Tracked operation failed: " + name, error); }
        }

        private async Task AwaitAll(List<Task> operations, List<Exception> errors, string fallbackName)
        {
            for (int i = 0; i < operations.Count; i++)
            {
                try { await operations[i]; }
                catch (OperationCanceledException) { }
                catch (Exception error) { errors.Add(error); _diagnostics.Error("Tracked operation failed: " + fallbackName, error); }
            }
        }

        private IDiagnosticSink _diagnosticsSink() => new LoggerSink(_diagnostics);

        private sealed class LoggerSink : IDiagnosticSink
        {
            private readonly DiagnosticLogger _logger;
            internal LoggerSink(DiagnosticLogger logger) { _logger = logger; }
            public void Report(in DiagnosticEvent diagnostic)
            {
                if (diagnostic.Level == DiagnosticLevel.Error) _logger.Error(diagnostic.Message, diagnostic.Exception);
                else if (diagnostic.Level == DiagnosticLevel.Warning) _logger.Warning(diagnostic.Message, diagnostic.Exception);
                else _logger.Info(diagnostic.Message);
            }
        }

        private void EnsureOpen()
        { lock (_gate) EnsureOpenLocked(); }

        private void EnsureOpenLocked()
        {
            if (_state != RuntimeScopeState.Open) throw new InvalidOperationException("Scope '" + Name + "' is no longer open.");
        }

        private void AddChild(RuntimeScope child)
        {
            lock (_gate) { EnsureOpenLocked(); _children.Add(child); }
        }

        public void Dispose() { _ = CloseAsync(); }
    }
}
