using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using WFrameWork.Core.ResLoad;
using WFrameWork.Diagnostics;
using WFrameWork.Threading;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Core.ResLoad.Unity
{
    /// <summary>Unity 2022.3 / Addressables 1.22.x backend. The service owns the returned handle through the release callback.</summary>
    public sealed class AddressablesResourceBackend : IResourceBackend
    {
        private bool _disposed;
        private readonly IMainThreadDispatcher _mainThread;
        public bool IsDisposed => _disposed;

        public AddressablesResourceBackend(IMainThreadDispatcher mainThread = null)
        { _mainThread = mainThread ?? new UnityMainThreadDispatcher(); }

        public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureUsable();
            AsyncOperationHandle<IResourceLocator> handle = await _mainThread.RunAsync(() => Addressables.InitializeAsync(false), cancellationToken);
            try
            {
                await AwaitHandle(handle, cancellationToken);
            }
            finally { await _mainThread.RunAsync(() => Addressables.Release(handle), CancellationToken.None); }
        }

        public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken cancellationToken)
        {
            EnsureUsable();
            if (requestedType == null) throw new ArgumentNullException(nameof(requestedType));
            return CompleteAssetAsync(key, requestedType, progress, cancellationToken);
        }

        public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays,
            IProgress<float> progress, CancellationToken cancellationToken)
        {
            EnsureUsable();
            return CompleteInstanceAsync(key, parent as Transform, worldPositionStays, progress, cancellationToken);
        }

        private async Task<ResourceBackendAsset> CompleteAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken token)
        {
            AsyncOperationHandle<UnityEngine.Object> handle = await _mainThread.RunAsync(() => Addressables.LoadAssetAsync<UnityEngine.Object>(key), token);
            return await CompleteAsset(handle, key, requestedType, progress, token, _mainThread);
        }

        private static async Task<ResourceBackendAsset> CompleteAsset(AsyncOperationHandle<UnityEngine.Object> handle,
            string key, Type requestedType, IProgress<float> progress, CancellationToken token)
        { return await CompleteAsset(handle, key, requestedType, progress, token, new UnityMainThreadDispatcher()); }

        private static async Task<ResourceBackendAsset> CompleteAsset(AsyncOperationHandle<UnityEngine.Object> handle,
            string key, Type requestedType, IProgress<float> progress, CancellationToken token, IMainThreadDispatcher mainThread)
        {
            try
            {
                await AwaitHandle(handle, token, progress);
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw handle.OperationException ?? new InvalidOperationException("Addressable asset load failed: " + key);
                if (!requestedType.IsInstanceOfType(handle.Result))
                {
                    await mainThread.RunAsync(() => Addressables.Release(handle));
                    throw new InvalidCastException("Addressable asset type mismatch for '" + key + "'. Requested " + requestedType.FullName + ".");
                }
                return new ResourceBackendAsset(handle.Result, () => mainThread.RunAsync(() =>
                {
                    if (handle.IsValid()) Addressables.Release(handle);
                }));
            }
            catch (OperationCanceledException)
            {
                _ = FinishCanceledAsset(handle, mainThread);
                throw;
            }
            catch
            {
                await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.Release(handle); });
                throw;
            }
        }

        private async Task<ResourceBackendInstance> CompleteInstanceAsync(string key, Transform parent, bool worldPositionStays,
            IProgress<float> progress, CancellationToken token)
        {
            AsyncOperationHandle<GameObject> handle = await _mainThread.RunAsync(() => Addressables.InstantiateAsync(key, parent, worldPositionStays), token);
            return await CompleteInstance(handle, key, progress, token, _mainThread);
        }

        private static async Task<ResourceBackendInstance> CompleteInstance(AsyncOperationHandle<GameObject> handle,
            string key, IProgress<float> progress, CancellationToken token)
        { return await CompleteInstance(handle, key, progress, token, new UnityMainThreadDispatcher()); }

        private static async Task<ResourceBackendInstance> CompleteInstance(AsyncOperationHandle<GameObject> handle,
            string key, IProgress<float> progress, CancellationToken token, IMainThreadDispatcher mainThread)
        {
            try
            {
                await AwaitHandle(handle, token, progress);
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw handle.OperationException ?? new InvalidOperationException("Addressable instance creation failed: " + key);
                return new ResourceBackendInstance(handle.Result, () => mainThread.RunAsync(() =>
                {
                    if (handle.IsValid()) Addressables.ReleaseInstance(handle);
                }), true);
            }
            catch (OperationCanceledException)
            {
                _ = FinishCanceledInstance(handle, mainThread);
                throw;
            }
            catch
            {
                await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.ReleaseInstance(handle); });
                throw;
            }
        }

        private static async Task FinishCanceledAsset(AsyncOperationHandle<UnityEngine.Object> handle, IMainThreadDispatcher mainThread)
        {
            try { await AwaitHandle(handle, CancellationToken.None); }
            catch { }
            await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.Release(handle); });
        }

        private static async Task FinishCanceledInstance(AsyncOperationHandle<GameObject> handle, IMainThreadDispatcher mainThread)
        {
            try { await AwaitHandle(handle, CancellationToken.None); }
            catch { }
            await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.ReleaseInstance(handle); });
        }

        private static Task AwaitHandle<T>(AsyncOperationHandle<T> handle, CancellationToken token,
            IProgress<float> progress = null)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (token.CanBeCanceled)
                registration = token.Register(() => completion.TrySetCanceled(token));
            handle.Completed += completed =>
            {
                registration.Dispose();
                progress?.Report(completed.PercentComplete);
                if (completed.Status == AsyncOperationStatus.Succeeded) completion.TrySetResult(true);
                else completion.TrySetException(completed.OperationException ?? new InvalidOperationException("Addressables operation failed."));
            };
            progress?.Report(handle.PercentComplete);
            return completion.Task;
        }

        private void EnsureUsable()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AddressablesResourceBackend));
        }

        public void Dispose() { _disposed = true; }
    }

    public sealed class AddressablesResourceService : IDisposable
    {
        public AddressablesResourceBackend Backend { get; }
        public ResourceService Service { get; }
        public bool IsDisposed => Service.IsDisposed;

        public AddressablesResourceService(IDiagnosticSink diagnostics = null, IMainThreadDispatcher mainThread = null)
        {
            Backend = new AddressablesResourceBackend(mainThread);
            Service = new ResourceService(Backend, diagnostics);
        }

        public Task InitializeAsync(CancellationToken token = default(CancellationToken)) => Backend.InitializeAsync(token);
        public Task CloseAsync() => Service.CloseAsync();
        public void Dispose() => Service.Dispose();
    }
}
