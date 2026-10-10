using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using WFrameWork.Scene;
using WFrameWork.Threading;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Scene.Unity
{
    public sealed class AddressablesSceneBackend : ISceneBackend
    {
        private bool _disposed;
        private readonly IMainThreadDispatcher _mainThread;
        public AddressablesSceneBackend(IMainThreadDispatcher mainThread = null)
        { _mainThread = mainThread ?? new UnityMainThreadDispatcher(); }
        public Task<SceneLease> LoadAsync(string key, SceneLoadMode mode, IProgress<float> progress, CancellationToken token)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AddressablesSceneBackend));
            return LoadCoreAsync(key, mode, progress, token);
        }

        private async Task<SceneLease> LoadCoreAsync(string key, SceneLoadMode mode, IProgress<float> progress, CancellationToken token)
        {
            AsyncOperationHandle<SceneInstance> handle = await _mainThread.RunAsync(() =>
                Addressables.LoadSceneAsync(key, mode == SceneLoadMode.Single ? LoadSceneMode.Single : LoadSceneMode.Additive, true), token);
            return await Complete(handle, key, progress, token, _mainThread);
        }

        private static async Task<SceneLease> Complete(AsyncOperationHandle<SceneInstance> handle, string key, IProgress<float> progress, CancellationToken token, IMainThreadDispatcher mainThread)
        {
            try
            {
                await AwaitHandle(handle, progress, token);
                if (handle.Status != AsyncOperationStatus.Succeeded) throw handle.OperationException ?? new InvalidOperationException("Addressable scene load failed: " + key);
                SceneInstance scene = handle.Result;
                return new SceneLease(key, scene, () => UnloadAsync(handle, mainThread));
            }
            catch (OperationCanceledException)
            {
                // Direct backend callers also receive a task covering native cleanup.
                await FinishCanceledLoad(handle, mainThread);
                throw;
            }
            catch
            {
                await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.Release(handle); });
                throw;
            }
        }

        private static async Task UnloadAsync(AsyncOperationHandle<SceneInstance> handle, IMainThreadDispatcher mainThread)
        {
            AsyncOperationHandle<SceneInstance> unload = await mainThread.RunAsync(() => Addressables.UnloadSceneAsync(handle, true));
            await AwaitHandle(unload, null, CancellationToken.None);
        }

        private static async Task FinishCanceledLoad(AsyncOperationHandle<SceneInstance> handle, IMainThreadDispatcher mainThread)
        {
            try { await AwaitHandle(handle, null, CancellationToken.None); }
            catch
            {
                await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.Release(handle); });
                return;
            }
            try { await UnloadAsync(handle, mainThread); }
            catch { await mainThread.RunAsync(() => { if (handle.IsValid()) Addressables.Release(handle); }); }
        }

        private static Task AwaitHandle(AsyncOperationHandle<SceneInstance> handle, IProgress<float> progress, CancellationToken token)
        {
            var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (token.CanBeCanceled) registration = token.Register(() => source.TrySetCanceled(token));
            handle.Completed += completed =>
            {
                registration.Dispose(); progress?.Report(completed.PercentComplete);
                if (completed.Status == AsyncOperationStatus.Succeeded) source.TrySetResult(true);
                else source.TrySetException(completed.OperationException ?? new InvalidOperationException("Addressable scene operation failed."));
            };
            return source.Task;
        }
        public void Dispose() { _disposed = true; }
    }
}
