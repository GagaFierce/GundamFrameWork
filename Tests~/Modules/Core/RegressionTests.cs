using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Application;
using WFrameWork.Audio;
using WFrameWork.Config;
using WFrameWork.Core.Actions;
using WFrameWork.Core.Editor;
using WFrameWork.Core.ResLoad;
using WFrameWork.Input;
using WFrameWork.Pool;
using WFrameWork.UI;

namespace WFrameWork.Modules.Tests
{
    internal static class RegressionTests
    {
        internal static async Task SaveCancellationOwnsOnlyItsTempFile()
        {
            var files = new BlockingFileStore();
            using (var service = new SaveService<string>(files, new StringCodec(), 1))
            using (var canceled = new CancellationTokenSource())
            {
                Task first = Task.Run(() => service.SaveAsync("same", "first"));
                await files.AtReplace.Task;
                Task second = service.SaveAsync("same", "second", canceled.Token);
                canceled.Cancel();
                try { await second; throw new Exception("canceled save unexpectedly completed"); }
                catch (OperationCanceledException) { }
                Assert.Equal(1, files.Files.Keys.Count(path => path.Contains(".tmp.", StringComparison.Ordinal)), "waiting cancellation did not delete another writer temp");
                files.Proceed.Set();
                await first;
                Assert.True(files.Files.Keys.Any(path => !path.Contains(".tmp.", StringComparison.Ordinal)), "first save committed");
            }
        }

        internal static async Task SavePathValidationHasNoSideEffects()
        {
            string root = Path.Combine(Path.GetTempPath(), "gframework-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var service = new SaveService<string>(new SystemTextFileStore(), new StringCodec(), 1, rootPath: root))
                {
                    string outside = Path.Combine(Path.GetDirectoryName(root), Path.GetFileName(root) + "-outside.json");
                    try { await service.SaveAsync(" " + outside, "bad"); throw new Exception("absolute path was accepted"); }
                    catch (ArgumentException) { }
                    try { await service.SaveAsync("nested/../escape", "bad"); throw new Exception("parent traversal was accepted"); }
                    catch (ArgumentException) { }
                    Assert.False(File.Exists(outside), "rejected path created an outside file");
                    await service.SaveAsync("nested/good", "ok");
                    Assert.True(File.Exists(Path.Combine(root, "nested", "good")), "legal relative path still works");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        internal static async Task ScopeDetachesAndReportsCancellationErrors()
        {
            var scope = new RuntimeScope("regression");
            int cleanups = 0;
            scope.OwnAsync("owned", () => { cleanups++; return Task.CompletedTask; });
            await scope.CreateChild("child").CloseAsync();
            scope.Track(Task.CompletedTask);
            using (scope.CancellationToken.Register(() => throw new InvalidOperationException("cancel")))
            {
                try { await scope.CloseAsync(); throw new Exception("scope close unexpectedly succeeded"); }
                catch (AggregateException) { }
            }
            Assert.Equal(0, scope.ChildCount, "closed child was retained");
            Assert.Equal(0, scope.CaptureSnapshot().ActiveOperationCount, "completed operation was retained");
            Assert.Equal(1, cleanups, "cancellation callback skipped owned cleanup");
        }

        internal static async Task RuntimeScopeReleasesResourceBeforeService()
        {
            var backend = new ImmediateInstanceBackend();
            var resources = new ResourceService(backend);
            ResourceLease<object> lease = await resources.LoadAssetAsync<object>("asset");
            var runtime = new GameRuntime(new WFrameWork.Core.FrameUpdate.FrameUpdateManager(WFrameWork.Core.FrameUpdate.FrameUpdateConfig.Default));
            runtime.ApplicationScope.OwnAsync("lease", lease.DisposeAsync);
            runtime.AddPart(new GameRuntimePart("resources", shutdown: resources.CloseAsync));
            await runtime.InitializeAsync();
            Task close = runtime.ShutdownAsync();
            Task completed = await Task.WhenAny(close, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(ReferenceEquals(completed, close), "runtime shutdown deadlocked on resource lease");
            await close;
            Assert.Equal(0, resources.ActiveAssetLeaseCount, "scope lease was not returned");
            Assert.True(backend.Disposed, "resource backend was closed");
        }

        internal static async Task CanceledInstanceCleanupIsInCloseLifetime()
        {
            var backend = new DelayedInstanceBackend();
            var resources = new ResourceService(backend);
            using (var canceled = new CancellationTokenSource())
            {
                Task request = resources.InstantiateAsync("instance", cancellationToken: canceled.Token);
                canceled.Cancel();
                try { await request; throw new Exception("canceled instance request completed"); }
                catch (OperationCanceledException) { }
                Task close = resources.CloseAsync();
                backend.Load.TrySetResult(new ResourceBackendInstance(new object(), async () => { backend.ReleaseStarted.TrySetResult(true); await backend.ReleaseGate.Task; backend.Released = true; }, true));
                await backend.ReleaseStarted.Task;
                Assert.False(close.IsCompleted, "resource close ignored late instance cleanup");
                backend.ReleaseGate.TrySetResult(true);
                await close;
                Assert.True(backend.Released && backend.Disposed, "late instance cleanup completed after backend close");
            }
        }

        internal static async Task UiClosingUsesSharedTasksAndGeneration()
        {
            var provider = new ImmediateUiProvider();
            var factory = new BlockingPanelFactory();
            using (var manager = new UiPanelManager(provider, factory))
            {
                var id = new UiPanelId("panel"); manager.Register(new UiPanelDefinition(id, "panel"));
                UiPanelHandle opened = await manager.OpenAsync(id);
                Task first = opened.CloseAsync(); Task second = opened.CloseAsync();
                Assert.True(ReferenceEquals(first, second) && !second.IsCompleted, "repeated UI close did not share the real close task");
                UiPanelHandle reopened = await manager.OpenAsync(id);
                Assert.True(!ReferenceEquals(opened, reopened) && reopened.IsOpen, "closing UI returned an invalid old handle");
                Task all = manager.CloseAllAsync(); Assert.False(all.IsCompleted, "CloseAll skipped an in-flight destruction");
                factory.ReleaseAll();
                await first; await all;
                Assert.Equal(0, manager.OpenPanelCount, "CloseAll retained a panel generation");
            }
        }

        internal static Task EditorCommentTransformIsSafeAndIdempotent()
        {
            string source = "\uFEFFusing System;\r\nnamespace Demo { class C {} }\r\n";
            string transformed = CommentTextTransformer.AddHeader(source);
            Assert.True(transformed.Contains("using System;\r\n", StringComparison.Ordinal), "header transform removed source code");
            Assert.True(transformed.StartsWith("\uFEFF/", StringComparison.Ordinal), "UTF-8 BOM was not preserved");
            Assert.Equal(transformed, CommentTextTransformer.AddHeader(transformed), "header transform is not idempotent");
            string otherCopyright = "/* Copyright someone else */\nusing System;";
            Assert.True(CommentTextTransformer.AddHeader(otherCopyright).Contains(otherCopyright, StringComparison.Ordinal), "unrelated copyright block was removed");
            return Task.CompletedTask;
        }

        internal static Task AudioStaleRequestCannotStopCurrent()
        {
            return AudioStaleRequestCore();
        }

        private static async Task AudioStaleRequestCore()
        {
            var backend = new ControlledAudioBackend(); using (var service = new AudioService(backend))
            {
                Task<AudioPlaybackHandle> old = service.PlayBgmAsync("old");
                Task<AudioPlaybackHandle> current = service.PlayBgmAsync("current");
                backend.Complete("current"); AudioPlaybackHandle playing = await current;
                backend.Complete("old"); await old;
                Assert.True(playing.IsPlaying, "stale BGM request stopped the current request");
                await service.CloseAsync();
            }
        }

        internal static Task ActionCallbacksCanMutateCollection()
        {
            var actions = new DelayAction();
            actions.Delaydocall(0, action => actions.StopDelayDocall(action));
            actions.Delaydocall(0, action => actions.StopAllDelayDocall());
            actions.Update(1);
            actions.Update(1);
            return Task.CompletedTask;
        }

        internal static Task PoolUsesReferenceOwnershipAndClosesCreation()
        {
            int created = 0;
            using (var pool = new ObjectPool<ValueEqual>(() => { created++; return new ValueEqual(); }))
            {
                ValueEqual owned = pool.Rent(); Assert.False(pool.TryReturn(new ValueEqual()), "value-equal foreign object was accepted");
                Assert.True(pool.TryReturn(owned), "owned instance was rejected");
            }
            var closed = new ObjectPool<object>(() => { created++; return new object(); }); closed.Dispose();
            try { closed.Warmup(1); throw new Exception("closed pool warmed up"); }
            catch (ObjectDisposedException) { }
            Assert.Equal(1, created, "closed warmup called the factory");
            return Task.CompletedTask;
        }

        internal static Task InputUnrelatedContextPreservesEvents()
        {
            var backend = new InjectedInputBackend(); using (var input = new InputService(backend)) using (var reader = input.CreateFixedEventReader())
            {
                var jump = new InputActionId("Gameplay.Jump"); input.RegisterAction(new InputActionDefinition(jump, InputActionType.Button));
                input.RegisterContext("Telemetry", -10, false, false); backend.SetButton(jump, true); input.Update(1);
                using (input.AcquireContext("Telemetry"))
                {
                    Assert.True(reader.TryRead(1, out var item) && item.Phase == InputFixedEventPhase.Pressed, "unrelated context discarded a pending event");
                }
            }
            return Task.CompletedTask;
        }

        private sealed class StringCodec : IUserDataSerializer<string>
        {
            public string Serialize(string value, int version) => version + ":" + value;
            public string Deserialize(string text, out int version) { int split = text.IndexOf(':'); version = int.Parse(text.Substring(0, split)); return text.Substring(split + 1); }
            public string CreateDefault() => "default";
            public string Migrate(string value, int fromVersion, int currentVersion) => value;
        }

        private class MemoryFileStore : ITextFileStore
        {
            internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool Exists(string path) => Files.ContainsKey(path);
            public string Read(string path) => Files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
            public void Write(string path, string contents) => Files[path] = contents;
            public void Delete(string path) => Files.Remove(path);
            public virtual void Replace(string source, string destination, string backup)
            { if (!Files.TryGetValue(source, out var text)) throw new FileNotFoundException(source); if (Files.TryGetValue(destination, out var old)) Files[backup] = old; Files[destination] = text; Files.Remove(source); }
        }

        private sealed class BlockingFileStore : MemoryFileStore
        {
            internal readonly TaskCompletionSource<bool> AtReplace = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly ManualResetEventSlim Proceed = new ManualResetEventSlim();
            public override void Replace(string source, string destination, string backup) { AtReplace.TrySetResult(true); if (!Proceed.Wait(2000)) throw new TimeoutException(); base.Replace(source, destination, backup); }
        }

        private sealed class ImmediateInstanceBackend : IResourceBackend
        {
            internal bool Disposed;
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token) => Task.FromResult(new ResourceBackendAsset(new object(), () => { }));
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token) => Task.FromResult(new ResourceBackendInstance(new object(), () => { }));
            public void Dispose() { Disposed = true; }
        }

        private sealed class DelayedInstanceBackend : IResourceBackend
        {
            internal readonly TaskCompletionSource<ResourceBackendInstance> Load = new TaskCompletionSource<ResourceBackendInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> ReleaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> ReleaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Released; internal bool Disposed;
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token) => Task.FromException<ResourceBackendAsset>(new NotSupportedException());
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token) => Load.Task;
            public void Dispose() { Disposed = true; }
        }

        private sealed class ImmediateUiProvider : IUiResourceProvider
        {
            public Task<UiResourceHandle> LoadAsync(string key, CancellationToken token) => Task.FromResult(new UiResourceHandle(new object()));
        }

        private sealed class BlockingPanelFactory : IUiPanelFactory
        {
            internal readonly List<BlockingPanel> Panels = new List<BlockingPanel>();
            public IUiPanelInstance Create(UiPanelDefinition definition, UiResourceHandle resource) { var panel = new BlockingPanel(); Panels.Add(panel); return panel; }
            internal void ReleaseAll() { for (int i = 0; i < Panels.Count; i++) Panels[i].Done.TrySetResult(true); }
        }

        private sealed class BlockingPanel : IUiPanelInstance, IUiAsyncPanelInstance
        {
            internal readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool IsAlive => !Done.Task.IsCompleted;
            public bool RequiresContinuousUpdate => false;
            public void SetVisible(bool visible) { }
            public void OnOpened(object argument) { }
            public void OnShown() { }
            public void OnHidden() { }
            public void OnClosed() { }
            public void OnUpdate(in UiPanelUpdateContext context) { }
            public Task DisposeAsync() => Done.Task;
            public void Dispose() { Done.TrySetResult(true); }
        }

        private sealed class ControlledAudioBackend : IAudioBackend
        {
            private readonly Dictionary<string, TaskCompletionSource<AudioBackendClip>> _pending = new Dictionary<string, TaskCompletionSource<AudioBackendClip>>();
            public Task<AudioBackendClip> LoadClipAsync(string key, CancellationToken token) { var source = new TaskCompletionSource<AudioBackendClip>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[key] = source; return source.Task; }
            internal void Complete(string key) => _pending[key].TrySetResult(new AudioBackendClip(new object(), () => { }));
            public IAudioPlayback Play(AudioBackendClip clip, AudioPlayRequest request, float effectiveVolume, Action completed) => new Playback();
            public void Tick() { }
            public void Dispose() { }
        }

        private sealed class Playback : IAudioPlayback
        {
            public bool IsPlaying { get; private set; } = true;
            public void Stop() { IsPlaying = false; }
            public void Dispose() { Stop(); }
        }

        private sealed class ValueEqual
        {
            public override bool Equals(object obj) => obj is ValueEqual;
            public override int GetHashCode() => 1;
        }
    }
}
