using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Input;
using WFrameWork.Physics;
using WFrameWork.UI;
using WFrameWork.UIBridge;
using WFrameWork.Core.ResLoad;
using WFrameWork.Pool;
using WFrameWork.Config;
using WFrameWork.Application;
using WFrameWork.Core.Editor;

namespace WFrameWork.Modules.Tests
{
    internal static class Assert
    {
        internal static void True(bool value, string message) { if (!value) throw new Exception(message); }
        internal static void False(bool value, string message) { True(!value, message); }
        internal static void Equal<T>(T expected, T actual, string message)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(message + $" expected={expected} actual={actual}"); }
        internal static void Near(float expected, float actual, string message)
        { if (Math.Abs(expected - actual) > 0.0001f) throw new Exception(message); }
    }

    internal sealed class TestCase
    {
        internal readonly string Name;
        internal readonly Func<Task> Body;
        internal TestCase(string name, Func<Task> body) { Name = name; Body = body; }
    }

    internal sealed class ProbeParticipant : IPhysicsFixedStepParticipant
    {
        internal int Calls;
        internal bool First;
        internal double Total;
        public void OnPhysicsStep(in PhysicsStepContext context) { Calls++; First |= context.IsFirstStep; Total += context.DeltaTime; }
    }

    internal sealed class ControlledProvider : IUiResourceProvider
    {
        internal readonly Dictionary<string, TaskCompletionSource<UiResourceHandle>> Pending =
            new Dictionary<string, TaskCompletionSource<UiResourceHandle>>();
        internal UiResourceHandle LastHandle;
        public Task<UiResourceHandle> LoadAsync(string key, CancellationToken token)
        {
            var source = new TaskCompletionSource<UiResourceHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending[key] = source;
            return source.Task;
        }
        internal void Complete(string key, object asset = null)
        {
            LastHandle = new UiResourceHandle(asset ?? new object());
            Pending[key].TrySetResult(LastHandle);
        }
        internal void Fail(string key, Exception error) { Pending[key].TrySetException(error); }
    }

    internal sealed class ProbePanel : IUiPanelInstance
    {
        internal int Opened;
        internal int Closed;
        internal int Updates;
        internal bool ThrowOnClose;
        internal Action OpenedAction;
        public bool IsAlive { get; private set; } = true;
        public bool RequiresContinuousUpdate => false;
        public void SetVisible(bool visible) { }
        public void OnOpened(object argument) { Opened++; OpenedAction?.Invoke(); }
        public void OnShown() { }
        public void OnHidden() { }
        public void OnClosed() { Closed++; if (ThrowOnClose) throw new InvalidOperationException("callback"); }
        public void OnUpdate(in UiPanelUpdateContext context) { Updates++; }
        public void Dispose() { IsAlive = false; }
    }

    internal static class Program
    {
        private static readonly List<TestCase> Tests = new List<TestCase>
        {
            new TestCase("Input state transitions and repeatable snapshot", InputStateTransitions),
            new TestCase("Input context priority cancels blocked gameplay", InputContextPriority),
            new TestCase("Input fixed events do not repeat across multiple steps", InputFixedEvents),
            new TestCase("Input fixed buffer overflow and expiry are observable", InputBufferBounds),
            new TestCase("Input subscription release and callback isolation", InputSubscriptions),
            new TestCase("Input clear invalidates pending fixed events", InputClearInvalidatesEvents),
            new TestCase("Input subscription mutation is deterministic", InputSubscriptionMutation),
            new TestCase("Input focus suppression requires a fresh release", InputFocusSuppression),
            new TestCase("Physics participant executes once per Physics phase", PhysicsRegistration),
            new TestCase("Physics participant duplicate and dispose", PhysicsLifecycle),
            new TestCase("UI concurrent single open is merged", UiConcurrentOpen),
            new TestCase("UI cancellation and late resource cleanup", UiCancellation),
            new TestCase("UI modal depth and close cleanup", UiModalStack),
            new TestCase("UI load failure and callback exception cleanup", UiFailureCleanup),
            new TestCase("UI dirty refresh uses FrameUpdate attachment", UiFrameRefresh),
            new TestCase("UI reentrant close does not leak modal state", UiReentrantClose),
            new TestCase("UI concurrent caller cancellation is independent", UiIndependentCancellation),
            new TestCase("Resource leases share and release exactly once", ResourceLeaseOwnership),
            new TestCase("Resource asset async release is awaited", LifecycleTests.ResourceAsyncReleaseIsAwaited),
            new TestCase("Resource instance async release is awaited", LifecycleTests.ResourceInstanceAsyncReleaseIsAwaited),
            new TestCase("Audio close awaits playback and clip release", LifecycleTests.AudioCloseAwaitsClipAndPlaybackRelease),
            new TestCase("Object pool rejects foreign and duplicate returns", ObjectPoolLifecycle),
            new TestCase("Save versioning backup and serialized writes", SaveLifecycle),
            new TestCase("Application runtime merges initialization and shutdown", LifecycleTests.RuntimeConcurrentShutdown),
            new TestCase("Application runtime rolls back initialized parts", LifecycleTests.RuntimeRollback),
            new TestCase("Business scopes close children and preserve borrowed dependencies", LifecycleTests.ScopeOwnership),
            new TestCase("Resource close waits for external leases", LifecycleTests.ResourceCloseWaitsForLease),
            new TestCase("Game flow cancellation returns to menu without stale scene", LifecycleTests.GameFlowCancellation),
            new TestCase("Game flow repeated enter and return has stable releases", LifecycleTests.GameFlowPressure),
            new TestCase("Game flow merges concurrent return requests", LifecycleTests.GameFlowReturnIsMerged),
            new TestCase("Business scope closes children before parent cleanups", LifecycleTests.ScopeClosesChildrenBeforeParentCleanups),
            new TestCase("Diagnostics snapshot is on-demand and bounded", LifecycleTests.DiagnosticsBounded),
            new TestCase("Scene unload task completes after backend release", LifecycleTests.SceneUnloadWaits),
            new TestCase("Scene lease shares one async release task", LifecycleTests.SceneLeaseSharesReleaseTask),
            new TestCase("Regression save cancellation owns only its temp file", RegressionTests.SaveCancellationOwnsOnlyItsTempFile),
            new TestCase("Regression save path validation has no side effects", RegressionTests.SavePathValidationHasNoSideEffects),
            new TestCase("Regression scopes detach and report cancellation errors", RegressionTests.ScopeDetachesAndReportsCancellationErrors),
            new TestCase("Regression runtime releases resource before service", RegressionTests.RuntimeScopeReleasesResourceBeforeService),
            new TestCase("Regression canceled instance cleanup is in close lifetime", RegressionTests.CanceledInstanceCleanupIsInCloseLifetime),
            new TestCase("Regression UI closing shares tasks and generation", RegressionTests.UiClosingUsesSharedTasksAndGeneration),
            new TestCase("Regression editor comment transform is safe and idempotent", RegressionTests.EditorCommentTransformIsSafeAndIdempotent),
            new TestCase("Regression stale audio cannot stop current", RegressionTests.AudioStaleRequestCannotStopCurrent),
            new TestCase("Regression action callbacks can mutate collection", RegressionTests.ActionCallbacksCanMutateCollection),
            new TestCase("Regression pool uses reference ownership and closes creation", RegressionTests.PoolUsesReferenceOwnershipAndClosesCreation),
            new TestCase("Regression unrelated input context preserves events", RegressionTests.InputUnrelatedContextPreservesEvents),
            new TestCase("Followup resource instance failure paths close cleanly", FollowupRegressionTests.ResourceInstanceFailurePaths),
            new TestCase("Followup runtime rollback closes scope before resources", FollowupRegressionTests.RuntimeRollbackClosesScopeBeforeResources),
            new TestCase("Followup runtime rollback preserves cleanup errors", FollowupRegressionTests.RuntimeRollbackPreservesCleanupErrors),
            new TestCase("Followup scene cancellation has no pre-commit side effects", FollowupRegressionTests.SceneCancellationBoundaries),
            new TestCase("Followup UI single generation survives repeated reopen", FollowupRegressionTests.UiSingleGenerationReopenAndRetry),
            new TestCase("Followup resource close aggregates release failures", FollowupRegressionTests.ResourceCloseAggregatesReleaseFailures),
            new TestCase("Followup editor encodings round trip safely", FollowupRegressionTests.EditorEncodingRoundTrips),
            new TestCase("Followup object pool reentrant return cleanup", FollowupRegressionTests.ObjectPoolReentrantReturnCleanup),
            new TestCase("Async asset cancellation includes deferred observation", AsyncLifecycleRegressionTests.CanceledAssetIncludesDeferredObservation),
            new TestCase("Async shared asset publication and failure lifetime", AsyncLifecycleRegressionTests.SharedAssetPublicationAndFailures),
            new TestCase("Async canceled asset release failure is awaited", AsyncLifecycleRegressionTests.CanceledAssetReleaseFailureIsAwaited),
            new TestCase("Async late successful initializer uses scope-first rollback", AsyncLifecycleRegressionTests.LateSuccessfulInitializerUsesScopeFirstRollback),
            new TestCase("Async audio failure drains counts and closes other playback", AsyncLifecycleRegressionTests.AudioFailureReturnsCountAndClosesOthers),
            new TestCase("Async scene cancellation retains native load and unload", AsyncLifecycleRegressionTests.SceneCancellationRetainsNativeOperation),
            new TestCase("UI MVVM property notification uses equality", UiMvvmTests.PropertyNotificationUsesEquality),
            new TestCase("UI MVVM two-way binding avoids feedback loop", UiMvvmTests.TwoWayBindingAvoidsFeedbackLoop),
            new TestCase("UI MVVM commands protect CanExecute", UiMvvmTests.CommandsProtectCanExecuteAndDuplicateExecution),
            new TestCase("UI MVVM async command cancellation and duplicate click", UiMvvmTests.AsyncCommandCancellationAndDuplicateClick),
            new TestCase("UI MVVM settings draft apply rollback and failure", UiMvvmTests.SettingsDraftApplyRollbackAndFailure),
            new TestCase("UI MVVM closed binding ignores late updates", UiMvvmTests.ClosedBindingStopsLateUpdates),
            new TestCase("UI MVVM dialog once and bounded toast", UiMvvmTests.DialogCompletesOnceAndToastIsBounded),
        };

        private static async Task Main(string[] args)
        {
            if (args.Length == 1 && args[0] == AsyncLifecycleRegressionTests.AssetProbeArgument)
            {
                Environment.ExitCode = AsyncLifecycleRegressionTests.RunAssetLifetimeProbe();
                return;
            }
            int passed = 0;
            foreach (var test in Tests)
            {
                try { await test.Body(); Console.WriteLine("PASS " + test.Name); passed++; }
                catch (Exception error) { Console.WriteLine("FAIL " + test.Name + " :: " + error); }
            }
            Console.WriteLine("RESULT passed=" + passed + " failed=" + (Tests.Count - passed));
            if (passed != Tests.Count) Environment.ExitCode = 1;
        }

        private static InputService MakeInput(InjectedInputBackend backend, int capacity = 16, long lifetime = 8)
        {
            var service = new InputService(backend, new InputServiceConfig { FixedEventCapacity = capacity, FixedEventLifetimeFrames = lifetime });
            service.RegisterContext("Gameplay", 0, false, true);
            service.RegisterAction(new InputActionDefinition(new InputActionId("Jump"), InputActionType.Button));
            return service;
        }

        private static Task InputStateTransitions()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            {
                var id = new InputActionId("Jump"); backend.SetButton(id, true); var first = input.Update(1);
                Assert.True(first.IsPressed(id), "first sample is pressed"); Assert.True(first.IsPressed(id), "read is repeatable");
                backend.SetButton(id, true); var held = input.Update(2); Assert.True(held.IsHeld(id), "held state"); Assert.False(held.IsPressed(id), "held is not pressed");
                backend.SetButton(id, false); Assert.True(input.Update(3).IsReleased(id), "release state");
                Assert.True(input.Update(4).TryGetState(id, out var final) && final.ButtonPhase == InputButtonPhase.None, "idle state");
            }
            return Task.CompletedTask;
        }

        private static Task InputContextPriority()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            {
                input.RegisterContext("UI", 100, true, false);
                var uiId = new InputActionId("Accept");
                input.RegisterAction(new InputActionDefinition(uiId, InputActionType.Button, "UI"));
                var jump = new InputActionId("Jump"); backend.SetButton(jump, true); backend.SetButton(uiId, true);
                Assert.True(input.Update(1).IsPressed(jump), "gameplay initially visible");
                using (input.AcquireContext("UI"))
                {
                    var blocked = input.Update(2); Assert.True(blocked.IsCanceled(jump), "blocked gameplay canceled"); Assert.True(blocked.IsHeld(uiId), "UI visible");
                }
                Assert.True(input.Update(3).IsHeld(jump), "gameplay resumes without a stuck cancellation");
            }
            return Task.CompletedTask;
        }

        private static Task InputFixedEvents()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            using (var reader = input.CreateFixedEventReader("test"))
            {
                var id = new InputActionId("Jump"); backend.SetButton(id, true); input.Update(1);
                Assert.True(input.Update(2).IsHeld(id), "held frame");
                Assert.True(reader.TryRead(2, out var pressed) && pressed.Phase == InputFixedEventPhase.Pressed, "press buffered");
                Assert.False(reader.TryRead(2, out _), "press consumed once");
                backend.SetButton(id, false); input.Update(3);
                Assert.True(reader.TryRead(3, out var released) && released.Phase == InputFixedEventPhase.Released, "release buffered");
                Assert.False(reader.TryRead(3, out _), "release not repeated");
            }
            return Task.CompletedTask;
        }

        private static Task InputBufferBounds()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend, 2, 1))
            using (var first = input.CreateFixedEventReader("first")) using (var second = input.CreateFixedEventReader("second"))
            {
                var id = new InputActionId("Jump"); backend.SetButton(id, true); input.Update(1); backend.SetButton(id, false); input.Update(2);
                backend.SetButton(id, true); input.Update(3); Assert.True(input.DroppedFixedEventCount > 0, "overflow is counted");
                Assert.True(first.TryRead(3, out _), "reader sees retained event"); Assert.True(second.TryRead(3, out _), "second reader has independent cursor");
                var backend2 = new InjectedInputBackend(); using (var expired = MakeInput(backend2, 4, 1)) using (var r = expired.CreateFixedEventReader())
                { backend2.SetButton(id, true); expired.Update(1); expired.Update(3); Assert.False(r.TryRead(3, out _), "old event expires"); }
            }
            return Task.CompletedTask;
        }

        private static Task InputSubscriptions()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            {
                int callbacks = 0; var id = new InputActionId("Jump");
                var subscription = input.Subscribe(id, _ => { callbacks++; throw new InvalidOperationException(); });
                backend.SetButton(id, true); input.Update(1); Assert.Equal(1, callbacks, "callback called");
                subscription.Dispose(); subscription.Dispose(); backend.SetButton(id, false); input.Update(2); Assert.Equal(1, callbacks, "disposed callback removed");
            }
            return Task.CompletedTask;
        }

        private static Task InputClearInvalidatesEvents()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend)) using (var reader = input.CreateFixedEventReader())
            {
                var id = new InputActionId("Jump"); backend.SetButton(id, true); input.Update(1); input.ClearInput();
                Assert.True(reader.TryRead(2, out var item), "clear publishes a cancellation boundary");
                Assert.Equal(InputFixedEventPhase.Canceled, item.Phase, "old pressed event is invalidated");
                Assert.False(reader.TryRead(2, out _), "no duplicate stale event");
            }
            return Task.CompletedTask;
        }

        private static Task InputSubscriptionMutation()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            {
                var id = new InputActionId("Jump"); int first = 0, second = 0, added = 0;
                InputSubscription self = null; self = input.Subscribe(id, _ => { first++; self.Dispose(); input.Subscribe(id, __ => added++); });
                input.Subscribe(id, _ => second++); backend.SetButton(id, true); input.Update(1);
                Assert.Equal(1, first, "self listener called once"); Assert.Equal(1, second, "next listener was not skipped"); Assert.Equal(0, added, "new listener waits for next dispatch");
                backend.SetButton(id, false); input.Update(2); Assert.Equal(1, added, "added listener participates in a later dispatch");
            }
            return Task.CompletedTask;
        }

        private static Task InputFocusSuppression()
        {
            var backend = new InjectedInputBackend(); using (var input = MakeInput(backend))
            {
                var id = new InputActionId("Jump"); backend.SetButton(id, true); input.Update(1); input.SetApplicationFocus(false);
                backend.SetButton(id, true); Assert.False(input.Update(2).IsHeld(id), "background state is suppressed");
                input.SetApplicationFocus(true); Assert.False(input.Update(3).IsHeld(id), "held background state is not replayed");
                backend.SetButton(id, false); input.Update(4); backend.SetButton(id, true); Assert.True(input.Update(5).IsPressed(id), "fresh press works after release");
            }
            return Task.CompletedTask;
        }

        private static Task PhysicsRegistration()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            {
                var loop = manager.CreateLoop("PhysicsTest"); using (var driver = manager.BindDriver(loop, "driver"))
                using (var dispatcher = new PhysicsStepDispatcher(manager, loop))
                {
                    var participant = new ProbeParticipant(); dispatcher.Register(participant);
                    manager.Tick(driver, UpdatePhase.EarlyUpdate, new FrameTimeSample(1, 0, 0));
                    manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, 0, 0));
                    manager.Tick(driver, UpdatePhase.LateUpdate, new FrameTimeSample(1, 0, 0));
                    Assert.Equal(1, participant.Calls, "one physics callback"); Assert.True(participant.First, "first zero step is explicit");
                    Assert.Near(0, (float)participant.Total, "zero step has zero delta");
                }
            }
            return Task.CompletedTask;
        }

        private static Task PhysicsLifecycle()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            {
                var loop = manager.CreateLoop("PhysicsTest"); using (var driver = manager.BindDriver(loop, "driver"))
                using (var dispatcher = new PhysicsStepDispatcher(manager, loop))
                {
                    var participant = new ProbeParticipant(); var handle = dispatcher.Register(participant);
                    try { dispatcher.Register(participant); throw new Exception("duplicate registration accepted"); }
                    catch (InvalidOperationException) { }
                    handle.Dispose(); handle.Dispose(); Assert.Equal(0, dispatcher.ParticipantCount, "disposed participant removed");
                    manager.Tick(driver, UpdatePhase.EarlyUpdate, new FrameTimeSample(1, .02, .02));
                    manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .02, .02));
                    Assert.Equal(0, participant.Calls, "unregistered participant stopped");
                }
            }
            return Task.CompletedTask;
        }

        private static UiPanelManager MakeUi(ControlledProvider provider, ProbePanel panel,
            IUiModalInputBlocker blocker = null, bool multiple = false)
        {
            var manager = new UiPanelManager(provider, new DelegateUiPanelFactory((_, __) => panel), modalBlocker: blocker);
            manager.Register(new UiPanelDefinition(new UiPanelId("Settings"), "settings", UiPanelLayer.Modal, blocker != null,
                multiple ? UiPanelInstanceMode.Multiple : UiPanelInstanceMode.Single));
            return manager;
        }

        private static async Task UiConcurrentOpen()
        {
            var provider = new ControlledProvider(); var panel = new ProbePanel(); using (var manager = MakeUi(provider, panel))
            {
                var first = manager.OpenAsync(new UiPanelId("Settings")); var second = manager.OpenAsync(new UiPanelId("Settings"));
                Assert.True(ReferenceEquals(first, second), "single loading request is merged"); provider.Complete("settings");
                var handle = await first; Assert.True(handle.IsOpen, "open handle active"); Assert.Equal(1, manager.OpenPanelCount, "one panel");
                await manager.CloseAsync(new UiPanelId("Settings")); Assert.Equal(0, manager.OpenPanelCount, "closed");
            }
        }

        private static async Task UiCancellation()
        {
            var provider = new ControlledProvider(); var panel = new ProbePanel(); using (var manager = MakeUi(provider, panel))
            {
                using (var cts = new CancellationTokenSource())
                {
                    var task = manager.OpenAsync(new UiPanelId("Settings"), cancellationToken: cts.Token); cts.Cancel();
                    try { await task; throw new Exception("canceled open completed"); } catch (TaskCanceledException) { }
                    provider.Complete("settings"); await Task.Yield(); Assert.True(provider.LastHandle.IsReleased, "late resource released");
                }
            }
        }

        private static async Task UiModalStack()
        {
            var provider = new ControlledProvider(); var blocker = new NullUiModalInputBlocker(); var firstPanel = new ProbePanel();
            using (var manager = MakeUi(provider, firstPanel, blocker, true))
            {
                var first = manager.OpenAsync(new UiPanelId("Settings")); provider.Complete("settings"); var firstHandle = await first;
                var second = manager.OpenAsync(new UiPanelId("Settings")); provider.Complete("settings"); var secondHandle = await second;
                Assert.Equal(2, blocker.Depth, "two modals block input"); await secondHandle.CloseAsync(); Assert.Equal(1, blocker.Depth, "top close keeps lower modal");
                await firstHandle.CloseAsync(); Assert.Equal(0, blocker.Depth, "all modal input restored");
            }
        }

        private static async Task UiFailureCleanup()
        {
            var provider = new ControlledProvider(); var panel = new ProbePanel(); using (var manager = MakeUi(provider, panel))
            {
                var task = manager.OpenAsync(new UiPanelId("Settings")); provider.Fail("settings", new InvalidOperationException("load"));
                try { await task; throw new Exception("failure accepted"); } catch (InvalidOperationException) { }
                Assert.Equal(0, manager.OpenPanelCount, "failed load removed");
                var provider2 = new ControlledProvider(); var throwing = new ProbePanel { ThrowOnClose = true };
                using (var manager2 = MakeUi(provider2, throwing))
                { var open = manager2.OpenAsync(new UiPanelId("Settings")); provider2.Complete("settings"); var handle = await open; await handle.CloseAsync(); Assert.False(throwing.IsAlive, "callback error still disposed"); }
            }
        }

        private static Task UiFrameRefresh()
        {
            var provider = new ControlledProvider(); var panel = new ProbePanel(); using (var manager = MakeUi(provider, panel))
            using (var frame = new FrameUpdateManager(FrameUpdateConfig.Default))
            {
                var loop = frame.CreateLoop("Presentation"); using (var driver = frame.BindDriver(loop, "presentation", UpdatePhaseMask.Early | UpdatePhaseMask.Normal))
                {
                    manager.AttachToFrameUpdate(frame, loop); var open = manager.OpenAsync(new UiPanelId("Settings")); provider.Complete("settings"); open.GetAwaiter().GetResult();
                    frame.Tick(driver, UpdatePhase.EarlyUpdate, new FrameTimeSample(1, 0, 0));
                    frame.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, 0, 0));
                    Assert.Equal(1, panel.Updates, "dirty panel refreshed once");
                    frame.Tick(driver, UpdatePhase.EarlyUpdate, new FrameTimeSample(2, 0, 0));
                    frame.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(2, 0, 0)); Assert.Equal(1, panel.Updates, "clean panel not refreshed");
                    manager.MarkDirty(new UiPanelId("Settings"));
                    frame.Tick(driver, UpdatePhase.EarlyUpdate, new FrameTimeSample(3, 0, 0));
                    frame.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(3, 0, 0)); Assert.Equal(2, panel.Updates, "dirty refresh");
                }
            }
            return Task.CompletedTask;
        }

        private static async Task UiReentrantClose()
        {
            var provider = new ControlledProvider(); var blocker = new NullUiModalInputBlocker(); var panel = new ProbePanel();
            using (var manager = MakeUi(provider, panel, blocker))
            {
                panel.OpenedAction = () => manager.CloseAsync(new UiPanelId("Settings"));
                var open = manager.OpenAsync(new UiPanelId("Settings")); provider.Complete("settings");
                try { await open; throw new Exception("reentrant close should cancel open"); } catch (TaskCanceledException) { }
                Assert.Equal(0, manager.OpenPanelCount, "reentrant close removes entry"); Assert.Equal(0, manager.ModalDepth, "reentrant close removes modal token");
            }
        }

        private static async Task UiIndependentCancellation()
        {
            var provider = new ControlledProvider(); var panel = new ProbePanel(); using (var manager = MakeUi(provider, panel))
            using (var cts = new CancellationTokenSource())
            {
                var canceled = manager.OpenAsync(new UiPanelId("Settings"), cancellationToken: cts.Token);
                var valid = manager.OpenAsync(new UiPanelId("Settings")); cts.Cancel();
                try { await canceled; throw new Exception("first caller was not canceled"); } catch (TaskCanceledException) { }
                provider.Complete("settings"); var handle = await valid; Assert.True(handle.IsOpen, "second caller remains valid"); await handle.CloseAsync();
            }
        }

        private sealed class FakeResourceBackend : IResourceBackend
        {
            internal int LoadCalls; internal int ReleaseCalls; internal readonly TaskCompletionSource<ResourceBackendAsset> Pending = new TaskCompletionSource<ResourceBackendAsset>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<ResourceBackendAsset> LoadAssetAsync(string key, Type type, IProgress<float> progress, CancellationToken token)
            { LoadCalls++; return Pending.Task; }
            public Task<ResourceBackendInstance> InstantiateAsync(string key, object parent, bool worldPositionStays, IProgress<float> progress, CancellationToken token)
            { throw new NotSupportedException(); }
            public void Dispose() { }
            internal ResourceBackendAsset Complete(object value) { var asset = new ResourceBackendAsset(value, () => ReleaseCalls++); Pending.TrySetResult(asset); return asset; }
        }

        private static async Task ResourceLeaseOwnership()
        {
            var backend = new FakeResourceBackend(); using (var service = new ResourceService(backend))
            {
                var first = service.LoadAssetAsync<object>("shared"); var second = service.LoadAssetAsync<object>("shared"); backend.Complete(new object());
                var a = await first; var b = await second; Assert.Equal(1, backend.LoadCalls, "shared backend load"); a.Dispose(); Assert.Equal(0, backend.ReleaseCalls, "first lease does not release shared asset");
                a.Dispose(); b.Dispose(); Assert.Equal(1, backend.ReleaseCalls, "last lease releases once"); Assert.Equal(0, service.ActiveAssetEntryCount, "cache entry removed");
            }
        }

        private static Task ObjectPoolLifecycle()
        {
            int destroyed = 0; using (var pool = new ObjectPool<object>(() => new object(), destroy: _ => destroyed++, maxCapacity: 1))
            {
                object item = pool.Rent(); Assert.False(pool.TryReturn(new object()), "foreign return rejected"); Assert.True(pool.TryReturn(item), "owned return accepted");
                Assert.False(pool.TryReturn(item), "duplicate return rejected"); object leased = pool.Rent(); pool.Dispose(); Assert.Equal(1, destroyed, "leased item destroyed on close");
                Assert.True(leased != null, "lease remains a valid reference for caller cleanup");
            }
            return Task.CompletedTask;
        }

        private sealed class MemoryFileStore : ITextFileStore
        {
            internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
            public bool Exists(string path) => Files.ContainsKey(path);
            public string Read(string path) => Files.TryGetValue(path, out var value) ? value : throw new System.IO.FileNotFoundException(path);
            public void Write(string path, string contents) => Files[path] = contents;
            public void Delete(string path) => Files.Remove(path);
            public void Replace(string sourcePath, string destinationPath, string backupPath)
            { if (Files.TryGetValue(destinationPath, out var old)) Files[backupPath] = old; Files[destinationPath] = Files[sourcePath]; Files.Remove(sourcePath); }
        }

        private sealed class VersionedStringSerializer : IUserDataSerializer<string>
        {
            public string Serialize(string value, int version) => version + ":" + value;
            public string Deserialize(string text, out int version)
            { var split = text.IndexOf(':'); if (split <= 0) throw new InvalidOperationException("corrupt"); version = int.Parse(text.Substring(0, split)); return text.Substring(split + 1); }
            public string CreateDefault() => "default";
            public string Migrate(string value, int fromVersion, int currentVersion) => value + ":migrated";
        }

        private static async Task SaveLifecycle()
        {
            var files = new MemoryFileStore(); using (var save = new SaveService<string>(files, new VersionedStringSerializer(), 2))
            {
                await save.SaveAsync("slot", "first"); await save.SaveAsync("slot", "second");
                var loaded = await save.LoadAsync("slot"); Assert.Equal("second", loaded.Value, "latest save is readable");
                string path = System.IO.Path.GetFullPath("slot"); files.Files[path] = "bad"; files.Files[path + ".bak"] = "2:backup";
                loaded = await save.LoadAsync("slot"); Assert.True(loaded.UsedBackup && loaded.Value == "backup", "backup recovers corruption");
                files.Files.Remove(path); files.Files.Remove(path + ".bak"); loaded = await save.LoadAsync("slot"); Assert.True(loaded.UsedDefault && loaded.Value == "default", "missing save uses default");
                await Task.WhenAll(save.SaveAsync("same", "a"), save.SaveAsync("same", "b")); loaded = await save.LoadAsync("same"); Assert.True(loaded.Value == "a" || loaded.Value == "b", "concurrent save is serialized");
            }
        }
    }
}
