using System;
using System.Collections.Generic;

namespace WFrameWork.Diagnostics
{
    public enum DiagnosticLevel { Trace, Info, Warning, Error }

    public readonly struct DiagnosticEvent
    {
        public DiagnosticLevel Level { get; }
        public string Module { get; }
        public string Message { get; }
        public Exception Exception { get; }
        public long Sequence { get; }

        public DiagnosticEvent(DiagnosticLevel level, string module, string message, Exception exception = null, long sequence = 0)
        {
            Level = level; Module = module ?? string.Empty; Message = message ?? string.Empty;
            Exception = exception; Sequence = sequence;
        }
    }

    public interface IDiagnosticSink
    {
        void Report(in DiagnosticEvent diagnostic);
    }

    public sealed class NullDiagnosticSink : IDiagnosticSink
    {
        public static readonly NullDiagnosticSink Instance = new NullDiagnosticSink();
        private NullDiagnosticSink() { }
        public void Report(in DiagnosticEvent diagnostic) { }
    }

    public sealed class CollectingDiagnosticSink : IDiagnosticSink
    {
        private readonly List<DiagnosticEvent> _events = new List<DiagnosticEvent>();
        public IReadOnlyList<DiagnosticEvent> Events => _events;
        public void Report(in DiagnosticEvent diagnostic) { _events.Add(diagnostic); }
        public void Clear() { _events.Clear(); }
    }

    public sealed class DiagnosticLogger
    {
        private readonly IDiagnosticSink _sink;
        private readonly string _module;
        private long _sequence;

        public DiagnosticLogger(string module, IDiagnosticSink sink = null)
        {
            _module = string.IsNullOrWhiteSpace(module) ? "GFramework" : module.Trim();
            _sink = sink ?? NullDiagnosticSink.Instance;
        }

        public void Trace(string message) => Report(DiagnosticLevel.Trace, message, null);
        public void Info(string message) => Report(DiagnosticLevel.Info, message, null);
        public void Warning(string message, Exception exception = null) => Report(DiagnosticLevel.Warning, message, exception);
        public void Error(string message, Exception exception = null) => Report(DiagnosticLevel.Error, message, exception);

        private void Report(DiagnosticLevel level, string message, Exception exception)
        {
            try { _sink.Report(new DiagnosticEvent(level, _module, message, exception, ++_sequence)); }
            catch { /* Diagnostics must never break the operation being observed. */ }
        }
    }

    public sealed class DiagnosticSnapshot
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        internal DiagnosticSnapshot(DateTimeOffset capturedAt) { CapturedAt = capturedAt; }
        public DateTimeOffset CapturedAt { get; }
        public IReadOnlyDictionary<string, string> Values => _values;
        public void Set(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Diagnostic key is required.", nameof(key));
            _values[key.Trim()] = value == null ? string.Empty : value.ToString();
        }
    }

    public sealed class DiagnosticRegistry
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, Action<DiagnosticSnapshot>> _providers = new Dictionary<string, Action<DiagnosticSnapshot>>(StringComparer.Ordinal);
        private readonly Queue<DiagnosticSnapshot> _history = new Queue<DiagnosticSnapshot>();
        private readonly int _historyCapacity;

        public DiagnosticRegistry(int historyCapacity = 32)
        { if (historyCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(historyCapacity)); _historyCapacity = historyCapacity; }

        public IDisposable Register(string name, Action<DiagnosticSnapshot> provider)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Diagnostic provider name is required.", nameof(name));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            lock (_gate) _providers[name.Trim()] = provider;
            return new Registration(this, name.Trim());
        }

        public DiagnosticSnapshot Capture()
        {
            var snapshot = new DiagnosticSnapshot(DateTimeOffset.UtcNow);
            KeyValuePair<string, Action<DiagnosticSnapshot>>[] providers;
            lock (_gate) { providers = new List<KeyValuePair<string, Action<DiagnosticSnapshot>>>(_providers).ToArray(); }
            for (int i = 0; i < providers.Length; i++)
            {
                try { providers[i].Value(snapshot); }
                catch (Exception error) { snapshot.Set(providers[i].Key + ".error", error.GetType().Name); }
            }
            lock (_gate)
            {
                _history.Enqueue(snapshot);
                while (_history.Count > _historyCapacity) _history.Dequeue();
            }
            return snapshot;
        }

        public int HistoryCount { get { lock (_gate) return _history.Count; } }
        private void Remove(string name) { lock (_gate) _providers.Remove(name); }

        private sealed class Registration : IDisposable
        {
            private DiagnosticRegistry _owner; private readonly string _name;
            internal Registration(DiagnosticRegistry owner, string name) { _owner = owner; _name = name; }
            public void Dispose() { var owner = _owner; _owner = null; owner?.Remove(_name); }
        }
    }
}
