using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Threading;

namespace WFrameWork.Threading.Unity
{
    /// <summary>
    /// Captures Unity's main-thread context explicitly at construction. All Unity adapters
    /// receive the same instance from the application root; they do not capture contexts on
    /// individual awaits.
    /// </summary>
    public sealed class UnityMainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
        private readonly SynchronizationContext _context;
        private bool _accepting = true;

        public UnityMainThreadDispatcher()
        {
            _context = SynchronizationContext.Current ?? throw new InvalidOperationException("Unity main-thread SynchronizationContext is not available.");
        }

        public bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _threadId;
        public bool IsAcceptingWork => _accepting;

        public Task RunAsync(Action action, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (!_accepting) return Task.FromException(new ObjectDisposedException(nameof(UnityMainThreadDispatcher)));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
            if (IsMainThread)
            {
                try { action(); return Task.CompletedTask; }
                catch (Exception error) { return Task.FromException(error); }
            }
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _context.Post(_ =>
            {
                if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
                try { action(); completion.TrySetResult(true); }
                catch (Exception error) { completion.TrySetException(error); }
            }, null);
            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<T> function, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (!_accepting) return Task.FromException<T>(new ObjectDisposedException(nameof(UnityMainThreadDispatcher)));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
            if (IsMainThread)
            {
                try { return Task.FromResult(function()); }
                catch (Exception error) { return Task.FromException<T>(error); }
            }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _context.Post(_ =>
            {
                if (cancellationToken.IsCancellationRequested) { completion.TrySetCanceled(cancellationToken); return; }
                try { completion.TrySetResult(function()); }
                catch (Exception error) { completion.TrySetException(error); }
            }, null);
            return completion.Task;
        }

        public void StopAcceptingWork() { _accepting = false; }
    }

    /// <summary>One-frame-safe destruction barrier for Unity objects.</summary>
    public static class UnityObjectLifetime
    {
        private sealed class Watcher : MonoBehaviour
        {
            private static readonly System.Collections.Generic.List<Tuple<UnityEngine.Object, TaskCompletionSource<bool>>> Pending =
                new System.Collections.Generic.List<Tuple<UnityEngine.Object, TaskCompletionSource<bool>>>();
            private static Watcher _instance;

            internal static Task WaitForDestroyed(UnityEngine.Object target)
            {
                if (target == null) return Task.CompletedTask;
                Ensure();
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Pending.Add(Tuple.Create(target, completion));
                return completion.Task;
            }

            internal static Task DestroyAndWait(UnityEngine.Object target)
            {
                if (target == null) return Task.CompletedTask;
                Ensure();
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Pending.Add(Tuple.Create(target, completion));
                UnityEngine.Object.Destroy(target);
                return completion.Task;
            }

            private static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("GFramework.UnityAsyncDriver");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<Watcher>();
            }

            private void Update()
            {
                for (int i = Pending.Count - 1; i >= 0; i--)
                {
                    var item = Pending[i];
                    if (item.Item1 == null) { Pending.RemoveAt(i); item.Item2.TrySetResult(true); }
                }
            }
        }

        public static Task WaitForDestroyed(UnityEngine.Object target) => Watcher.WaitForDestroyed(target);
        public static Task DestroyAndWait(UnityEngine.Object target) => Watcher.DestroyAndWait(target);
    }
}
