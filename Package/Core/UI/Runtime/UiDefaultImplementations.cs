using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WFrameWork.UI
{
    public sealed class InMemoryUiResourceProvider : IUiResourceProvider
    {
        private readonly Dictionary<string, object> _assets = new Dictionary<string, object>(StringComparer.Ordinal);
        public void Add(string key, object asset)
        {
            if (string.IsNullOrWhiteSpace(key) || asset == null) throw new ArgumentException("A key and asset are required.");
            _assets[key] = asset;
        }
        public Task<UiResourceHandle> LoadAsync(string resourceKey, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<UiResourceHandle>(cancellationToken);
            if (!_assets.TryGetValue(resourceKey, out var asset)) return Task.FromException<UiResourceHandle>(new KeyNotFoundException(resourceKey));
            return Task.FromResult(new UiResourceHandle(asset));
        }
    }

    public sealed class DelegateUiPanelFactory : IUiPanelFactory
    {
        private readonly Func<UiPanelDefinition, UiResourceHandle, IUiPanelInstance> _create;
        public DelegateUiPanelFactory(Func<UiPanelDefinition, UiResourceHandle, IUiPanelInstance> create)
        { _create = create ?? throw new ArgumentNullException(nameof(create)); }
        public IUiPanelInstance Create(UiPanelDefinition definition, UiResourceHandle resource) => _create(definition, resource);
    }

    public sealed class BasicUiPanelInstance : IUiPanelInstance
    {
        private readonly Action<UiPanelUpdateContext> _update;
        private readonly bool _continuous;
        public bool IsAlive { get; private set; } = true;
        public bool RequiresContinuousUpdate => _continuous;
        public bool IsVisible { get; private set; }
        public BasicUiPanelInstance(Action<UiPanelUpdateContext> update = null, bool continuous = false)
        { _update = update; _continuous = continuous; }
        public void SetVisible(bool visible) { if (IsAlive) IsVisible = visible; }
        public void OnOpened(object argument) { }
        public void OnShown() { }
        public void OnHidden() { }
        public void OnClosed() { }
        public void OnUpdate(in UiPanelUpdateContext context) { if (IsAlive) _update?.Invoke(context); }
        public void Dispose() { IsAlive = false; IsVisible = false; }
    }
}

