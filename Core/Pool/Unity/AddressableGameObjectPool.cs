using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Core.ResLoad;
using WFrameWork.Threading;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Pool.Unity
{
    public sealed class AddressableGameObjectPool : IDisposable
    {
        private readonly ResourceService _resources;
        private readonly string _key;
        private readonly Transform _parent;
        private readonly int _maxCapacity;
        private readonly Stack<GameObject> _available = new Stack<GameObject>();
        private readonly HashSet<GameObject> _leased = new HashSet<GameObject>();
        private readonly Action<GameObject> _reset;
        private readonly IMainThreadDispatcher _mainThread;
        private readonly object _gate = new object();
        private ResourceLease<GameObject> _prefab;
        private bool _closed;
        private Task _warmupTask;
        private Task _closeTask;

        public AddressableGameObjectPool(ResourceService resources, string key, Transform parent = null, int maxCapacity = 32,
            Action<GameObject> reset = null, IMainThreadDispatcher mainThread = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources)); _key = key ?? throw new ArgumentNullException(nameof(key));
            if (maxCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            _parent = parent; _maxCapacity = maxCapacity; _reset = reset;
            _mainThread = mainThread ?? new UnityMainThreadDispatcher();
        }
        public int AvailableCount => _available.Count;
        public int LeasedCount => _leased.Count;
        public bool IsClosed => _closed;

        public Task WarmupAsync(int count) => WarmupAsync(count, default(System.Threading.CancellationToken));

        public Task WarmupAsync(int count, System.Threading.CancellationToken cancellationToken)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_gate)
            {
                if (_closed) return Task.FromException(new ObjectDisposedException(nameof(AddressableGameObjectPool)));
                if (_warmupTask != null) return _warmupTask;
                _warmupTask = WarmupCoreAsync(count, cancellationToken);
                Task warmup = _warmupTask;
                _ = warmup.ContinueWith(completed =>
                {
                    if (!completed.IsCanceled && !completed.IsFaulted) return;
                    lock (_gate) if (ReferenceEquals(_warmupTask, warmup) && !_closed) _warmupTask = null;
                }, TaskScheduler.Default);
                return warmup;
            }
        }

        private async Task WarmupCoreAsync(int count, System.Threading.CancellationToken cancellationToken)
        {
            ResourceLease<GameObject> loaded = null;
            try
            {
                if (_prefab == null)
                {
                    loaded = await _resources.LoadAssetAsync<GameObject>(_key, cancellationToken: cancellationToken);
                    if (_closed) { await loaded.DisposeAsync().ConfigureAwait(false); throw new ObjectDisposedException(nameof(AddressableGameObjectPool)); }
                    _prefab = loaded; loaded = null;
                }
                for (int i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool canCreate;
                    lock (_gate) canCreate = !_closed;
                    if (!canCreate) throw new ObjectDisposedException(nameof(AddressableGameObjectPool));
                    bool hasCapacity = await _mainThread.RunAsync(() => _available.Count < _maxCapacity, cancellationToken).ConfigureAwait(false);
                    if (!hasCapacity) break;
                    GameObject instance = await _mainThread.RunAsync(CreateInstance, cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await _mainThread.RunAsync(() =>
                        {
                            if (_closed) throw new ObjectDisposedException(nameof(AddressableGameObjectPool));
                            _available.Push(instance);
                        }, cancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        await DestroyAsync(instance).ConfigureAwait(false);
                        throw;
                    }
                }
            }
            finally { if (loaded != null) await loaded.DisposeAsync().ConfigureAwait(false); }
        }

        public GameObject Rent()
        {
            EnsureMainThread();
            if (_closed) throw new ObjectDisposedException(nameof(AddressableGameObjectPool));
            if (_prefab == null) throw new InvalidOperationException("WarmupAsync must complete before Rent.");
            GameObject item = _available.Count > 0 ? _available.Pop() : CreateInstance();
            if (item == null) throw new InvalidOperationException("The pooled instance was destroyed.");
            _leased.Add(item); item.SetActive(true); return item;
        }

        public bool TryReturn(GameObject item)
        {
            EnsureMainThread();
            if (item == null || !_leased.Remove(item)) return false;
            if (_closed || _available.Count >= _maxCapacity || item == null) { Destroy(item); return true; }
            _reset?.Invoke(item); item.transform.SetParent(_parent, false); item.SetActive(false); _available.Push(item); return true;
        }
        public void Return(GameObject item) { if (!TryReturn(item)) throw new InvalidOperationException("The object is not leased from this pool."); }

        private GameObject CreateInstance() => UnityEngine.Object.Instantiate(_prefab.Asset, _parent, false);
        private static void Destroy(GameObject item) { if (item != null) UnityEngine.Object.Destroy(item); }

        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _closed = true;
                _closeTask = CloseCoreAsync();
                return _closeTask;
            }
        }

        private async Task CloseCoreAsync()
        {
            Exception warmupError = null;
            if (_warmupTask != null)
            {
                try { await _warmupTask.ConfigureAwait(false); }
                catch (Exception error) { warmupError = error; }
            }
            List<GameObject> objects = await _mainThread.RunAsync(() =>
            {
                var result = new List<GameObject>(_available.Count + _leased.Count);
                while (_available.Count > 0) result.Add(_available.Pop());
                foreach (GameObject item in new List<GameObject>(_leased)) { _leased.Remove(item); result.Add(item); }
                return result;
            }).ConfigureAwait(false);
            for (int i = 0; i < objects.Count; i++) await DestroyAsync(objects[i]).ConfigureAwait(false);
            ResourceLease<GameObject> prefab = _prefab; _prefab = null;
            if (prefab != null) await prefab.DisposeAsync().ConfigureAwait(false);
            if (warmupError != null && !(warmupError is OperationCanceledException) && !(warmupError is ObjectDisposedException)) throw warmupError;
        }

        private Task DestroyAsync(GameObject item)
        {
            if (item == null) return Task.CompletedTask;
            return _mainThread.RunAsync(() => UnityObjectLifetime.DestroyAndWait(item)).Unwrap();
        }

        private void EnsureMainThread()
        { if (!_mainThread.IsMainThread) throw new InvalidOperationException("AddressableGameObjectPool operations must run on the Unity main thread."); }

        public void Dispose() { _ = CloseAsync(); }
    }
}
