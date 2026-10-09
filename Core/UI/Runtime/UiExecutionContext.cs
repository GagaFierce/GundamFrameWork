using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Threading;

namespace WFrameWork.UI
{
    // No update loop or global dispatcher: use the injected application dispatcher or
    // the owner's captured context. Context-free hosts remain usable in pure C# tests.
    internal sealed class UiExecutionContext
    {
        private readonly IMainThreadDispatcher _dispatcher;
        private readonly SynchronizationContext _context = SynchronizationContext.Current;
        private readonly int _owner = Thread.CurrentThread.ManagedThreadId;
        internal UiExecutionContext(IMainThreadDispatcher dispatcher = null) { _dispatcher = dispatcher; }
        internal bool HasAccess => _dispatcher != null ? _dispatcher.IsMainThread :
            _context == null || Thread.CurrentThread.ManagedThreadId == _owner;
        internal void VerifyAccess()
        { if (!HasAccess) throw new InvalidOperationException("Create and edit UI bindings on their owning thread."); }
        internal Task RunAsync(Action action) => RunAsync(() => { action(); return true; });
        internal Task<T> RunAsync<T>(Func<T> action)
        {
            if (_dispatcher != null) return _dispatcher.RunAsync(action);
            if (HasAccess)
            {
                try { return Task.FromResult(action()); }
                catch (Exception error) { return Task.FromException<T>(error); }
            }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _context.Post(_ =>
            {
                try { completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
            }, null);
            return completion.Task;
        }
        internal void Post(Action action) { _ = Observe(RunAsync(action)); }
        private static async Task Observe(Task task)
        { try { await task.ConfigureAwait(false); } catch (Exception error) { Trace.TraceError(error.ToString()); } }
    }

    internal static class UiCleanup
    {
        internal static void All(params Action[] actions)
        {
            List<Exception> errors = null;
            foreach (var action in actions)
            {
                try { action?.Invoke(); }
                catch (Exception error) { if (errors == null) errors = new List<Exception>(); errors.Add(error); }
            }
            if (errors != null) throw new AggregateException("UI cleanup failed.", errors);
        }
    }
}
