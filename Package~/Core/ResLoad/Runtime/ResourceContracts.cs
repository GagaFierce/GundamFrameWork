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
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }

        public ResourceBackendAsset(object asset, Action release)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _release = () => { release?.Invoke(); return Task.CompletedTask; };
        }

        public ResourceBackendAsset(object asset, Func<Task> releaseAsync)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _release = releaseAsync ?? (() => Task.CompletedTask);
        }

        public void Release() { _ = ReleaseAsync(); }

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

    public sealed class ResourceBackendInstance
    {
        public object Instance { get; }
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }

        public ResourceBackendInstance(object instance, Action release)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _release = () => { release?.Invoke(); return Task.CompletedTask; };
        }

        public ResourceBackendInstance(object instance, Func<Task> releaseAsync, bool asynchronousRelease)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _release = releaseAsync ?? (() => Task.CompletedTask);
        }

        public void Release() { _ = ReleaseAsync(); }

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

    public interface IResourceBackend : IDisposable
    {
        Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken cancellationToken);
        Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken cancellationToken);
    }

    public sealed class ResourceLease<T> : IDisposable where T : class
    {
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public T Asset { get; }
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }

        internal ResourceLease(T asset, Action release) : this(asset, () => { release?.Invoke(); return Task.CompletedTask; }) { }
        internal ResourceLease(T asset, Func<Task> release) { Asset = asset ?? throw new ArgumentNullException(nameof(asset)); _release = release; }
        public void Dispose() { _ = DisposeAsync(); }

        public Task DisposeAsync()
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

    public sealed class ResourceInstanceLease : IDisposable
    {
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public object Instance { get; }
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }

        internal ResourceInstanceLease(object instance, Action release) : this(instance, () => { release?.Invoke(); return Task.CompletedTask; }) { }
        internal ResourceInstanceLease(object instance, Func<Task> release) { Instance = instance ?? throw new ArgumentNullException(nameof(instance)); _release = release; }
        public void Dispose() { _ = DisposeAsync(); }

        public Task DisposeAsync()
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

    public sealed class ResourceService : IDisposable
    {
        private sealed class AssetEntry
        {
            internal readonly string Key; internal readonly Type Type; internal readonly Task<ResourceBackendAsset> Load;
            // Publish the result before a caller can release its lease. The lifetime stays
            // registered through observation, all holders, and the actual backend release.
            internal readonly TaskCompletionSource<ResourceBackendAsset> Ready =
                new TaskCompletionSource<ResourceBackendAsset>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Lifetime = NewSignal();
            internal ResourceBackendAsset Result; internal int Holders; internal bool Removed; internal bool Released; internal Task ReleaseTask;
            internal AssetEntry(string key, Type type, Task<ResourceBackendAsset> load) { Key = key; Type = type; Load = load; }
        }

        private sealed class ReleaseObservation
        {
            internal readonly Task Task;
            internal bool FailureRecorded;
            internal ReleaseObservation(Task task) { Task = task; }
        }

        private readonly object _gate = new object();
        private readonly IResourceBackend _backend;
        private readonly Dictionary<string, AssetEntry> _assets = new Dictionary<string, AssetEntry>(StringComparer.Ordinal);
        private readonly HashSet<AssetEntry> _assetLifetimes = new HashSet<AssetEntry>();
        private readonly HashSet<Task> _pendingInstances = new HashSet<Task>();
        private readonly HashSet<Task> _pendingReleases = new HashSet<Task>();
        private readonly Dictionary<Task, ReleaseObservation> _releaseObservations = new Dictionary<Task, ReleaseObservation>();
        private readonly List<Exception> _releaseFailures = new List<Exception>();
        private const int MaxRememberedReleaseFailures = 32;
        private readonly DiagnosticLogger _diagnostics;
        private bool _disposed;
        private Task _closeTask;
        private TaskCompletionSource<bool> _leaseSignal = NewSignal();
        private int _activeInstances;
        private TaskCompletionSource<bool> _instanceSignal = NewSignal();
        private TaskCompletionSource<bool> _activitySignal = NewSignal();

        public ResourceService(IResourceBackend backend, IDiagnosticSink diagnostics = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnostics = new DiagnosticLogger("ResLoad", diagnostics);
        }

        public bool IsDisposed { get { lock (_gate) return _disposed; } }
        public int ActiveAssetEntryCount { get { lock (_gate) return _assets.Count; } }
        public int ActiveAssetLeaseCount
        {
            get { lock (_gate) { int count = 0; foreach (var pair in _assets) count += pair.Value.Holders; return count; } }
        }
        public int ActiveInstanceLeaseCount => Volatile.Read(ref _activeInstances);
        public int PendingOperationCount
        {
            get
            {
                lock (_gate)
                {
                    int count = 0;
                    foreach (var asset in _assetLifetimes)
                        if (!asset.Ready.Task.IsCompleted || (asset.Holders == 0 && asset.ReleaseTask == null)) count++;
                    foreach (var instance in _pendingInstances) if (!instance.IsCompleted) count++;
                    foreach (var release in _pendingReleases) if (!release.IsCompleted) count++;
                    return count;
                }
            }
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
                    _assetLifetimes.Add(entry);
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
            var lifecycle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                EnsureUsable();
                _pendingInstances.Add(lifecycle.Task);
            }
            Task<ResourceBackendInstance> operation;
            try { operation = _backend.InstantiateAsync(key.Trim(), parent, worldPositionStays, progress, CancellationToken.None); }
            catch (Exception error)
            {
                CompleteInstanceLifecycle(lifecycle);
                return Task.FromException<ResourceInstanceLease>(error);
            }
            return AwaitInstanceLease(operation, cancellationToken, lifecycle);
        }

        private async Task<ResourceLease<T>> AwaitAssetLease<T>(AssetEntry entry, CancellationToken token) where T : class
        {
            bool released = false;
            try
            {
                if (!await WaitForCompletion(entry.Ready.Task, token).ConfigureAwait(false))
                {
                    released = true;
                    await ReleaseHolderAsync(entry).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                ResourceBackendAsset loaded = await entry.Ready.Task.ConfigureAwait(false);
                if (_disposed || token.IsCancellationRequested)
                {
                    released = true;
                    await ReleaseHolderAsync(entry).ConfigureAwait(false);
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(ResourceService));
                }
                var typed = loaded.Asset as T;
                if (typed == null)
                {
                    released = true;
                    await ReleaseHolderAsync(entry).ConfigureAwait(false);
                    throw new InvalidCastException("Addressable asset type mismatch. Requested " + typeof(T).FullName + ".");
                }
                return new ResourceLease<T>(typed, () => ReleaseHolderAsync(entry));
            }
            catch
            {
                if (!released) await ReleaseHolderAsync(entry).ConfigureAwait(false);
                throw;
            }
        }

        private async Task<ResourceInstanceLease> AwaitInstanceLease(Task<ResourceBackendInstance> operation,
            CancellationToken token, TaskCompletionSource<bool> lifecycle)
        {
            bool cleanupStarted = false;
            try
            {
                bool completed;
                try { completed = await WaitForCompletion(operation, token).ConfigureAwait(false); }
                catch
                {
                    CompleteInstanceLifecycle(lifecycle);
                    throw;
                }
                if (!completed)
                {
                    cleanupStarted = true;
                    _ = CleanupCanceledInstanceAsync(operation, lifecycle);
                    token.ThrowIfCancellationRequested();
                }
                ResourceBackendInstance loaded = await operation.ConfigureAwait(false);
                if (token.IsCancellationRequested || IsDisposed)
                {
                    await ReleaseInstanceForCleanupAsync(loaded).ConfigureAwait(false);
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(ResourceService));
                }
                Interlocked.Increment(ref _activeInstances);
                CompleteInstanceLifecycle(lifecycle);
                return new ResourceInstanceLease(loaded.Instance, () => ReleaseInstanceLeaseAsync(loaded));
            }
            catch
            {
                if (!cleanupStarted) CompleteInstanceLifecycle(lifecycle);
                throw;
            }
        }

        private async Task CleanupCanceledInstanceAsync(Task<ResourceBackendInstance> operation,
            TaskCompletionSource<bool> lifecycle)
        {
            try
            {
                ResourceBackendInstance loaded = await operation.ConfigureAwait(false);
                await ReleaseInstanceForCleanupAsync(loaded).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (!(error is OperationCanceledException)) _diagnostics.Error("Canceled instance cleanup failed.", error);
            }
            finally { CompleteInstanceLifecycle(lifecycle); }
        }

        private async Task ReleaseInstanceForCleanupAsync(ResourceBackendInstance loaded)
        {
            Task release = loaded.ReleaseAsync();
            TrackPendingRelease(release);
            await release.ConfigureAwait(false);
        }

        private async Task ReleaseInstanceLeaseAsync(ResourceBackendInstance loaded)
        {
            try { await ReleaseInstanceForCleanupAsync(loaded).ConfigureAwait(false); }
            finally
            {
                if (Interlocked.Decrement(ref _activeInstances) == 0) _instanceSignal.TrySetResult(true);
            }
        }

        private void CompleteInstanceLifecycle(TaskCompletionSource<bool> lifecycle)
        {
            lifecycle.TrySetResult(true);
            lock (_gate)
            {
                _pendingInstances.Remove(lifecycle.Task);
                if (_disposed) _instanceSignal.TrySetResult(true);
            }
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
            ResourceBackendAsset loaded;
            try
            {
                loaded = await entry.Load.ConfigureAwait(false);
                if (loaded == null) throw new InvalidOperationException("Resource backend returned a null asset result.");
            }
            catch (Exception error)
            {
                if (!(error is OperationCanceledException)) _diagnostics.Error("Asset load failed: " + entry.Key, error);
                lock (_gate) { entry.Removed = true; RemoveIfCurrent(entry); }
                if (error is OperationCanceledException) entry.Ready.TrySetCanceled();
                else entry.Ready.TrySetException(error);
                CompleteAssetLifetime(entry);
                return;
            }
            bool shouldRelease;
            lock (_gate)
            {
                entry.Result = loaded;
                if (entry.Holders == 0)
                {
                    entry.Removed = true;
                    RemoveIfCurrent(entry);
                    shouldRelease = true;
                }
                else shouldRelease = false;
            }
            entry.Ready.TrySetResult(loaded);
            if (shouldRelease)
            {
                // Release observations retain failures for CloseAsync; do not leave an
                // unobserved fire-and-forget exception from this result publisher.
                try { await StartReleaseEntryAsync(entry).ConfigureAwait(false); }
                catch { }
            }
        }

        private Task ReleaseHolderAsync(AssetEntry entry)
        {
            bool release = false;
            lock (_gate)
            {
                if (entry.Holders > 0) entry.Holders--;
                if (_disposed && entry.Holders == 0) _leaseSignal.TrySetResult(true);
                if (entry.Holders == 0 && entry.Result != null)
                {
                    entry.Removed = true;
                    RemoveIfCurrent(entry);
                    release = true;
                }
                else if (entry.Holders == 0)
                {
                    entry.Removed = true;
                    RemoveIfCurrent(entry);
                }
            }
            return release ? StartReleaseEntryAsync(entry) : Task.CompletedTask;
        }

        private void RemoveIfCurrent(AssetEntry entry)
        {
            if (_assets.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _assets.Remove(entry.Key);
        }

        private void CompleteAssetLifetime(AssetEntry entry)
        {
            lock (_gate)
            {
                entry.Lifetime.TrySetResult(true);
                _assetLifetimes.Remove(entry);
                _leaseSignal.TrySetResult(true);
            }
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

        private Task StartReleaseEntryAsync(AssetEntry entry)
        {
            ResourceBackendAsset result;
            TaskCompletionSource<bool> completion = null;
            lock (_gate)
            {
                if (entry.Released) return entry.ReleaseTask ?? Task.CompletedTask;
                entry.Released = true;
                completion = NewSignal(); entry.ReleaseTask = completion.Task; result = entry.Result;
                TrackPendingReleaseLocked(entry.ReleaseTask);
            }
            _ = CompleteEntryReleaseAsync(entry, result, completion);
            return completion.Task;
        }

        private async Task CompleteEntryReleaseAsync(AssetEntry entry, ResourceBackendAsset result, TaskCompletionSource<bool> completion)
        {
            try
            {
                if (result != null) await result.ReleaseAsync().ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally { CompleteAssetLifetime(entry); }
        }

        private void TrackPendingRelease(Task release)
        {
            lock (_gate) TrackPendingReleaseLocked(release);
        }

        private void TrackPendingReleaseLocked(Task release)
        {
            if (release == null || _releaseObservations.ContainsKey(release)) return;
            var observation = new ReleaseObservation(release);
            _releaseObservations.Add(release, observation);
            if (release.IsCompleted)
            {
                CompleteReleaseObservationLocked(observation);
                return;
            }
            _pendingReleases.Add(release);
            _ = release.ContinueWith(_ =>
            {
                lock (_gate) { CompleteReleaseObservationLocked(observation); }
            }, TaskScheduler.Default);
        }

        private void CompleteReleaseObservationLocked(ReleaseObservation observation)
        {
            if (!observation.Task.IsCompleted) return;
            if (!observation.FailureRecorded)
            {
                observation.FailureRecorded = true;
                if (observation.Task.IsFaulted && observation.Task.Exception != null)
                {
                    var failures = observation.Task.Exception.Flatten().InnerExceptions;
                    for (int i = 0; i < failures.Count; i++) RememberReleaseFailureLocked(failures[i]);
                }
                else if (observation.Task.IsCanceled)
                {
                    RememberReleaseFailureLocked(new TaskCanceledException("A resource release operation was canceled."));
                }
            }
            _pendingReleases.Remove(observation.Task);
            _releaseObservations.Remove(observation.Task);
            _activitySignal.TrySetResult(true);
        }

        private void RememberReleaseFailureLocked(Exception error)
        {
            if (error == null || _releaseFailures.Count >= MaxRememberedReleaseFailures) return;
            _releaseFailures.Add(error);
        }

        private void CollectCompletedReleasesLocked(List<Exception> errors)
        {
            var observations = new List<ReleaseObservation>(_releaseObservations.Values);
            for (int i = 0; i < observations.Count; i++) CompleteReleaseObservationLocked(observations[i]);
            if (_releaseFailures.Count == 0) return;
            errors.AddRange(_releaseFailures);
            _releaseFailures.Clear();
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
            List<Exception> errors = new List<Exception>();
            while (true)
            {
                Task[] pending;
                Task signal;
                Task instanceSignal;
                Task activitySignal;
                List<AssetEntry> releaseEntries = null;
                lock (_gate)
                {
                    CollectCompletedReleasesLocked(errors);
                    var wait = new List<Task>();
                    bool hasActiveLeases = false;
                    foreach (var asset in _assetLifetimes)
                        if (!asset.Lifetime.Task.IsCompleted) wait.Add(asset.Lifetime.Task);
                    foreach (var instance in _pendingInstances) if (!instance.IsCompleted) wait.Add(instance);
                    foreach (var release in _pendingReleases) if (!release.IsCompleted) wait.Add(release);
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
                    pending = wait.ToArray(); signal = _leaseSignal.Task; instanceSignal = _instanceSignal.Task; activitySignal = _activitySignal.Task;
                }
                if (releaseEntries != null)
                {
                    for (int i = 0; i < releaseEntries.Count; i++)
                    {
                        try { await StartReleaseEntryAsync(releaseEntries[i]).ConfigureAwait(false); }
                        catch { lock (_gate) CollectCompletedReleasesLocked(errors); }
                    }
                    break;
                }
                var tasks = new Task[pending.Length + 3];
                Array.Copy(pending, tasks, pending.Length); tasks[tasks.Length - 2] = signal; tasks[tasks.Length - 1] = instanceSignal;
                tasks[tasks.Length - 3] = activitySignal;
                await Task.WhenAny(tasks);
                lock (_gate)
                {
                    if (ReferenceEquals(signal, _leaseSignal.Task) && signal.IsCompleted)
                        _leaseSignal = NewSignal();
                    if (ReferenceEquals(instanceSignal, _instanceSignal.Task) && instanceSignal.IsCompleted)
                        _instanceSignal = NewSignal();
                    if (ReferenceEquals(activitySignal, _activitySignal.Task) && activitySignal.IsCompleted)
                        _activitySignal = NewSignal();
                }
            }
            lock (_gate) CollectCompletedReleasesLocked(errors);
            try { _backend.Dispose(); }
            catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Resource service close failed.", errors);
        }

        private static TaskCompletionSource<bool> NewSignal()
        { return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); }

        public void Dispose() { _ = CloseAsync(); }
    }
}
