using System;
using System.Collections.Generic;

namespace WFrameWork.Pool
{
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _reset;
        private readonly Action<T> _destroy;
        private readonly Stack<T> _available = new Stack<T>();
        private readonly HashSet<T> _owned = new HashSet<T>();
        private readonly HashSet<T> _leased = new HashSet<T>();
        private readonly int _maxCapacity;
        private bool _closed;

        public ObjectPool(Func<T> create, Action<T> reset = null, Action<T> destroy = null, int maxCapacity = 32)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _reset = reset; _destroy = destroy;
            if (maxCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            _maxCapacity = maxCapacity;
        }

        public bool IsClosed => _closed;
        public int AvailableCount => _available.Count;
        public int LeasedCount => _leased.Count;
        public int TotalCount => _owned.Count;

        public void Warmup(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count && _available.Count < _maxCapacity; i++) _available.Push(CreateOwned());
        }

        public T Rent()
        {
            if (_closed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
            T item = _available.Count > 0 ? _available.Pop() : CreateOwned();
            _leased.Add(item);
            return item;
        }

        public bool TryReturn(T item)
        {
            if (item == null || !_leased.Remove(item)) return false;
            if (_closed || _available.Count >= _maxCapacity)
            {
                _owned.Remove(item); _destroy?.Invoke(item); return true;
            }
            try { _reset?.Invoke(item); }
            catch { _owned.Remove(item); _destroy?.Invoke(item); throw; }
            _available.Push(item);
            return true;
        }

        public void Return(T item)
        {
            if (!TryReturn(item)) throw new InvalidOperationException("The object is not leased from this pool.");
        }

        public void Clear()
        {
            while (_available.Count > 0)
            {
                T item = _available.Pop(); _owned.Remove(item); _destroy?.Invoke(item);
            }
        }

        private T CreateOwned()
        {
            T item = _create();
            if (item == null) throw new InvalidOperationException("Pool factory returned null.");
            _owned.Add(item); return item;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true; Clear();
            foreach (T item in new List<T>(_leased)) { _leased.Remove(item); _owned.Remove(item); _destroy?.Invoke(item); }
        }
    }
}
