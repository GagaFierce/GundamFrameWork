using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;

namespace WFrameWork.Scene
{
    public enum SceneLoadMode { Single, Additive }
    public enum SceneFlowState { None, Loading, Loaded, Unloading, Failed }

    public sealed class SceneLease : IDisposable
    {
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public string Key { get; }
        public object Scene { get; }
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }
        public bool IsReleaseCompleted { get { lock (_gate) return _releaseTask != null && _releaseTask.IsCompleted; } }
        public SceneLease(string key, object scene, Action release) : this(key, scene, () => { release?.Invoke(); return Task.CompletedTask; }) { }
        public SceneLease(string key, object scene, Func<Task> release) { Key = key; Scene = scene; _release = release; }
        public void Dispose() { _ = ReleaseAsync(); }

        public Task ReleaseAsync()
        {
            lock (_gate)
            {
                if (_releaseTask != null) return _releaseTask;
                var release = _release; _release = null;
                try { _releaseTask = release == null ? Task.CompletedTask : (release() ?? Task.CompletedTask); }
                catch (Exception error) { _releaseTask = Task.FromException(error); }
                return _releaseTask;
            }
        }
    }

    public interface ISceneBackend : IDisposable
    {
        // Completion includes any cleanup owned by the backend. SceneFlow cancels its
        // caller's wait separately and retains the underlying operation until it settles.
        Task<SceneLease> LoadAsync(string key, SceneLoadMode mode, IProgress<float> progress, CancellationToken token);
    }

    public sealed class SceneFlowService : IDisposable
    {
        private readonly ISceneBackend _backend;
        private readonly DiagnosticLogger _diagnostics;
        private readonly List<SceneLease> _loaded = new List<SceneLease>();
        private readonly object _gate = new object();
        private bool _disposed;
        private Task<SceneLease> _loading;
        private string _loadingKey;
        private Task _loadingCleanup;
        private bool _loadReserved;
        private SceneFlowState _state;
        private Task _shutdownTask;

        public SceneFlowService(ISceneBackend backend, IDiagnosticSink diagnostics = null)
        { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _diagnostics = new DiagnosticLogger("Scene", diagnostics); }
        public SceneFlowState State { get { lock (_gate) return _state; } }
        public string CurrentSceneKey { get { lock (_gate) return _loaded.Count == 0 ? null : _loaded[_loaded.Count - 1].Key; } }
        public string LoadingSceneKey { get { lock (_gate) return _loadingKey; } }
        public int LoadedSceneCount { get { lock (_gate) return _loaded.Count; } }
        public bool IsLoading => State == SceneFlowState.Loading;
        public bool IsClosing { get { lock (_gate) return _shutdownTask != null; } }

        public async Task WaitForIdleAsync()
        {
            while (true)
            {
                Task loading;
                Task cleanup;
                lock (_gate) { loading = _loading; cleanup = _loadingCleanup; }
                if (loading == null && cleanup == null) return;
                try { await (cleanup ?? loading).ConfigureAwait(false); } catch { }
                lock (_gate)
                {
                    if (ReferenceEquals(cleanup, _loadingCleanup) && cleanup != null)
                    {
                        _loadingCleanup = null; _loading = null; _loadingKey = null; _loadReserved = false;
                        return;
                    }
                    if (ReferenceEquals(loading, _loading) && loading != null && _loadingCleanup == null && _loading.IsCompleted)
                    { _loading = null; _loadingKey = null; _loadReserved = false; return; }
                }
            }
        }

        public async Task<SceneLease> LoadAsync(string key, SceneLoadMode mode = SceneLoadMode.Single,
            IProgress<float> progress = null, CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Scene key is required.", nameof(key));
            EnsureUsable();
            token.ThrowIfCancellationRequested();
            SceneLease[] previousScenes;
            lock (_gate)
            {
                if (_loadReserved || _loading != null || _loadingCleanup != null)
                    throw new InvalidOperationException("A scene load or its cleanup is already in progress.");
                token.ThrowIfCancellationRequested();
                _loadReserved = true;
                previousScenes = _loaded.ToArray();
            }
            if (mode == SceneLoadMode.Single)
            {
                try
                {
                    for (int i = 0; i < previousScenes.Length; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        await UnloadAsync(previousScenes[i]);
                    }
                    token.ThrowIfCancellationRequested();
                }
                catch { lock (_gate) _loadReserved = false; throw; }
            }
            Task<SceneLease> operation;
            lock (_gate)
            {
                _state = SceneFlowState.Loading;
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Admission was checked above. After starting, retain the native load
                    // through late cleanup instead of truncating its task with caller cancellation.
                    operation = _backend.LoadAsync(key.Trim(), mode, progress, CancellationToken.None);
                }
                catch { _state = SceneFlowState.Failed; _loadReserved = false; throw; }
                _loadReserved = false;
                _loading = operation; _loadingKey = key.Trim();
            }
            try
            {
                SceneLease lease = await AwaitWithCancellation(operation, token);
                if (_disposed) { await lease.ReleaseAsync().ConfigureAwait(false); throw new ObjectDisposedException(nameof(SceneFlowService)); }
                lock (_gate)
                {
                    _loaded.Add(lease); _state = SceneFlowState.Loaded; _loading = null; _loadingKey = null; _loadReserved = false;
                }
                return lease;
            }
            catch (Exception error)
            {
                bool canceled = error is OperationCanceledException;
                if (canceled)
                {
                    _loadingCleanup = FinishCanceledLoadAsync(operation);
                    _ = _loadingCleanup.ContinueWith(_ =>
                    {
                        lock (_gate) { if (ReferenceEquals(_loading, operation)) { _loading = null; _loadingKey = null; _loadingCleanup = null; } }
                    }, TaskScheduler.Default);
                }
                lock (_gate)
                {
                    _state = canceled ? SceneFlowState.None : SceneFlowState.Failed;
                    if (!canceled && ReferenceEquals(_loading, operation)) { _loading = null; _loadingKey = null; _loadReserved = false; }
                }
                if (canceled) _diagnostics.Warning("Scene load canceled: " + key);
                else _diagnostics.Error("Scene load failed: " + key, error);
                throw;
            }
        }

        private async Task UnloadOthersAsync(SceneLease keep)
        {
            SceneLease[] old;
            lock (_gate) { old = _loaded.FindAll(x => !ReferenceEquals(x, keep)).ToArray(); }
            for (int i = 0; i < old.Length; i++) { await UnloadAsync(old[i]); }
        }

        public async Task UnloadAsync(SceneLease lease)
        {
            if (lease == null) return;
            lock (_gate) { if (_loaded.Count == 0 && lease.IsReleased) return; _state = SceneFlowState.Unloading; }
            await lease.ReleaseAsync().ConfigureAwait(false);
            lock (_gate)
            {
                _loaded.Remove(lease);
                _state = _loaded.Count == 0 ? SceneFlowState.None : SceneFlowState.Loaded;
            }
        }

        private async Task FinishCanceledLoadAsync(Task<SceneLease> operation)
        {
            try
            {
                SceneLease lease = await operation;
                if (lease != null) await lease.ReleaseAsync();
            }
            catch (Exception error) { _diagnostics.Warning("Canceled scene load cleanup failed.", error); }
        }

        private static async Task<T> AwaitWithCancellation<T>(Task<T> operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!token.CanBeCanceled) return await operation.ConfigureAwait(false);
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (!ReferenceEquals(await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false), operation)) token.ThrowIfCancellationRequested();
                return await operation.ConfigureAwait(false);
            }
        }

        private void EnsureUsable() { if (_disposed) throw new ObjectDisposedException(nameof(SceneFlowService)); }
        public Task ShutdownAsync()
        {
            lock (_gate)
            {
                if (_shutdownTask != null) return _shutdownTask;
                _disposed = true;
                _shutdownTask = ShutdownCoreAsync();
                return _shutdownTask;
            }
        }

        private async Task ShutdownCoreAsync()
        {
            while (true)
            {
                Task<SceneLease> loading;
                Task cleanup;
                lock (_gate) { loading = _loading; cleanup = _loadingCleanup; }
                if (loading == null && cleanup == null) break;
                if (cleanup != null)
                {
                    try { await cleanup.ConfigureAwait(false); } catch { }
                    lock (_gate)
                    {
                        if (ReferenceEquals(cleanup, _loadingCleanup))
                        { _loadingCleanup = null; _loading = null; _loadingKey = null; _loadReserved = false; }
                    }
                    continue;
                }
                try
                {
                    SceneLease late = await loading.ConfigureAwait(false);
                    if (late != null) await late.ReleaseAsync().ConfigureAwait(false);
                }
                catch { }
                lock (_gate)
                {
                    if (ReferenceEquals(_loading, loading))
                    { _loading = null; _loadingKey = null; _loadReserved = false; }
                }
            }
            SceneLease[] loaded;
            lock (_gate) { loaded = _loaded.ToArray(); }
            List<Exception> errors = new List<Exception>();
            for (int i = loaded.Length - 1; i >= 0; i--)
            {
                try { await loaded[i].ReleaseAsync(); }
                catch (Exception error) { errors.Add(error); _diagnostics.Error("Scene unload failed: " + loaded[i].Key, error); }
            }
            lock (_gate) { _loaded.Clear(); _loading = null; _loadingKey = null; _loadingCleanup = null; _loadReserved = false; _state = errors.Count == 0 ? SceneFlowState.None : SceneFlowState.Failed; }
            _backend.Dispose();
            if (errors.Count > 0) throw new AggregateException("Scene flow shutdown failed.", errors);
        }

        public void Dispose() { _ = ShutdownAsync(); }
    }
}
