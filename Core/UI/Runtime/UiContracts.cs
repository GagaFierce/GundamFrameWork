using System;
using System.Threading;
using System.Threading.Tasks;

namespace WFrameWork.UI
{
    public readonly struct UiPanelId : IEquatable<UiPanelId>
    {
        public string Value { get; }
        public UiPanelId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Panel id is required.", nameof(value));
            Value = value.Trim();
        }
        public bool Equals(UiPanelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is UiPanelId && Equals((UiPanelId)obj);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(UiPanelId left, UiPanelId right) => left.Equals(right);
        public static bool operator !=(UiPanelId left, UiPanelId right) => !left.Equals(right);
        public override string ToString() => Value ?? "<invalid panel>";
    }

    public enum UiPanelLayer
    {
        Hud,
        Normal,
        Modal,
        Overlay
    }

    public enum UiPanelInstanceMode
    {
        Single,
        Multiple
    }

    public enum UiPanelState
    {
        Loading,
        Visible,
        Hidden,
        Closing,
        Closed,
        Failed
    }

    public sealed class UiPanelDefinition : IEquatable<UiPanelDefinition>
    {
        public UiPanelId Id { get; }
        public string ResourceKey { get; }
        public UiPanelLayer Layer { get; }
        public bool IsModal { get; }
        public UiPanelInstanceMode InstanceMode { get; }

        public UiPanelDefinition(UiPanelId id, string resourceKey, UiPanelLayer layer = UiPanelLayer.Normal,
            bool isModal = false, UiPanelInstanceMode instanceMode = UiPanelInstanceMode.Single)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) throw new ArgumentException("Resource key is required.", nameof(resourceKey));
            if (!Enum.IsDefined(typeof(UiPanelLayer), layer)) throw new ArgumentOutOfRangeException(nameof(layer));
            if (!Enum.IsDefined(typeof(UiPanelInstanceMode), instanceMode)) throw new ArgumentOutOfRangeException(nameof(instanceMode));
            Id = id; ResourceKey = resourceKey.Trim(); Layer = layer; IsModal = isModal;
            InstanceMode = instanceMode;
        }

        public bool Equals(UiPanelDefinition other)
        {
            if (ReferenceEquals(other, null)) return false;
            return Id == other.Id && ResourceKey == other.ResourceKey && Layer == other.Layer &&
                IsModal == other.IsModal && InstanceMode == other.InstanceMode;
        }
        public override bool Equals(object obj) => Equals(obj as UiPanelDefinition);
        public override int GetHashCode() => Id.GetHashCode();
    }

    public readonly struct UiPanelUpdateContext
    {
        public double DeltaTime { get; }
        public double UnscaledDeltaTime { get; }
        public bool IsDirty { get; }
        internal UiPanelUpdateContext(double deltaTime, double unscaledDeltaTime, bool isDirty)
        { DeltaTime = deltaTime; UnscaledDeltaTime = unscaledDeltaTime; IsDirty = isDirty; }
    }

    public readonly struct UiPanelInfo
    {
        public UiPanelId Id { get; }
        public UiPanelState State { get; }
        public UiPanelLayer Layer { get; }
        public bool IsModal { get; }
        public int InstanceIndex { get; }
        internal UiPanelInfo(UiPanelId id, UiPanelState state, UiPanelLayer layer, bool isModal, int instanceIndex)
        { Id = id; State = state; Layer = layer; IsModal = isModal; InstanceIndex = instanceIndex; }
    }

    public interface IUiPanelInstance : IDisposable
    {
        bool IsAlive { get; }
        bool RequiresContinuousUpdate { get; }
        void SetVisible(bool visible);
        void OnOpened(object argument);
        void OnShown();
        void OnHidden();
        void OnClosed();
        void OnUpdate(in UiPanelUpdateContext context);
    }

    /// <summary>Optional asynchronous destruction barrier for Unity-backed panel instances.</summary>
    public interface IUiAsyncPanelInstance
    {
        Task DisposeAsync();
    }

    public interface IUiPanelFactory
    {
        IUiPanelInstance Create(UiPanelDefinition definition, UiResourceHandle resource);
    }

    public interface IUiResourceProvider
    {
        Task<UiResourceHandle> LoadAsync(string resourceKey, CancellationToken cancellationToken);
    }

    public sealed class UiResourceHandle : IDisposable
    {
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public object Asset { get; }
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }

        public UiResourceHandle(object asset, Action release = null) : this(asset, () => { release?.Invoke(); return Task.CompletedTask; }, true)
        {
        }

        public UiResourceHandle(object asset, Func<Task> release, bool asynchronousRelease)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Asset = asset; _release = release;
        }

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

    public interface IUiFocusService
    {
        object CaptureFocusedElement();
        void RestoreFocusedElement(object focusedElement);
    }

    public interface IUiModalInputBlocker
    {
        IDisposable PushModal(UiPanelId panelId);
    }

    public sealed class UiPanelHandle : IDisposable
    {
        private readonly UiPanelManager _manager;
        private readonly object _entry;
        private bool _disposed;
        private Task _closeTask;
        internal UiPanelHandle(UiPanelManager manager, object entry) { _manager = manager; _entry = entry; }
        public bool IsOpen => !_disposed && _manager.IsEntryOpen(_entry);
        public UiPanelId PanelId => _manager.GetEntryId(_entry);
        public UiPanelState State => _manager.GetEntryState(_entry);
        public Task CloseAsync()
        {
            if (_closeTask != null) return _closeTask;
            if (_disposed) return Task.CompletedTask;
            _disposed = true;
            _closeTask = _manager.CloseEntryAsync(_entry);
            return _closeTask;
        }
        public void Dispose() { CloseAsync(); }
    }

    public sealed class NullUiFocusService : IUiFocusService
    {
        public object CaptureFocusedElement() => null;
        public void RestoreFocusedElement(object focusedElement) { }
    }

    public sealed class NullUiModalInputBlocker : IUiModalInputBlocker
    {
        private sealed class Token : IDisposable { private Action _release; internal Token(Action release) { _release = release; } public void Dispose() { var release = _release; _release = null; release?.Invoke(); } }
        public int Depth { get; private set; }
        public IDisposable PushModal(UiPanelId panelId) { Depth++; return new Token(() => { if (Depth > 0) Depth--; }); }
    }
}
