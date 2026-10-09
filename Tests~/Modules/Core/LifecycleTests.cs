using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Audio;
using WFrameWork.Application;
using WFrameWork.Core.ResLoad;
using WFrameWork.Scene;
using WFrameWork.Diagnostics;

namespace WFrameWork.Modules.Tests
{
    internal static class LifecycleTests
    {
        internal static async Task RuntimeConcurrentShutdown()
        {
            var initGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int shutdowns = 0;
            using (var runtime = new GameRuntime(new WFrameWork.Core.FrameUpdate.FrameUpdateManager(WFrameWork.Core.FrameUpdate.FrameUpdateConfig.Default)))
            {
                runtime.AddPart(new GameRuntimePart("pending", async token =>
                {
                    await initGate.Task;
                    token.ThrowIfCancellationRequested();
                }, () => { shutdowns++; return Task.CompletedTask; }));
                Task first = runtime.InitializeAsync();
                Task second = runtime.InitializeAsync();
                Assert.True(ReferenceEquals(first, second), "concurrent initialization shares one task");
                Task close = runtime.ShutdownAsync();
                Assert.True(ReferenceEquals(close, runtime.ShutdownAsync()), "shutdown is idempotent");
                initGate.TrySetResult(true);
                try { await first; throw new Exception("initialization unexpectedly reached running"); }
                catch (OperationCanceledException) { }
                await close;
                Assert.Equal(GameRuntimeState.Stopped, runtime.State, "shutdown during initialization stops");
                Assert.Equal(0, shutdowns, "a part that never initialized is not shut down");
            }
        }

        internal static async Task RuntimeRollback()
        {
            int firstShutdown = 0;
            var runtime = new GameRuntime();
            runtime.AddPart(new GameRuntimePart("first", shutdown: () => { firstShutdown++; return Task.CompletedTask; }));
            runtime.AddPart(new GameRuntimePart("broken", initialize: _ => Task.FromException(new InvalidOperationException("broken"))));
            try { await runtime.InitializeAsync(); throw new Exception("failure was swallowed"); }
            catch (InvalidOperationException) { }
            Assert.Equal(1, firstShutdown, "initialized part was rolled back");
            Assert.Equal(GameRuntimeState.Failed, runtime.State, "failed initialization is terminal");
            try { await runtime.ShutdownAsync(); } catch { }
        }

        internal static async Task ScopeOwnership()
        {
            int owned = 0, borrowed = 0, child = 0;
            var external = new ProbeDisposable(() => borrowed++);
            var scope = new RuntimeScope("application");
            scope.Own(new ProbeDisposable(() => owned++), "owned");
            scope.Borrow(external, "external");
            var nested = scope.CreateChild("scene");
            nested.Own(new ProbeDisposable(() => child++), "child");
            Task close = scope.CloseAsync();
            Assert.True(ReferenceEquals(close, scope.CloseAsync()), "scope close is shared");
            await close;
            Assert.Equal(RuntimeScopeState.Closed, scope.State, "scope closed");
            Assert.Equal(1, owned, "owned resource closed");
            Assert.Equal(1, child, "child scope closed by parent");
            Assert.Equal(0, borrowed, "borrowed resource was not closed");
        }

        internal static async Task ResourceCloseWaitsForLease()
        {
            var backend = new ImmediateResourceBackend();
            var service = new ResourceService(backend);
            ResourceLease<object> lease = await service.LoadAssetAsync<object>("asset");
            Task close = service.CloseAsync();
            Assert.False(close.IsCompleted, "service waits for the external lease");
            lease.Dispose();
            await close;
            Assert.Equal(1, backend.ReleaseCount, "backend release occurs after lease release");
        }

        internal static async Task ResourceAsyncReleaseIsAwaited()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new AsyncReleaseResourceBackend(release.Task, false);
            var service = new ResourceService(backend);
            ResourceLease<object> lease = await service.LoadAssetAsync<object>("asset");
            Task close = service.CloseAsync();
            Assert.False(close.IsCompleted, "close waits for the external asset lease");
            Task dispose = lease.DisposeAsync();
            Assert.False(dispose.IsCompleted, "lease disposal exposes backend async release");
            release.TrySetResult(true);
            await dispose; await close;
            Assert.Equal(1, backend.ReleaseCount, "async backend release occurs once");
        }

        internal static async Task ResourceInstanceAsyncReleaseIsAwaited()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new AsyncReleaseResourceBackend(release.Task, true);
            var service = new ResourceService(backend);
            ResourceInstanceLease instance = await service.InstantiateAsync("instance");
            Task close = service.CloseAsync();
            Assert.False(close.IsCompleted, "close waits for an active instance lease");
            Task dispose = instance.DisposeAsync();
            Assert.False(dispose.IsCompleted, "instance disposal exposes backend async release");
            release.TrySetResult(true);
            await dispose; await close;
            Assert.Equal(1, backend.ReleaseCount, "async instance release occurs once");
        }

        internal static async Task AudioCloseAwaitsClipAndPlaybackRelease()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new ControlledAudioBackend(release.Task);
            var service = new AudioService(backend);
            AudioPlaybackHandle playback = await service.PlayAsync("click", new AudioPlayRequest(AudioBus.Sfx));
            Assert.Equal(1, service.ActivePlaybackCount, "audio playback is tracked");
            Task close = service.CloseAsync();
            Assert.False(close.IsCompleted, "audio close waits for clip release");
            release.TrySetResult(true);
            await close;
            Assert.Equal(1, backend.ReleaseCount, "audio clip is released once");
            Assert.False(playback.IsPlaying, "playback is stopped before audio close completes");
        }

        internal static async Task GameFlowCancellation()
        {
            var pending = new TaskCompletionSource<SceneLease>(TaskCreationOptions.RunContinuationsAsynchronously);
            int unloads = 0;
            var scope = new RuntimeScope("application");
            var flow = new GameFlowService(_ => pending.Task, lease => lease.ReleaseAsync(),
                () => Task.CompletedTask, () => scope.CreateChild("scene"));
            await flow.EnterMenuAsync();
            Task start = flow.StartGameAsync();
            Assert.True(ReferenceEquals(start, flow.StartGameAsync()), "duplicate start shares transition");
            Task returning = flow.ReturnToMenuAsync();
            pending.TrySetResult(new SceneLease("game", new object(), () => unloads++));
            try { await start; throw new Exception("canceled transition completed as playing"); }
            catch (OperationCanceledException) { }
            await returning;
            Assert.Equal(GameFlowState.Menu, flow.State, "return reaches menu");
            Assert.Equal(1, unloads, "late scene is released once");
            await scope.CloseAsync();
        }

        internal static async Task GameFlowPressure()
        {
            int releases = 0;
            var scope = new RuntimeScope("application");
            var flow = new GameFlowService(_ => Task.FromResult(new SceneLease("game", new object(), () => releases++)),
                lease => lease.ReleaseAsync(), () => Task.CompletedTask, () => scope.CreateChild("scene"));
            await flow.EnterMenuAsync();
            for (int i = 0; i < 20; i++)
            {
                await flow.StartGameAsync();
                Assert.Equal(GameFlowState.Playing, flow.State, "iteration enters playing");
                await flow.ReturnToMenuAsync();
                Assert.Equal(GameFlowState.Menu, flow.State, "iteration returns to menu");
            }
            Assert.Equal(20, releases, "each scene lease released exactly once");
            await scope.CloseAsync();
        }

        internal static async Task GameFlowReturnIsMerged()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var scope = new RuntimeScope("application");
            var flow = new GameFlowService(_ => Task.FromResult(new SceneLease("game", new object(), async () => await release.Task)),
                lease => lease.ReleaseAsync(), () => Task.CompletedTask, () => scope.CreateChild("scene"));
            await flow.EnterMenuAsync(); await flow.StartGameAsync();
            Task first = flow.ReturnToMenuAsync();
            Task second = flow.ReturnToMenuAsync();
            Assert.True(ReferenceEquals(first, second), "concurrent return requests share one task");
            release.TrySetResult(true); await first;
            Assert.Equal(GameFlowState.Menu, flow.State, "merged return reaches menu");
            await scope.CloseAsync();
        }

        internal static async Task ScopeClosesChildrenBeforeParentCleanups()
        {
            var order = new List<string>();
            var scope = new RuntimeScope("application");
            var child = scope.CreateChild("scene");
            child.OwnAsync("child", () => { order.Add("child"); return Task.CompletedTask; });
            scope.OwnAsync("parent", () => { order.Add("parent"); return Task.CompletedTask; });
            await scope.CloseAsync();
            Assert.Equal("child", order[0], "child user closes before parent resources");
            Assert.Equal("parent", order[1], "parent cleanup runs after child cleanup");
        }

        internal static async Task SceneLeaseSharesReleaseTask()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int count = 0;
            var lease = new SceneLease("scene", new object(), async () => { await release.Task; count++; });
            Task first = lease.ReleaseAsync(); Task second = lease.ReleaseAsync();
            Assert.True(ReferenceEquals(first, second), "scene release is idempotent and shared");
            Assert.False(first.IsCompleted, "scene release does not complete early");
            release.TrySetResult(true); await first;
            Assert.Equal(1, count, "scene backend release runs once");
        }

        internal static Task DiagnosticsBounded()
        {
            var registry = new DiagnosticRegistry(2);
            int captures = 0;
            using (registry.Register("test", snapshot => { captures++; snapshot.Set("value", captures); }))
            {
                var first = registry.Capture(); registry.Capture(); registry.Capture();
                Assert.Equal("1", first.Values["value"], "snapshot is sampled on demand");
                Assert.Equal(3, captures, "provider invoked once per capture");
                Assert.Equal(2, registry.HistoryCount, "history capacity is bounded");
            }
            return Task.CompletedTask;
        }

        internal static async Task SceneUnloadWaits()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new ControlledSceneBackend(release.Task);
            using (var flow = new WFrameWork.Scene.SceneFlowService(backend))
            {
                var lease = await flow.LoadAsync("game");
                Task unload = flow.UnloadAsync(lease);
                Assert.False(unload.IsCompleted, "scene unload does not report before backend completion");
                release.TrySetResult(true);
                await unload;
                Assert.Equal(1, backend.ReleaseCount, "scene backend releases once");
            }
        }

        private sealed class ProbeDisposable : IDisposable
        {
            private Action _dispose;
            internal ProbeDisposable(Action dispose) { _dispose = dispose; }
            public void Dispose() { var action = _dispose; _dispose = null; action?.Invoke(); }
        }

        private sealed class ImmediateResourceBackend : IResourceBackend
        {
            internal int ReleaseCount;
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken cancellationToken)
            { return Task.FromResult(new ResourceBackendAsset(new object(), () => ReleaseCount++)); }
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken cancellationToken)
            { return Task.FromException<ResourceBackendInstance>(new NotSupportedException()); }
            public void Dispose() { }
        }

        private sealed class AsyncReleaseResourceBackend : IResourceBackend
        {
            private readonly Task _release;
            private readonly bool _instance;
            internal int ReleaseCount;
            internal AsyncReleaseResourceBackend(Task release, bool instance) { _release = release; _instance = instance; }
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type requestedType, IProgress<float> progress, CancellationToken cancellationToken)
            { return Task.FromResult(new ResourceBackendAsset(new object(), async () => { await _release; ReleaseCount++; })); }
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken cancellationToken)
            {
                if (!_instance) return Task.FromException<ResourceBackendInstance>(new InvalidOperationException("not an instance backend"));
                return Task.FromResult(new ResourceBackendInstance(new object(), async () => { await _release; ReleaseCount++; }, true));
            }
            public void Dispose() { }
        }

        private sealed class ControlledAudioBackend : IAudioBackend
        {
            private readonly Task _release;
            internal int ReleaseCount;
            internal ControlledAudioBackend(Task release) { _release = release; }
            public Task<AudioBackendClip> LoadClipAsync(string key, CancellationToken token)
            { return Task.FromResult(new AudioBackendClip(new object(), async () => { await _release; ReleaseCount++; }, true)); }
            public IAudioPlayback Play(AudioBackendClip clip, AudioPlayRequest request, float effectiveVolume, Action completed)
            { return new ControlledPlayback(); }
            public void Tick() { }
            public void Dispose() { }
        }

        private sealed class ControlledPlayback : IAudioPlayback
        {
            private bool _playing = true;
            public bool IsPlaying => _playing;
            public void Stop() { _playing = false; }
            public void Dispose() { Stop(); }
        }

        private sealed class ControlledSceneBackend : WFrameWork.Scene.ISceneBackend
        {
            private readonly Task _release;
            internal int ReleaseCount;
            internal ControlledSceneBackend(Task release) { _release = release; }
            public Task<SceneLease> LoadAsync(string key, WFrameWork.Scene.SceneLoadMode mode, IProgress<float> progress, CancellationToken token)
            { return Task.FromResult(new SceneLease(key, new object(), async () => { await _release; ReleaseCount++; })); }
            public void Dispose() { }
        }
    }
}
