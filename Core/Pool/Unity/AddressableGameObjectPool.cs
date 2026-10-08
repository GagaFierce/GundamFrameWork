using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Core.ResLoad;
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
        private ResourceLease<GameObject> _prefab;
        private bool _closed;
        private Task _warmupTask;
        private Task _closeTask;

        public AddressableGameObjectPool(ResourceService resources, string key, Transform parent = null, int maxCapacity = 32, Action<GameObject> reset = null)
        {
            _resources = resources ?? throw new ArgumentNullException(nameof(resources)); _key = key ?? throw new ArgumentNullException(nameof(key));
            if (maxCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            _parent = parent; _maxCapacity = maxCapacity; _reset = reset;
        }
        public int AvailableCount => _available.Count;
        public int LeasedCount => _leased.Count;
        public bool IsClosed => _closed;

        public Task WarmupAsync(int count) => WarmupAsync(count, default(System.Threading.CancellationToken));

        public Task WarmupAsync(int count, System.Threading.CancellationToken cancellationToken)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (_closed) return Task.FromException(new ObjectDisposedException(nameof(AddressableGameObjectPool)));
            if (_warmupTask != null) return _warmupTask;
            _warmupTask = WarmupCoreAsync(count, cancellationToken);
            return _warmupTask;
        }

        private async Task WarmupCoreAsync(int count, System.Threading.CancellationToken cancellationToken)
        {
            ResourceLease<GameObject> loaded = null;
            try
            {
                if (_prefab == null)
                {
                    loaded = await _resources.LoadAssetAsync<GameObject>(_key, cancellationToken: cancellationToken);
                    if (_closed) { loaded.Dispose(); throw new ObjectDisposedException(nameof(AddressableGameObjectPool)); }
                    _prefab = loaded; loaded = null;
                }
                for (int i = 0; i < count && _available.Count < _maxCapacity; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_closed) throw new ObjectDisposedException(nameof(AddressableGameObjectPool));
                    _available.Push(CreateInstance());
                }
            }
            finally { loaded?.Dispose(); }
        }

        public GameObject Rent()
        {
            if (_closed) throw new ObjectDisposedException(nameof(AddressableGameObjectPool));
            if (_prefab == null) throw new InvalidOperationException("WarmupAsync must complete before Rent.");
            GameObject item = _available.Count > 0 ? _available.Pop() : CreateInstance();
            if (item == null) throw new InvalidOperationException("The pooled instance was destroyed.");
            _leased.Add(item); item.SetActive(true); return item;
        }

        public bool TryReturn(GameObject item)
        {
            if (item == null || !_leased.Remove(item)) return false;
            if (_closed || _available.Count >= _maxCapacity || item == null) { Destroy(item); return true; }
            _reset?.Invoke(item); item.transform.SetParent(_parent, false); item.SetActive(false); _available.Push(item); return true;
        }
        public void Return(GameObject item) { if (!TryReturn(item)) throw new InvalidOperationException("The object is not leased from this pool."); }

        private GameObject CreateInstance() => UnityEngine.Object.Instantiate(_prefab.Asset, _parent, false);
        private static void Destroy(GameObject item) { if (item != null) UnityEngine.Object.Destroy(item); }
        private void ClearAvailable() { while (_available.Count > 0) Destroy(_available.Pop()); }

        public Task CloseAsync()
        {
            if (_closeTask != null) return _closeTask;
            _closed = true;
            _closeTask = CloseCoreAsync();
            return _closeTask;
        }

        private async Task CloseCoreAsync()
        {
            if (_warmupTask != null)
            {
                try { await _warmupTask; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
            }
            var destruction = new List<Task>(_available.Count + _leased.Count);
            while (_available.Count > 0) destruction.Add(DestroyAsync(_available.Pop()));
            foreach (GameObject item in new List<GameObject>(_leased)) { _leased.Remove(item); destruction.Add(DestroyAsync(item)); }
            for (int i = 0; i < destruction.Count; i++) await destruction[i];
            _prefab?.Dispose(); _prefab = null;
        }

        private static Task DestroyAsync(GameObject item)
        {
            if (item == null) return Task.CompletedTask;
            return UnityObjectLifetime.DestroyAndWait(item);
        }

        public void Dispose() { _ = CloseAsync(); }
    }
}
