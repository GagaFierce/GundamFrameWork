using System;
using System.Threading;
using System.Threading.Tasks;

namespace WFrameWork.Threading
{
    /// <summary>
    /// Explicit boundary for work that must execute on the Unity thread. The implementation
    /// is intentionally small so runtime code cannot silently depend on a captured context.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        bool IsMainThread { get; }
        bool IsAcceptingWork { get; }
        Task RunAsync(Action action, CancellationToken cancellationToken = default(CancellationToken));
        Task<T> RunAsync<T>(Func<T> function, CancellationToken cancellationToken = default(CancellationToken));
        void StopAcceptingWork();
    }

    /// <summary>Deterministic dispatcher for pure C# tests and non-Unity hosts.</summary>
    public sealed class InlineMainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private bool _accepting = true;

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _threadId;
        public bool IsAcceptingWork => _accepting;

        public Task RunAsync(Action action, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!_accepting) return Task.FromException(new ObjectDisposedException(nameof(InlineMainThreadDispatcher)));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            if (!IsMainThread) return Task.FromException(new InvalidOperationException("The operation must be dispatched to the main thread."));
            try { action(); return Task.CompletedTask; }
            catch (Exception error) { return Task.FromException(error); }
        }

        public Task<T> RunAsync<T>(Func<T> function, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (!_accepting) return Task.FromException<T>(new ObjectDisposedException(nameof(InlineMainThreadDispatcher)));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
            if (!IsMainThread) return Task.FromException<T>(new InvalidOperationException("The operation must be dispatched to the main thread."));
            try { return Task.FromResult(function()); }
            catch (Exception error) { return Task.FromException<T>(error); }
        }

        public void StopAcceptingWork() { _accepting = false; }
    }
}
