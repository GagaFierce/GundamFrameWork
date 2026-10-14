using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Core.ResLoad;
using WFrameWork.Threading;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Core.ResLoad.Unity
{
    /// <summary>Local Unity Resources adapter for the shared ResourceService contract.</summary>
    public sealed class UnityResourcesResourceBackend : IResourceBackend
    {
        private readonly IMainThreadDispatcher _mainThread;
        private bool _disposed;

        public bool IsDisposed => _disposed;

        public UnityResourcesResourceBackend(IMainThreadDispatcher mainThread = null)
        {
            _mainThread = mainThread ?? new UnityMainThreadDispatcher();
        }

        public async Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            EnsureUsable();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Resources key is required.", nameof(key));
            if (requestedType == null) throw new ArgumentNullException(nameof(requestedType));

            ResourceRequest request = await _mainThread.RunAsync(
                () => Resources.LoadAsync(key.Trim(), requestedType), cancellationToken);
            await AwaitRequest(request, progress);
            cancellationToken.ThrowIfCancellationRequested();

            UnityEngine.Object asset = await _mainThread.RunAsync(() => request.asset, cancellationToken);
            if (asset == null) throw new FileNotFoundException("Unity Resources asset was not found: " + key, key);
            if (!requestedType.IsInstanceOfType(asset))
                throw new InvalidCastException("Resources asset type mismatch for '" + key + "'. Requested " + requestedType.FullName + ".");
            return new ResourceBackendAsset(asset, () => Task.CompletedTask);
        }

        public async Task<ResourceBackendInstance> InstantiateAsync(string key, object parent,
            bool worldPositionStays, IProgress<float> progress, CancellationToken cancellationToken)
        {
            EnsureUsable();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Resources key is required.", nameof(key));

            ResourceRequest request = await _mainThread.RunAsync(
                () => Resources.LoadAsync<GameObject>(key.Trim()), cancellationToken);
            await AwaitRequest(request, progress);
            cancellationToken.ThrowIfCancellationRequested();

            GameObject prefab = await _mainThread.RunAsync(() => request.asset as GameObject, cancellationToken);
            if (prefab == null) throw new FileNotFoundException("Unity Resources prefab was not found: " + key, key);
            GameObject instance = await _mainThread.RunAsync(
                () => UnityEngine.Object.Instantiate(prefab, ResolveParent(parent), worldPositionStays), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                await _mainThread.RunAsync(() => { if (instance != null) UnityEngine.Object.Destroy(instance); });
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new ResourceBackendInstance(instance,
                () => _mainThread.RunAsync(() => { if (instance != null) UnityEngine.Object.Destroy(instance); }));
        }

        private static Task AwaitRequest(AsyncOperation operation, IProgress<float> progress)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.completed += _ =>
            {
                try { progress?.Report(1); }
                finally { completion.TrySetResult(true); }
            };
            return completion.Task;
        }

        private static Transform ResolveParent(object parent)
        {
            if (parent == null) return null;
            if (parent is Transform transform) return transform;
            if (parent is GameObject gameObject) return gameObject.transform;
            if (parent is Component component) return component.transform;
            throw new ArgumentException("Resources instance parent must be a Transform, GameObject, or Component.", nameof(parent));
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UnityResourcesResourceBackend));
        }

        public void Dispose() { _disposed = true; }
    }
}
