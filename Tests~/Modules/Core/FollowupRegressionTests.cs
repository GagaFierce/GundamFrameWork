using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Application;
using WFrameWork.Core.Editor;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.ResLoad;
using WFrameWork.Pool;
using WFrameWork.Scene;
using WFrameWork.UI;

namespace WFrameWork.Modules.Tests
{
    internal static class FollowupRegressionTests
    {
        internal static async Task ResourceInstanceFailurePaths()
        {
            await VerifyInstanceFailure(() => throw new IOException("synchronous failure"));
            await VerifyInstanceFailure(() => Task.FromException<ResourceBackendInstance>(new IOException("faulted task")));

            var delayedFailure = new TaskCompletionSource<ResourceBackendInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayedBackend = new InstanceBackend(() => delayedFailure.Task);
            var delayedService = new ResourceService(delayedBackend);
            try
            {
                Task request = delayedService.InstantiateAsync("delayed");
                delayedFailure.TrySetException(new IOException("delayed failure"));
                await ExpectFailure<IOException>(request, "delayed instance failure");
                await delayedService.CloseAsync().WaitAsync(TestTimeout);
                Assert.True(delayedBackend.Disposed, "delayed failure closed the backend");
            }
            finally { await CloseQuietly(delayedService); }

            using (var canceledOperation = new CancellationTokenSource())
            {
                canceledOperation.Cancel();
                var canceledBackend = new InstanceBackend(() => Task.FromCanceled<ResourceBackendInstance>(canceledOperation.Token));
                var canceledService = new ResourceService(canceledBackend);
                try
                {
                    await ExpectFailure<TaskCanceledException>(canceledService.InstantiateAsync("canceled"), "backend cancellation");
                    await canceledService.CloseAsync().WaitAsync(TestTimeout);
                    Assert.True(canceledBackend.Disposed, "backend cancellation closed the backend");
                }
                finally { await CloseQuietly(canceledService); }
            }

            var lateOperation = new TaskCompletionSource<ResourceBackendInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lateBackend = new InstanceBackend(() => lateOperation.Task);
            var lateService = new ResourceService(lateBackend);
            using (var callerCanceled = new CancellationTokenSource())
            {
                try
                {
                    callerCanceled.Cancel();
                    Task request = lateService.InstantiateAsync("late", cancellationToken: callerCanceled.Token);
                    await ExpectFailure<OperationCanceledException>(request, "pre-canceled instance request");
                    Task close = lateService.CloseAsync();
                    Assert.False(close.IsCompleted, "close waits for a pre-canceled late instance");
                    lateOperation.TrySetResult(new ResourceBackendInstance(new object(), async () =>
                    {
                        releaseStarted.TrySetResult(true);
                        await releaseGate.Task;
                    }, true));
                    await releaseStarted.Task.WaitAsync(TestTimeout);
                    Assert.False(close.IsCompleted, "close waits for late instance release");
                    releaseGate.TrySetResult(true);
                    await close.WaitAsync(TestTimeout);
                    Assert.True(lateBackend.Disposed, "late instance is released before backend close");
                }
                finally
                {
                    releaseGate.TrySetResult(true);
                    lateOperation.TrySetResult(new ResourceBackendInstance(new object(), () => Task.CompletedTask, true));
                    await CloseQuietly(lateService);
                }
            }

            var successBackend = new InstanceBackend(() => Task.FromResult(new ResourceBackendInstance(new object(), () => Task.CompletedTask, true)));
            var successService = new ResourceService(successBackend);
            try
            {
                ResourceInstanceLease lease = await successService.InstantiateAsync("success");
                Task close = successService.CloseAsync();
                Assert.False(close.IsCompleted, "close waits for a delivered instance lease");
                await lease.DisposeAsync().WaitAsync(TestTimeout);
                await close.WaitAsync(TestTimeout);
                Assert.Equal(0, successService.PendingOperationCount, "successful instance tracking is removed");
                Assert.True(successBackend.Disposed, "successful instance closes the backend after release");
            }
            finally { await CloseQuietly(successService); }

            int mixedCalls = 0;
            var mixedBackend = new InstanceBackend(() =>
            {
                mixedCalls++;
                return mixedCalls == 1
                    ? Task.FromResult(new ResourceBackendInstance(new object(), () => Task.CompletedTask, true))
                    : Task.FromException<ResourceBackendInstance>(new IOException("mixed failure"));
            });
            var mixedService = new ResourceService(mixedBackend);
            try
            {
                Task<ResourceInstanceLease> validRequest = mixedService.InstantiateAsync("valid");
                Task<ResourceInstanceLease> failedRequest = mixedService.InstantiateAsync("failed-with-valid");
                ResourceInstanceLease validLease = await validRequest;
                await ExpectFailure<IOException>(failedRequest, "mixed instance failure");
                Task close = mixedService.CloseAsync();
                Assert.False(close.IsCompleted, "mixed close waits for the valid lease");
                await validLease.DisposeAsync().WaitAsync(TestTimeout);
                await close.WaitAsync(TestTimeout);
                Assert.True(mixedBackend.Disposed, "one failed request does not prevent another lease from closing");
            }
            finally { await CloseQuietly(mixedService); }
        }

        private static async Task VerifyInstanceFailure(Func<Task<ResourceBackendInstance>> operation)
        {
            var backend = new InstanceBackend(operation);
            var service = new ResourceService(backend);
            try
            {
                await ExpectFailure<IOException>(service.InstantiateAsync("failed"), "instance failure");
                await service.CloseAsync().WaitAsync(TestTimeout);
                Assert.Equal(0, service.PendingOperationCount, "failed instance lifecycle is removed");
                Assert.True(backend.Disposed, "failed instance closes the backend");
            }
            finally { await CloseQuietly(service); }
        }

        internal static async Task RuntimeRollbackClosesScopeBeforeResources()
        {
            var backend = new InstanceBackend(() => Task.FromResult(new ResourceBackendInstance(new object(), () => Task.CompletedTask, true)));
            var resources = new ResourceService(backend);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var continueInitialization = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new GameRuntime(new FrameUpdateManager(FrameUpdateConfig.Default));
            int resourceShutdowns = 0;
            runtime.AddPart(new GameRuntimePart("resources", shutdown: async () => { resourceShutdowns++; await resources.CloseAsync(); }));
            runtime.AddPart(new GameRuntimePart("consumer", async token =>
            {
                ResourceLease<object> lease = await resources.LoadAssetAsync<object>("asset");
                RuntimeScope child = runtime.ApplicationScope.CreateChild("consumer");
                child.OwnAsync("child lease", lease.DisposeAsync);
                runtime.ApplicationScope.OwnAsync("application lease", () => Task.CompletedTask);
                entered.TrySetResult(true);
                await continueInitialization.Task;
                throw new IOException("consumer initialization failed");
            }));

            try
            {
                Task initialize = runtime.InitializeAsync();
                await entered.Task.WaitAsync(TestTimeout);
                Task shutdown = runtime.ShutdownAsync();
                Assert.True(ReferenceEquals(shutdown, runtime.ShutdownAsync()), "rollback and external shutdown share the shutdown task");
                continueInitialization.TrySetResult(true);
                await ExpectFailure<IOException>(initialize, "failed initialization");
                try { await shutdown.WaitAsync(TestTimeout); }
                catch (AggregateException error) { Assert.True(error.ToString().Contains("consumer initialization failed", StringComparison.Ordinal), "shared shutdown preserves the initialization error"); }
                Assert.Equal(RuntimeScopeState.Closed, runtime.ApplicationScope.State, "initialization failure closes the application scope");
                Assert.Equal(0, resources.ActiveAssetLeaseCount, "scope releases consumer leases before resources");
                Assert.Equal(1, resourceShutdowns, "resource part shuts down once");
                Assert.True(backend.Disposed, "resource backend is closed after scope cleanup");
            }
            finally
            {
                continueInitialization.TrySetResult(true);
                try { await runtime.ShutdownAsync().WaitAsync(TestTimeout); } catch { }
                await CloseQuietly(resources);
            }
            await VerifyCanceledInitializationWithLease();
        }

        private static async Task VerifyCanceledInitializationWithLease()
        {
            var backend = new InstanceBackend(() => Task.FromResult(new ResourceBackendInstance(new object(), () => Task.CompletedTask, true)));
            var resources = new ResourceService(backend);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var runtime = new GameRuntime(new FrameUpdateManager(FrameUpdateConfig.Default));
            runtime.AddPart(new GameRuntimePart("resources", shutdown: resources.CloseAsync));
            runtime.AddPart(new GameRuntimePart("canceling consumer", async token =>
            {
                ResourceLease<object> lease = await resources.LoadAssetAsync<object>("cancel-asset");
                runtime.ApplicationScope.OwnAsync("cancel lease", lease.DisposeAsync);
                entered.TrySetResult(true);
                await gate.Task;
                token.ThrowIfCancellationRequested();
            }));
            try
            {
                Task initialize = runtime.InitializeAsync();
                await entered.Task.WaitAsync(TestTimeout);
                Task shutdown = runtime.ShutdownAsync();
                gate.TrySetResult(true);
                await ExpectFailure<OperationCanceledException>(initialize, "initialization cancellation with a lease");
                await shutdown.WaitAsync(TestTimeout);
                Assert.Equal(0, resources.ActiveAssetLeaseCount, "canceled initialization returns its scope lease");
                Assert.True(backend.Disposed, "canceled initialization closes resources after scope cleanup");
            }
            finally
            {
                gate.TrySetResult(true);
                try { await runtime.ShutdownAsync().WaitAsync(TestTimeout); } catch { }
                await CloseQuietly(resources);
            }
        }

        internal static async Task RuntimeRollbackPreservesCleanupErrors()
        {
            var runtime = new GameRuntime();
            runtime.AddPart(new GameRuntimePart("broken", initialize: _ =>
            {
                runtime.ApplicationScope.OwnAsync("failing cleanup", () => Task.FromException(new InvalidOperationException("cleanup failure")));
                return Task.FromException(new IOException("initialization failure"));
            }));
            try
            {
                try { await runtime.InitializeAsync(); throw new Exception("initialization unexpectedly succeeded"); }
                catch (AggregateException error)
                {
                    string text = error.ToString();
                    Assert.True(text.Contains("initialization failure", StringComparison.Ordinal), "original initialization error is preserved");
                    Assert.True(text.Contains("cleanup failure", StringComparison.Ordinal), "rollback error is preserved");
                }
                Assert.Equal(RuntimeScopeState.Failed, runtime.ApplicationScope.State, "scope exposes cleanup failure");
                try { await runtime.ShutdownAsync(); } catch (AggregateException) { }
            }
            finally { try { await runtime.ShutdownAsync().WaitAsync(TestTimeout); } catch { } }
        }

        internal static async Task SceneCancellationBoundaries()
        {
            var backend = new SceneBackend();
            var flow = new SceneFlowService(backend);
            try
            {
                backend.Enqueue(Task.FromResult(backend.NewLease("old")));
                await flow.LoadAsync("old", SceneLoadMode.Additive);
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    await ExpectFailure<OperationCanceledException>(flow.LoadAsync("new", SceneLoadMode.Single, token: canceled.Token), "pre-canceled single scene");
                }
                using (var canceled = new CancellationTokenSource())
                {
                    canceled.Cancel();
                    await ExpectFailure<OperationCanceledException>(flow.LoadAsync("additive", SceneLoadMode.Additive, token: canceled.Token), "pre-canceled additive scene");
                }
                Assert.Equal(1, backend.LoadCalls, "pre-canceled scene requests do not call the backend");
                Assert.Equal(0, backend.ReleaseCalls, "pre-canceled scene requests do not unload the old scene");
                Assert.Equal("old", flow.CurrentSceneKey, "old scene remains current before commit");

                var pending = new TaskCompletionSource<SceneLease>(TaskCreationOptions.RunContinuationsAsynchronously);
                backend.Enqueue(pending.Task);
                using (var canceled = new CancellationTokenSource())
                {
                    Task request = flow.LoadAsync("late", SceneLoadMode.Additive, token: canceled.Token);
                    canceled.Cancel();
                    await ExpectFailure<OperationCanceledException>(request, "in-flight scene cancellation");
                    pending.TrySetResult(backend.NewLease("late"));
                    await flow.WaitForIdleAsync().WaitAsync(TestTimeout);
                }
                Assert.Equal(1, backend.ReleaseCalls, "late scene result is released exactly once");
                backend.Enqueue(Task.FromResult(backend.NewLease("next")));
                await flow.LoadAsync("next", SceneLoadMode.Additive);
                Assert.Equal("next", flow.CurrentSceneKey, "a legal request works after cancellation cleanup");
            }
            finally
            {
                try { await flow.ShutdownAsync().WaitAsync(TestTimeout); } catch { }
            }
        }

        internal static async Task UiSingleGenerationReopenAndRetry()
        {
            var provider = new QueuedUiProvider();
            var factory = new AsyncPanelFactory();
            var manager = new UiPanelManager(provider, factory);
            var id = new UiPanelId("panel");
            manager.Register(new UiPanelDefinition(id, "panel"));
            try
            {
                provider.EnqueueSuccess();
                Task<UiPanelHandle> firstOpen = manager.OpenAsync(id);
                provider.CompleteNext();
                UiPanelHandle first = await firstOpen;
                Task firstClose = first.CloseAsync();
                provider.EnqueueSuccess();
                Task<UiPanelHandle> secondOpen = manager.OpenAsync(id);
                Task<UiPanelHandle> thirdOpen = manager.OpenAsync(id);
                Assert.True(ReferenceEquals(secondOpen, thirdOpen), "single loading generation merges all open callers");
                provider.CompleteNext();
                UiPanelHandle second = await secondOpen;
                UiPanelHandle third = await thirdOpen;
                Assert.True(ReferenceEquals(second, third), "closing history does not create a second live single generation");
                Assert.Equal(2, factory.Created.Count, "only one replacement generation is created");
                Task all = manager.CloseAllAsync();
                Assert.False(all.IsCompleted, "CloseAll waits for every generation");
                factory.ReleaseAll();
                await firstClose.WaitAsync(TestTimeout);
                await all.WaitAsync(TestTimeout);
                Assert.Equal(0, manager.OpenPanelCount, "CloseAll removes old and current generations");
            }
            finally { try { await manager.CloseAllAsync().WaitAsync(TestTimeout); } catch { } factory.ReleaseAll(); }

            var retryProvider = new QueuedUiProvider();
            var retryFactory = new AsyncPanelFactory();
            var retryManager = new UiPanelManager(retryProvider, retryFactory);
            retryManager.Register(new UiPanelDefinition(id, "panel"));
            try
            {
                retryProvider.EnqueueFailure(new InvalidOperationException("panel load failure"));
                await ExpectFailure<InvalidOperationException>(retryManager.OpenAsync(id), "failed panel generation");
                retryProvider.EnqueueSuccess();
                Task<UiPanelHandle> retryOpen = retryManager.OpenAsync(id);
                retryProvider.CompleteNext();
                UiPanelHandle retried = await retryOpen;
                Assert.True(retried.IsOpen, "failed generation is removed so a later open retries");
                retryFactory.ReleaseAll();
                await retryManager.CloseAllAsync().WaitAsync(TestTimeout);
            }
            finally { try { await retryManager.CloseAllAsync().WaitAsync(TestTimeout); } catch { } retryFactory.ReleaseAll(); }
        }

        internal static async Task ResourceCloseAggregatesReleaseFailures()
        {
            var backend = new AssetBackend();
            var service = new ResourceService(backend);
            try
            {
                ResourceLease<object> beforeClose = await service.LoadAssetAsync<object>("before");
                await ExpectFailure<IOException>(beforeClose.DisposeAsync(), "release failure before close");
                Task close = service.CloseAsync();
                await ExpectFailure<AggregateException>(close, "close reports a prior release failure");
                Assert.True(backend.Disposed, "backend closes even when a release fails");
            }
            finally { await CloseQuietly(service); }

            var delayedBackend = new AssetBackend();
            var delayedService = new ResourceService(delayedBackend);
            try
            {
                ResourceLease<object> during = await delayedService.LoadAssetAsync<object>("during");
                Task close = delayedService.CloseAsync();
                Task release = during.DisposeAsync();
                await delayedBackend.ReleaseStarted.Task.WaitAsync(TestTimeout);
                delayedBackend.ReleaseGate.TrySetException(new IOException("in-flight release failure"));
                await ExpectFailure<IOException>(release, "in-flight release failure");
                await ExpectFailure<AggregateException>(close, "close reports an in-flight release failure");
                Assert.True(delayedBackend.Disposed, "close continues to backend disposal after release failure");
            }
            finally
            {
                delayedBackend.ReleaseGate.TrySetResult(true);
                await CloseQuietly(delayedService);
            }

            var mixedBackend = new AssetBackend();
            var mixedService = new ResourceService(mixedBackend);
            try
            {
                ResourceLease<object> good = await mixedService.LoadAssetAsync<object>("good");
                ResourceLease<object> bad = await mixedService.LoadAssetAsync<object>("bad");
                await good.DisposeAsync().WaitAsync(TestTimeout);
                await ExpectFailure<IOException>(bad.DisposeAsync(), "one resource release failure among successful releases");
                await ExpectFailure<AggregateException>(mixedService.CloseAsync(), "close aggregates one resource failure");
                Assert.True(mixedBackend.Disposed, "independent resource cleanup continues after one release failure");
            }
            finally { await CloseQuietly(mixedService); }
        }

        internal static Task EditorEncodingRoundTrips()
        {
            const string body = "using System;\r\nclass 示例 { string Text = \"中文\"; }\r\n";
            AssertEncoding(body, new UTF8Encoding(false), null, "UTF-8 without BOM");
            AssertEncoding(body, new UTF8Encoding(false), new byte[] { 0xEF, 0xBB, 0xBF }, "UTF-8 BOM");
            AssertEncoding(body, new UnicodeEncoding(false, false, true), new byte[] { 0xFF, 0xFE }, "UTF-16 LE BOM");
            AssertEncoding(body, new UnicodeEncoding(true, false, true), new byte[] { 0xFE, 0xFF }, "UTF-16 BE BOM");
            try { CommentTextTransformer.TransformSource(new byte[] { 0xFF, 0x00, 0x61 }); throw new Exception("invalid source was accepted"); }
            catch (DecoderFallbackException) { }
            try { CommentTextTransformer.TransformUtf8(Encoding.Unicode.GetBytes(body)); throw new Exception("strict UTF-8 accepted UTF-16 data"); }
            catch (DecoderFallbackException) { }
            string path = Path.Combine(Path.GetTempPath(), "gframework-invalid-source-" + Guid.NewGuid().ToString("N") + ".cs");
            byte[] original = new byte[] { 0xFF, 0x00, 0x61 };
            try
            {
                File.WriteAllBytes(path, original);
                try { CommentTextTransformer.TransformSource(File.ReadAllBytes(path)); throw new Exception("invalid file source was accepted"); }
                catch (DecoderFallbackException) { }
                Assert.Equal(Convert.ToBase64String(original), Convert.ToBase64String(File.ReadAllBytes(path)), "failed file transform does not overwrite the source");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            return Task.CompletedTask;
        }

        private static void AssertEncoding(string body, Encoding encoding, byte[] preamble, string label)
        {
            byte[] content = encoding.GetBytes(body);
            byte[] bytes;
            if (preamble == null) bytes = content;
            else
            {
                bytes = new byte[preamble.Length + content.Length];
                Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
                Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);
            }
            byte[] transformed = CommentTextTransformer.TransformSource(bytes);
            Assert.True(HasPrefix(transformed, preamble), label + " preserves BOM");
            int offset = preamble == null ? 0 : preamble.Length;
            string decoded = encoding.GetString(transformed, offset, transformed.Length - offset);
            Assert.True(decoded.Contains(body, StringComparison.Ordinal), label + " preserves source body");
            Assert.Equal(Convert.ToBase64String(transformed), Convert.ToBase64String(CommentTextTransformer.TransformSource(transformed)), label + " is idempotent");
        }

        private static bool HasPrefix(byte[] value, byte[] prefix)
        {
            if (prefix == null) return value.Length < 2 || !(value[0] == 0xFF && value[1] == 0xFE) && !(value[0] == 0xFE && value[1] == 0xFF);
            if (value.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (value[i] != prefix[i]) return false;
            return true;
        }

        internal static Task ObjectPoolReentrantReturnCleanup()
        {
            int destroyed = 0;
            ObjectPool<object> pool = null;
            pool = new ObjectPool<object>(() => new object(), reset: _ => pool.Dispose(), destroy: _ => destroyed++);
            object item = pool.Rent();
            Assert.True(pool.TryReturn(item), "return succeeds when reset closes the pool");
            Assert.Equal(0, pool.AvailableCount, "closed pool is not repopulated by reset");
            Assert.Equal(0, pool.TotalCount, "returning object is destroyed after reset exits");
            Assert.Equal(1, destroyed, "returning object is destroyed once");
            pool.Dispose();

            ObjectPool<object> clearPool = null;
            clearPool = new ObjectPool<object>(() => new object(), reset: _ => clearPool.Clear(), destroy: _ => { });
            clearPool.Warmup(1);
            object clearItem = clearPool.Rent();
            Assert.True(clearPool.TryReturn(clearItem), "reset Clear does not corrupt return ownership");
            clearPool.Dispose();

            ObjectPool<object> duplicatePool = null;
            duplicatePool = new ObjectPool<object>(() => new object(), reset: itemToReset => { Assert.False(duplicatePool.TryReturn(itemToReset), "reentrant return is rejected"); });
            object duplicate = duplicatePool.Rent();
            Assert.True(duplicatePool.TryReturn(duplicate), "outer return remains the owner");
            duplicatePool.Dispose();

            int failedDestroy = 0;
            var throwingPool = new ObjectPool<object>(() => new object(), reset: _ => { throw new InvalidOperationException("reset"); }, destroy: _ => { failedDestroy++; });
            object throwing = throwingPool.Rent();
            try { throwingPool.TryReturn(throwing); throw new Exception("reset failure was swallowed"); }
            catch (InvalidOperationException) { }
            Assert.Equal(1, failedDestroy, "reset failure still destroys the object");
            throwingPool.Dispose();

            int resetCloseDestroy = 0;
            ObjectPool<object> resetClosePool = null;
            resetClosePool = new ObjectPool<object>(() => new object(), reset: _ => { resetClosePool.Dispose(); throw new InvalidOperationException("reset after close"); }, destroy: _ => resetCloseDestroy++);
            object resetCloseItem = resetClosePool.Rent();
            try { resetClosePool.TryReturn(resetCloseItem); throw new Exception("reset failure after close was swallowed"); }
            catch (InvalidOperationException) { }
            Assert.Equal(1, resetCloseDestroy, "reset failure after close still destroys the returning object");

            int destroyFailures = 0;
            var destroyThrowingPool = new ObjectPool<object>(() => new object(), destroy: _ => { destroyFailures++; throw new InvalidOperationException("destroy"); }, maxCapacity: 2);
            destroyThrowingPool.Warmup(2);
            try { destroyThrowingPool.Dispose(); throw new Exception("destroy failure was swallowed"); }
            catch (AggregateException) { }
            Assert.Equal(2, destroyFailures, "destroy errors do not skip other owned objects");
            Assert.Equal(0, destroyThrowingPool.TotalCount, "destroy errors do not retain pool ownership");

            int factoryDestroy = 0;
            ObjectPool<object> factoryPool = null;
            factoryPool = new ObjectPool<object>(() => { factoryPool.Dispose(); return new object(); }, destroy: _ => factoryDestroy++);
            try { factoryPool.Rent(); throw new Exception("factory reentrant close was ignored"); }
            catch (ObjectDisposedException) { }
            Assert.Equal(1, factoryDestroy, "factory result is destroyed when creation observes close");
            return Task.CompletedTask;
        }

        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);

        private static async Task ExpectFailure<T>(Task task, string name) where T : Exception
        {
            try { await task.WaitAsync(TestTimeout); throw new Exception(name + " unexpectedly succeeded"); }
            catch (T) { }
        }

        private static async Task CloseQuietly(ResourceService service)
        {
            try { await service.CloseAsync().WaitAsync(TestTimeout); } catch { }
        }

        private sealed class InstanceBackend : IResourceBackend
        {
            private readonly Func<Task<ResourceBackendInstance>> _instantiate;
            internal bool Disposed;
            internal InstanceBackend(Func<Task<ResourceBackendInstance>> instantiate) { _instantiate = instantiate; }
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token)
                => Task.FromResult(new ResourceBackendAsset(new object(), () => { }));
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token)
                => _instantiate();
            public void Dispose() { Disposed = true; }
        }

        private sealed class SceneBackend : ISceneBackend
        {
            private readonly Queue<Task<SceneLease>> _responses = new Queue<Task<SceneLease>>();
            internal int LoadCalls;
            internal int ReleaseCalls;
            internal bool Disposed;
            internal void Enqueue(Task<SceneLease> response) { _responses.Enqueue(response); }
            internal SceneLease NewLease(string key) => new SceneLease(key, new object(), () => ReleaseCalls++);
            public Task<SceneLease> LoadAsync(string key, SceneLoadMode mode, IProgress<float> progress, CancellationToken token)
            { LoadCalls++; return _responses.Dequeue(); }
            public void Dispose() { Disposed = true; }
        }

        private sealed class QueuedUiProvider : IUiResourceProvider
        {
            private readonly Queue<TaskCompletionSource<UiResourceHandle>> _responses = new Queue<TaskCompletionSource<UiResourceHandle>>();
            private readonly Queue<TaskCompletionSource<UiResourceHandle>> _active = new Queue<TaskCompletionSource<UiResourceHandle>>();
            internal void EnqueueSuccess() { _responses.Enqueue(new TaskCompletionSource<UiResourceHandle>(TaskCreationOptions.RunContinuationsAsynchronously)); }
            internal void EnqueueFailure(Exception error)
            {
                var source = new TaskCompletionSource<UiResourceHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
                source.TrySetException(error); _responses.Enqueue(source);
            }
            internal void CompleteNext()
            {
                TaskCompletionSource<UiResourceHandle> source = _active.Dequeue();
                source.TrySetResult(new UiResourceHandle(new object()));
            }
            public Task<UiResourceHandle> LoadAsync(string key, CancellationToken token)
            {
                TaskCompletionSource<UiResourceHandle> source = _responses.Dequeue();
                if (!source.Task.IsCompleted) _active.Enqueue(source);
                return source.Task;
            }
        }

        private sealed class AsyncPanelFactory : IUiPanelFactory
        {
            internal readonly List<AsyncPanel> Created = new List<AsyncPanel>();
            public IUiPanelInstance Create(UiPanelDefinition definition, UiResourceHandle resource)
            { var panel = new AsyncPanel(); Created.Add(panel); return panel; }
            internal void ReleaseAll() { for (int i = 0; i < Created.Count; i++) Created[i].Gate.TrySetResult(true); }
        }

        private sealed class AsyncPanel : IUiPanelInstance, IUiAsyncPanelInstance
        {
            internal readonly TaskCompletionSource<bool> Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool IsAlive => !Gate.Task.IsCompleted;
            public bool RequiresContinuousUpdate => false;
            public void SetVisible(bool visible) { }
            public void OnOpened(object argument) { }
            public void OnShown() { }
            public void OnHidden() { }
            public void OnClosed() { }
            public void OnUpdate(in UiPanelUpdateContext context) { }
            public Task DisposeAsync() => Gate.Task;
            public void Dispose() { Gate.TrySetResult(true); }
        }

        private sealed class AssetBackend : IResourceBackend
        {
            internal readonly TaskCompletionSource<bool> ReleaseStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> ReleaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Disposed;
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token)
            {
                if (key == "during")
                    return Task.FromResult(new ResourceBackendAsset(new object(), async () => { ReleaseStarted.TrySetResult(true); await ReleaseGate.Task; throw new IOException("in-flight release failure"); }));
                if (key == "good") return Task.FromResult(new ResourceBackendAsset(new object(), () => { }));
                return Task.FromResult(new ResourceBackendAsset(new object(), () => { throw new IOException("release failure"); }));
            }
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token)
                => Task.FromException<ResourceBackendInstance>(new NotSupportedException());
            public void Dispose() { Disposed = true; }
        }
    }
}
