using System;
using System.Collections.Generic;
using WFrameWork.Diagnostics;

namespace WFrameWork.Events
{
    /// <summary>
    /// Main-thread, synchronous, strongly typed events. Each Subscribe creates an independent lease.
    /// Own the lease with RuntimeScope.Own or UiBindingSet.Add to automatically unsubscribe.
    /// </summary>
    public sealed class EventBus : IDisposable
    {
        private interface IChannel { void Clear(); }
        private sealed class Subscription<T> : IDisposable
        {
            internal Action<T> Callback;
            private Channel<T> _channel;
            internal Subscription(Channel<T> channel, Action<T> callback) { _channel = channel; Callback = callback; }
            public void Dispose()
            {
                var channel = _channel;
                _channel = null; Callback = null;
                channel?.Compact();
            }
        }
        private sealed class Channel<T> : IChannel
        {
            private readonly List<Subscription<T>> _listeners = new List<Subscription<T>>();
            private int _depth;
            internal IDisposable Subscribe(Action<T> callback)
            { var lease = new Subscription<T>(this, callback); _listeners.Add(lease); return lease; }
            internal void Publish(T message, DiagnosticLogger log)
            {
                if (_depth >= 64) throw new InvalidOperationException("Event recursion limit exceeded.");
                int count = _listeners.Count;
                _depth++;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        var callback = _listeners[i].Callback;
                        if (callback == null) continue;
                        try { callback(message); }
                        catch (Exception error) { log.Error("Event subscriber failed: " + typeof(T).Name, error); }
                    }
                }
                finally { _depth--; Compact(); }
            }
            internal void Compact()
            {
                if (_depth != 0) return;
                int write = 0;
                for (int i = 0; i < _listeners.Count; i++)
                    if (_listeners[i].Callback != null) _listeners[write++] = _listeners[i];
                if (write < _listeners.Count) _listeners.RemoveRange(write, _listeners.Count - write);
            }
            public void Clear()
            {
                // Prevent list compaction while subscriptions are being disposed.
                _depth++;
                try { for (int i = 0; i < _listeners.Count; i++) _listeners[i].Dispose(); }
                finally { _depth--; Compact(); }
            }
        }
        private readonly Dictionary<Type, IChannel> _channels = new Dictionary<Type, IChannel>();
        private readonly DiagnosticLogger _log;
        private bool _disposed;
        public EventBus(IDiagnosticSink diagnostics = null) { _log = new DiagnosticLogger("Events", diagnostics); }
        public IDisposable Subscribe<T>(Action<T> callback)
        {
            EnsureOpen();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!_channels.TryGetValue(typeof(T), out var channel))
            { channel = new Channel<T>(); _channels.Add(typeof(T), channel); }
            return ((Channel<T>)channel).Subscribe(callback);
        }
        public void Publish<T>(T message)
        {
            EnsureOpen();
            if (_channels.TryGetValue(typeof(T), out var channel)) ((Channel<T>)channel).Publish(message, _log);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var channel in _channels.Values) channel.Clear();
            _channels.Clear();
        }
        private void EnsureOpen() { if (_disposed) throw new ObjectDisposedException(nameof(EventBus)); }
    }
}
