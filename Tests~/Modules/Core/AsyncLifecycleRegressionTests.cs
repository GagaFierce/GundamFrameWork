using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Application;
using WFrameWork.Audio;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.ResLoad;
using WFrameWork.Scene;
using WFrameWork.Threading;

namespace WFrameWork.Modules.Tests
{
    internal static class AsyncLifecycleRegressionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
        internal const string AssetProbeArgument = "--asset-lifetime-schedule-probe";
        private static TaskCompletionSource<T> Signal<T>() =>
            new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static async Task CanceledAssetIncludesDeferredObservation()
        {
            // Isolate the controlled ThreadPool schedule from the rest of the test runner.
            var start = new ProcessStartInfo(Environment.ProcessPath)
            {
                UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
            start.ArgumentList.Add(AssetProbeArgument);
            using (var process = Process.Start(start))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.Equal(0, process.ExitCode, "isolated asset lifetime probe: " + await output + await error);
                }
                finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
            }
        }

        internal static int RunAssetLifetimeProbe()
        {
            ThreadPool.GetMinThreads(out int minimumWorkers, out int minimumIo);
            ThreadPool.GetMaxThreads(out int maximumWorkers, out int maximumIo);
            using (var resume = new ManualResetEventSlim())
            using (var occupied = new ManualResetEventSlim())
            {
                Task worker = null;
                try
                {
                    Assert.True(ThreadPool.SetMinThreads(1, minimumIo) && ThreadPool.SetMaxThreads(1, maximumIo), "controlled continuation schedule available");
                    var backend = new AssetBackend();
                    var service = new ResourceService(backend);
                    using (var cancellation = new CancellationTokenSource())
                    {
                        Task request = service.LoadAssetAsync<object>("asset", cancellationToken: cancellation.Token);
                        cancellation.Cancel();
                        ExpectFailure<OperationCanceledException>(request).GetAwaiter().GetResult();
                        worker = Task.Run(() => { occupied.Set(); if (!resume.Wait(Timeout)) throw new TimeoutException("continuation worker barrier"); });
                        Assert.True(occupied.Wait(Timeout), "continuation worker is occupied");
                        backend.Load.TrySetResult(backend.NewAsset());
                        Task close = service.CloseAsync();
                        Assert.False(close.IsCompleted, "raw load completion is not the end of asset lifetime");
                        Assert.False(backend.Disposed, "backend must survive deferred result observation");
                        resume.Set();
                        ThreadPool.SetMaxThreads(maximumWorkers, maximumIo);
                        ThreadPool.SetMinThreads(minimumWorkers, minimumIo);
                        worker.WaitAsync(Timeout).GetAwaiter().GetResult();
                        close.WaitAsync(Timeout).GetAwaiter().GetResult();
                        Assert.Equal(1, backend.Releases, "late asset released exactly once");
                        Assert.True(backend.Disposed && !backend.ReleaseAfterDispose, "asset released before backend disposal");
                        Assert.Equal(0, service.PendingOperationCount, "no asset lifecycle remains");
                    }
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
                finally
                {
                    resume.Set();
                    ThreadPool.SetMaxThreads(maximumWorkers, maximumIo);
                    ThreadPool.SetMinThreads(minimumWorkers, minimumIo);
                    if (worker != null) { try { worker.WaitAsync(Timeout).GetAwaiter().GetResult(); } catch { } }
                }
            }
        }

        internal static async Task SharedAssetPublicationAndFailures()
        {
            var backend = new AssetBackend();
            var service = new ResourceService(backend);
            using (var cancellation = new CancellationTokenSource())
            {
                Task<ResourceLease<object>> canceled = service.LoadAssetAsync<object>("shared", cancellationToken: cancellation.Token);
                Task<ResourceLease<object>> active = service.LoadAssetAsync<object>("shared");
                cancellation.Cancel();
                await ExpectFailure<OperationCanceledException>(canceled);
                backend.Load.TrySetResult(backend.NewAsset());
                var lease = await active.WaitAsync(Timeout);
                Assert.Equal(1, backend.LoadCalls, "shared callers use one load");
                Assert.Equal(0, service.PendingOperationCount, "an idle held lease is not a pending operation");
                Task close = service.CloseAsync();
                Assert.False(close.IsCompleted, "close waits for the valid consumer");
                await lease.DisposeAsync().WaitAsync(Timeout);
                await close.WaitAsync(Timeout);
                Assert.Equal(1, backend.Releases, "published asset remains releasable by its last holder");
            }

            var failedBackend = new AssetBackend();
            var failed = new ResourceService(failedBackend);
            Task failure = failed.LoadAssetAsync<object>("failed");
            failedBackend.Load.TrySetException(new IOException("asset load failed"));
            await ExpectFailure<IOException>(failure);
            await failed.CloseAsync().WaitAsync(Timeout);
            Assert.Equal(0, failed.PendingOperationCount, "failed load completes its lifetime");
            Assert.True(failedBackend.Disposed, "failed load does not prevent backend disposal");
        }

        internal static async Task CanceledAssetReleaseFailureIsAwaited()
        {
            var backend = new AssetBackend { ReleaseGate = Signal<bool>() };
            var service = new ResourceService(backend);
            using (var cancellation = new CancellationTokenSource())
            {
                Task load = service.LoadAssetAsync<object>("late", cancellationToken: cancellation.Token);
                cancellation.Cancel();
                await ExpectFailure<OperationCanceledException>(load);
                Task close = service.CloseAsync();
                try
                {
                    backend.Load.TrySetResult(backend.NewAsset());
                    await backend.ReleaseStarted.Task.WaitAsync(Timeout);
                    Assert.False(close.IsCompleted || backend.Disposed, "close waits for late asset release");
                    backend.ReleaseGate.TrySetException(new IOException("late asset release failed"));
                    await ExpectFailure<AggregateException>(close);
                    Assert.Equal(0, service.PendingOperationCount, "release failure still settles asset lifetime");
                    Assert.True(backend.Disposed, "independent backend cleanup is completed");
                }
                finally
                {
                    backend.ReleaseGate.TrySetResult(true);
                    try { await close.WaitAsync(Timeout); } catch { }
                }
            }
        }

        internal static async Task LateSuccessfulInitializerUsesScopeFirstRollback()
        {
            foreach (var ownership in new[] { RuntimeOwnership.Owned, RuntimeOwnership.Borrowed })
            {
                var backend = new AssetBackend();
                backend.Load.TrySetResult(backend.NewAsset());
                var resources = new ResourceService(backend);
                var runtime = new GameRuntime(new FrameUpdateManager(FrameUpdateConfig.Default));
                var ready = Signal<bool>(); var finish = Signal<bool>(); int shutdowns = 0;
                runtime.AddPart(new GameRuntimePart("resource-owner", async token =>
                {
                    var lease = await resources.LoadAssetAsync<object>("warmup");
                    runtime.ApplicationScope.OwnAsync("warmup", lease.DisposeAsync);
                    ready.TrySetResult(true);
                    await finish.Task; // Models an underlying operation that cannot be canceled.
                }, () => { shutdowns++; return resources.CloseAsync(); }, ownership));
                Task initialization = runtime.InitializeAsync();
                await ready.Task.WaitAsync(Timeout);
                Task shutdown = runtime.ShutdownAsync();
                try
                {
                    Assert.True(ReferenceEquals(shutdown, runtime.ShutdownAsync()), "concurrent shutdown is shared");
                    finish.TrySetResult(true);
                    await ExpectFailure<OperationCanceledException>(initialization);
                    await shutdown.WaitAsync(Timeout);
                    Assert.Equal(RuntimeScopeState.Closed, runtime.ApplicationScope.State, "scope ended before module shutdown");
                    Assert.Equal(0, resources.ActiveAssetLeaseCount, "late initializer's lease was returned");
                    Assert.Equal(ownership == RuntimeOwnership.Owned ? 1 : 0, shutdowns, "ownership controls module shutdown");
                }
                finally
                {
                    finish.TrySetResult(true);
                    try { await runtime.ApplicationScope.CloseAsync().WaitAsync(Timeout); } catch { }
                    try { await shutdown.WaitAsync(Timeout); } catch { }
                    await resources.CloseAsync().WaitAsync(Timeout);
                }
            }
        }

        internal static async Task AudioFailureReturnsCountAndClosesOthers()
        {
            var backend = new AudioBackend();
            var audio = new AudioService(backend, mainThread: new DispatchProbe());
            var bad = await audio.PlayAsync("bad", new AudioPlayRequest(AudioBus.Sfx));
            await audio.PlayAsync("good", new AudioPlayRequest(AudioBus.Sfx));
            await ExpectFailure<IOException>(bad.StopAsync());
            await ExpectFailure<AggregateException>(audio.CloseAsync());
            Assert.Equal(0, audio.PendingOperationCount, "failed dispatched release returns operation count");
            Assert.Equal(0, audio.ActivePlaybackCount, "other playback is stopped despite failure");
            Assert.Equal(2, backend.Releases, "both clips reached cleanup");
            Assert.True(backend.Disposed, "backend closes even when a clip release fails");
        }

        internal static async Task SceneCancellationRetainsNativeOperation()
        {
            var backend = new NativeSceneBackend();
            var flow = new SceneFlowService(backend);
            using (var cancellation = new CancellationTokenSource())
            {
                Task load = flow.LoadAsync("scene", SceneLoadMode.Additive, token: cancellation.Token);
                cancellation.Cancel();
                await ExpectFailure<OperationCanceledException>(load);
                Task idle = flow.WaitForIdleAsync();
                Task shutdown = flow.ShutdownAsync();
                try
                {
                    Assert.False(backend.Token.CanBeCanceled, "caller cancellation does not truncate the backend task");
                    Assert.False(idle.IsCompleted || shutdown.IsCompleted, "native load still belongs to the service");
                    backend.CompleteNativeLoad();
                    await backend.ReleaseStarted.Task.WaitAsync(Timeout);
                    Assert.False(idle.IsCompleted || shutdown.IsCompleted || backend.Disposed, "native unload is also awaited");
                    backend.ReleaseGate.TrySetResult(true);
                    await idle.WaitAsync(Timeout);
                    await shutdown.WaitAsync(Timeout);
                    Assert.Equal(1, backend.Releases, "late scene is unloaded once");
                    Assert.True(backend.Disposed, "backend disposed after native cleanup");
                }
                finally
                {
                    backend.CompleteNativeLoad(); backend.ReleaseGate.TrySetResult(true);
                    try { await shutdown.WaitAsync(Timeout); } catch { }
                }
            }
        }

        private static async Task ExpectFailure<T>(Task task) where T : Exception
        {
            try { await task.WaitAsync(Timeout); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        private sealed class AssetBackend : IResourceBackend
        {
            internal readonly TaskCompletionSource<ResourceBackendAsset> Load = Signal<ResourceBackendAsset>();
            internal readonly TaskCompletionSource<bool> ReleaseStarted = Signal<bool>();
            internal TaskCompletionSource<bool> ReleaseGate;
            internal int LoadCalls, Releases;
            internal bool Disposed, ReleaseAfterDispose;
            internal ResourceBackendAsset NewAsset() => new ResourceBackendAsset(new object(), async () =>
            {
                ReleaseAfterDispose |= Disposed; Interlocked.Increment(ref Releases); ReleaseStarted.TrySetResult(true);
                if (ReleaseGate != null) await ReleaseGate.Task;
            });
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token)
            { Interlocked.Increment(ref LoadCalls); return Load.Task; }
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token)
                => throw new NotSupportedException();
            public void Dispose() { Disposed = true; }
        }

        private sealed class DispatchProbe : IMainThreadDispatcher
        {
            // Drive the dispatched cleanup path without Unity API or a native thread.
            public bool IsMainThread => false;
            public bool IsAcceptingWork => true;
            public Task RunAsync(Action action, CancellationToken token = default)
            { try { action(); return Task.CompletedTask; } catch (Exception error) { return Task.FromException(error); } }
            public Task<T> RunAsync<T>(Func<T> action, CancellationToken token = default)
            { try { return Task.FromResult(action()); } catch (Exception error) { return Task.FromException<T>(error); } }
            public void StopAcceptingWork() { }
        }

        private sealed class AudioBackend : IAudioBackend
        {
            internal int Releases;
            internal bool Disposed;
            public Task<AudioBackendClip> LoadClipAsync(string key, CancellationToken token) =>
                Task.FromResult(new AudioBackendClip(key, () =>
                { Releases++; if (key == "bad") throw new IOException("audio clip release failed"); }));
            public IAudioPlayback Play(AudioBackendClip clip, AudioPlayRequest request, float volume, Action completed) => new Playback();
            public void Tick() { }
            public void Dispose() { Disposed = true; }
        }

        private sealed class Playback : IAudioPlayback
        {
            public bool IsPlaying { get; private set; } = true;
            public void Stop() { IsPlaying = false; }
            public void Dispose() { Stop(); }
        }

        private sealed class NativeSceneBackend : ISceneBackend
        {
            private readonly TaskCompletionSource<SceneLease> _load = Signal<SceneLease>();
            internal readonly TaskCompletionSource<bool> ReleaseStarted = Signal<bool>();
            internal readonly TaskCompletionSource<bool> ReleaseGate = Signal<bool>();
            internal CancellationToken Token;
            internal int Releases;
            internal bool Disposed;
            public Task<SceneLease> LoadAsync(string key, SceneLoadMode mode, IProgress<float> progress, CancellationToken token)
            {
                Token = token;
                if (token.CanBeCanceled) token.Register(() => _load.TrySetCanceled(token));
                return _load.Task;
            }
            internal void CompleteNativeLoad() => _load.TrySetResult(new SceneLease("scene", new object(), async () =>
            { ReleaseStarted.TrySetResult(true); await ReleaseGate.Task; Releases++; }));
            public void Dispose() { Disposed = true; }
        }
    }
}
