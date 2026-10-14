using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WFrameWork.Pool
{
    public sealed class ObjectPool<T> : IDisposable where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _reset;
        private readonly Action<T> _destroy;
        private readonly Stack<T> _available = new Stack<T>();
        private readonly HashSet<T> _owned = new HashSet<T>(ReferenceComparer<T>.Instance);
        private readonly HashSet<T> _leased = new HashSet<T>(ReferenceComparer<T>.Instance);
        private readonly HashSet<T> _returning = new HashSet<T>(ReferenceComparer<T>.Instance);
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
            if (_closed) throw new ObjectDisposedException(nameof(ObjectPool<T>));
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
            _returning.Add(item);
            if (_closed || _available.Count >= _maxCapacity)
            {
                DestroyReturning(item); return true;
            }
            try { _reset?.Invoke(item); }
            catch (Exception error)
            {
                try { DestroyReturning(item); }
                catch (Exception destroyError) { throw new AggregateException("Pool reset and destroy both failed.", error, destroyError); }
                throw;
            }
            if (_closed || _available.Count >= _maxCapacity) DestroyReturning(item);
            else { _returning.Remove(item); _available.Push(item); }
            return true;
        }

        public void Return(T item)
        {
            if (!TryReturn(item)) throw new InvalidOperationException("The object is not leased from this pool.");
        }

        public void Clear()
        {
            List<Exception> errors = new List<Exception>();
            DestroyAvailable(errors);
            if (errors.Count > 0) throw new AggregateException("Object pool clear failed.", errors);
        }

        private T CreateOwned()
        {
            T item = _create();
            if (item == null) throw new InvalidOperationException("Pool factory returned null.");
            if (_owned.Contains(item)) throw new InvalidOperationException("Pool factory returned an instance already owned by this pool.");
            if (_closed)
            {
                try { _destroy?.Invoke(item); }
                catch (Exception error) { throw new AggregateException("Pool closed during creation and cleanup failed.", error); }
                throw new ObjectDisposedException(nameof(ObjectPool<T>));
            }
            _owned.Add(item); return item;
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            List<Exception> errors = new List<Exception>();
            DestroyAvailable(errors);
            foreach (T item in new List<T>(_leased))
            {
                _leased.Remove(item); _owned.Remove(item);
                try { _destroy?.Invoke(item); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) throw new AggregateException("Object pool disposal failed.", errors);
        }

        private void DestroyAvailable(List<Exception> errors)
        {
            while (_available.Count > 0)
            {
                T item = _available.Pop(); _owned.Remove(item);
                try { _destroy?.Invoke(item); } catch (Exception error) { errors.Add(error); }
            }
        }

        private void DestroyReturning(T item)
        {
            _returning.Remove(item);
            _owned.Remove(item);
            _leased.Remove(item);
            _destroy?.Invoke(item);
        }

        private sealed class ReferenceComparer<TItem> : IEqualityComparer<TItem> where TItem : class
        {
            internal static readonly ReferenceComparer<TItem> Instance = new ReferenceComparer<TItem>();
            public bool Equals(TItem x, TItem y) => ReferenceEquals(x, y);
            public int GetHashCode(TItem obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
