using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;

namespace WFrameWork.Core.ResLoad
{
    public sealed class ResourceBackendAsset
    {
        public object Asset { get; }
        private Action _release;
        private Func<Task> _releaseAsync;
        private Task _releaseTask;
        public bool IsReleased { get; private set; }

        public ResourceBackendAsset(object asset, Action release)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _release = release;
            _releaseAsync = () => { var action = _release; action?.Invoke(); return Task.CompletedTask; };
        }

        public ResourceBackendAsset(object asset, Func<Task> releaseAsync)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _releaseAsync = releaseAsync ?? (() => Task.CompletedTask);
        }

        public void Release()
        {
            if (IsReleased) return;
            IsReleased = true;
            var release = _release; _release = null;
            var releaseAsync = _releaseAsync; _releaseAsync = null;
            if (release != null) release(); else _releaseTask = releaseAsync == null ? Task.CompletedTask : releaseAsync();
        }

        public Task ReleaseAsync()
        {
            if (IsReleased) return _releaseTask ?? Task.CompletedTask;
            IsReleased = true;
            var release = _release; _release = null;
            var releaseAsync = _releaseAsync; _releaseAsync = null;
            if (release != null) { release(); _releaseTask = Task.CompletedTask; return _releaseTask; }
            _releaseTask = releaseAsync == null ? Task.CompletedTask : releaseAsync();
            return _releaseTask;
        }
    }

    public sealed class ResourceBackendInstance
    {
        public object Instance { get; }
        private Action _release;
        private Func<Task> _releaseAsync;
        private Task _releaseTask;
        public bool IsReleased { get; private set; }

        public ResourceBackendInstance(object instance, Action release)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _release = release;
            _releaseAsync = () => { var action = _release; action?.Invoke(); return Task.CompletedTask; };
        }

        public void Release()
        {
            if (IsReleased) return;
            IsReleased = true;
            var release = _release; _release = null;
            var releaseAsync = _releaseAsync; _releaseAsync = null;
            if (release != null) release(); else _releaseTask = releaseAsync == null ? Task.CompletedTask : releaseAsync();
        }

        public Task ReleaseAsync()
        {
            if (IsReleased) return _releaseTask ?? Task.CompletedTask;
            IsReleased = true;
            var release = _release; _release = null;
            var releaseAsync = _releaseAsync; _releaseAsync = null;
            if (release != null) { release(); _releaseTask = Task.CompletedTask; return _releaseTask; }
            _releaseTask = releaseAsync == null ? Task.CompletedTask : releaseAsync();
            return _releaseTask;
        }
    }

    public interface IResourceBackend : IDisposable
    {
        Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken cancellationToken);
        Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken cancellationToken);
    }

    public sealed class ResourceLease<T> : IDisposable where T : class
    {
        private Action _release;
        public T Asset { get; }
        public bool IsReleased { get; private set; }

        internal ResourceLease(T asset, Action release) { Asset = asset; _release = release; }
        public void Dispose()
        {
            if (IsReleased) return;
            IsReleased = true;
            var release = _release; _release = null;
            release?.Invoke();
        }

        public Task DisposeAsync()
        {
            if (IsReleased) return Task.CompletedTask;
            Dispose();
            return Task.CompletedTask;
        }
    }

    public sealed class ResourceInstanceLease : IDisposable
    {
        private Action _release;
        public object Instance { get; }
        public bool IsReleased { get; private set; }

        internal ResourceInstanceLease(object instance, Action release) { Instance = instance; _release = release; }
        public void Dispose()
        {
            if (IsReleased) return;
            IsReleased = true;
            var release = _release; _release = null;
            release?.Invoke();
        }

        public Task DisposeAsync()
        {
            if (IsReleased) return Task.CompletedTask;
            Dispose();
            return Task.CompletedTask;
        }
    }

    public sealed class ResourceService : IDisposable
    {
        private sealed class AssetEntry
        {
            internal readonly string Key; internal readonly Type Type; internal readonly Task<ResourceBackendAsset> Load;
            internal ResourceBackendAsset Result; internal int Holders; internal bool Removed; internal bool Released;
            internal AssetEntry(string key, Type type, Task<ResourceBackendAsset> load) { Key = key; Type = type; Load = load; }
        }

        private readonly object _gate = new object();
        private readonly IResourceBackend _backend;
        private readonly Dictionary<string, AssetEntry> _assets = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        private readonly HashSet<Task> _pendingLoads = new HashSet<Task>();
        private readonly HashSet<Task> _pendingInstances = new HashSet<Task>();
        private readonly DiagnosticLogger _diagnostics;
        private bool _disposed;
        private Task _closeTask;
        private TaskCompletionSource<bool> _leaseSignal = NewSignal();
        private int _activeInstances;
        private TaskCompletionSource<bool> _instanceSignal = NewSignal();

        public ResourceService(IResourceBackend backend, IDiagnosticSink diagnostics = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnostics = new DiagnosticLogger("ResLoad", diagnostics);
        }

        public bool IsDisposed => _disposed;
        public int ActiveAssetEntryCount { get { lock (_gate) return _assets.Count; } }
        public int ActiveAssetLeaseCount
        {
            get { lock (_gate) { int count = 0; foreach (var pair in _assets) count += pair.Value.Holders; return count; } }
        }

        public Task<ResourceLease<T>> LoadAssetAsync<T>(string key, IProgress<float> progress = null, CancellationToken cancellationToken = default(CancellationToken))
            where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Resource key is required.", nameof(key));
            EnsureUsable();
            AssetEntry entry;
            lock (_gate)
            {
                EnsureUsable();
                string cacheKey = key.Trim() + "\n" + typeof(T).AssemblyQualifiedName;
                if (!_assets.TryGetValue(cacheKey, out entry))
                {
                    Task<ResourceBackendAsset> load;
                    try { load = _backend.LoadAssetAsync(key.Trim(), typeof(T), progress, CancellationToken.None); }
                    catch (Exception error) { return Task.FromException<ResourceLease<T>>(error); }
                    entry = new AssetEntry(cacheKey, typeof(T), load);
                    _assets.Add(cacheKey, entry);
                    entry.Holders++;
                    TrackPendingLoad(load);
                    _ = ObserveAssetLoad(entry);
                }
                else entry.Holders++;
            }
            return AwaitAssetLease<T>(entry, cancellationToken);
        }

        public Task<ResourceInstanceLease> InstantiateAsync(string key, object parent = null, bool worldPositionStays = false,
            IProgress<float> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Resource key is required.", nameof(key));
            EnsureUsable();
            Task<ResourceBackendInstance> operation;
            try { operation = _backend.InstantiateAsync(key.Trim(), parent, worldPositionStays, progress, CancellationToken.None); }
            catch (Exception error) { return Task.FromException<ResourceInstanceLease>(error); }
            TrackPendingInstance(operation);
            return AwaitInstanceLease(operation, cancellationToken);
        }

        private async Task<ResourceLease<T>> AwaitAssetLease<T>(AssetEntry entry, CancellationToken token) where T : class
        {
            bool released = false;
            try
            {
                if (!await WaitForCompletion(entry.Load, token).ConfigureAwait(false))
                {
                    ReleaseHolder(entry); released = true;
                    token.ThrowIfCancellationRequested();
                }
                ResourceBackendAsset loaded = await entry.Load.ConfigureAwait(false);
                if (_disposed || token.IsCancellationRequested)
                {
                    ReleaseHolder(entry); released = true;
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(ResourceService));
                }
                var typed = loaded.Asset as T;
                if (typed == null)
                {
                    ReleaseHolder(entry); released = true;
                    throw new InvalidCastException("Addressable asset type mismatch. Requested " + typeof(T).FullName + ".");
                }
                return new ResourceLease<T>(typed, () => ReleaseHolder(entry));
            }
            catch
            {
                if (!released) ReleaseHolder(entry);
                throw;
            }
        }

        private async Task<ResourceInstanceLease> AwaitInstanceLease(Task<ResourceBackendInstance> operation, CancellationToken token)
        {
            if (!await WaitForCompletion(operation, token).ConfigureAwait(false))
            {
                _ = operation.ContinueWith(completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion) completed.Result.Release();
                }, TaskScheduler.Default);
                token.ThrowIfCancellationRequested();
            }
            ResourceBackendInstance loaded = await operation.ConfigureAwait(false);
            if (token.IsCancellationRequested || _disposed)
            {
                loaded.Release();
                if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                throw new ObjectDisposedException(nameof(ResourceService));
            }
            Interlocked.Increment(ref _activeInstances);
            return new ResourceInstanceLease(loaded.Instance, () =>
            {
                loaded.Release();
                if (Interlocked.Decrement(ref _activeInstances) == 0) _instanceSignal.TrySetResult(true);
            });
        }

        private static async Task<bool> WaitForCompletion(Task operation, CancellationToken token)
        {
            if (!token.CanBeCanceled) { await operation.ConfigureAwait(false); return true; }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                Task completed = await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false);
                return ReferenceEquals(completed, operation);
            }
        }

        private async Task ObserveAssetLoad(AssetEntry entry)
        {
            try { entry.Result = await entry.Load.ConfigureAwait(false); }
            catch (Exception error)
            {
                _diagnostics.Error("Asset load failed: " + entry.Key, error);
                lock (_gate) { entry.Removed = true; RemoveIfCurrent(entry); }
                return;
            }
            lock (_gate)
            {
                if (entry.Removed || _disposed || entry.Holders == 0) ReleaseEntry(entry);
            }
        }

        private void ReleaseHolder(AssetEntry entry)
        {
            lock (_gate)
            {
                if (entry.Holders > 0) entry.Holders--;
                if (_disposed && entry.Holders == 0) _leaseSignal.TrySetResult(true);
                if (entry.Holders == 0 && entry.Load.IsCompleted)
                {
                    entry.Removed = true;
                    RemoveIfCurrent(entry);
                    ReleaseEntry(entry);
                }
                else if (entry.Holders == 0 && !entry.Load.IsCompleted)
                {
                    entry.Removed = true;
                    RemoveIfCurrent(entry);
                }
            }
        }

        private void RemoveIfCurrent(AssetEntry entry)
        {
            if (_assets.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _assets.Remove(entry.Key);
        }

        private void TrackPendingLoad(Task load)
        {
            lock (_gate) _pendingLoads.Add(load);
            _ = load.ContinueWith(_ =>
            {
                lock (_gate)
                {
                    _pendingLoads.Remove(load);
                    if (_disposed) _leaseSignal.TrySetResult(true);
                }
            }, TaskScheduler.Default);
        }

        private void TrackPendingInstance(Task operation)
        {
            lock (_gate) _pendingInstances.Add(operation);
            _ = operation.ContinueWith(_ =>
            {
                lock (_gate)
                {
                    _pendingInstances.Remove(operation);
                    if (_disposed) _instanceSignal.TrySetResult(true);
                }
            }, TaskScheduler.Default);
        }

        private static void ReleaseEntry(AssetEntry entry)
        {
            if (entry.Released) return;
            entry.Released = true;
            entry.Result?.Release();
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ResourceService));
        }

        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _disposed = true;
                _closeTask = CloseCoreAsync();
                return _closeTask;
            }
        }

        private async Task CloseCoreAsync()
        {
            while (true)
            {
                Task[] pending;
                Task signal;
                Task instanceSignal;
                List<AssetEntry> releaseEntries = null;
                lock (_gate)
                {
                    var wait = new List<Task>();
                    bool hasActiveLeases = false;
                    foreach (var load in _pendingLoads) if (!load.IsCompleted) wait.Add(load);
                    foreach (var instance in _pendingInstances) if (!instance.IsCompleted) wait.Add(instance);
                    foreach (var pair in _assets)
                    {
                        pair.Value.Removed = true;
                        if (!pair.Value.Load.IsCompleted) wait.Add(pair.Value.Load);
                        if (pair.Value.Holders > 0) hasActiveLeases = true;
                    }
                    if (wait.Count == 0 && !hasActiveLeases && Volatile.Read(ref _activeInstances) == 0)
                    {
                        releaseEntries = new List<AssetEntry>(_assets.Values);
                        _assets.Clear();
                    }
                    pending = wait.ToArray(); signal = _leaseSignal.Task; instanceSignal = _instanceSignal.Task;
                }
                if (releaseEntries != null)
                {
                    for (int i = 0; i < releaseEntries.Count; i++)
                        if (releaseEntries[i].Result != null) await releaseEntries[i].Result.ReleaseAsync();
                    break;
                }
                var tasks = new Task[pending.Length + 2];
                Array.Copy(pending, tasks, pending.Length); tasks[tasks.Length - 2] = signal; tasks[tasks.Length - 1] = instanceSignal;
                await Task.WhenAny(tasks);
                lock (_gate)
                {
                    if (ReferenceEquals(signal, _leaseSignal.Task) && signal.IsCompleted)
                        _leaseSignal = NewSignal();
                    if (ReferenceEquals(instanceSignal, _instanceSignal.Task) && instanceSignal.IsCompleted)
                        _instanceSignal = NewSignal();
                }
            }
            _backend.Dispose();
        }

        private static TaskCompletionSource<bool> NewSignal()
        { return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); }

        public void Dispose() { _ = CloseAsync(); }
    }
}
