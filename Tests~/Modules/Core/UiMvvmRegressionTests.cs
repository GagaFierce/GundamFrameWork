using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.UI;

namespace WFrameWork.Modules.Tests
{
    internal static class UiMvvmRegressionTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);
        private static TaskCompletionSource<T> Signal<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static Task BindingNotificationsUseOwnerAndCommandsPublishTask()
        {
            var previous = SynchronizationContext.Current;
            using (var pump = new PumpContext())
            {
                SynchronizationContext.SetSynchronizationContext(pump);
                try
                {
                    var gate = Signal<bool>();
                    AsyncUiCommand command = null; Task nested = null;
                    command = new AsyncUiCommand(token => { nested = command.ExecuteAsync(); return gate.Task; });
                    var target = new ButtonProbe();
                    using (UiBinding.Command(command, target))
                    {
                        Task execution = command.ExecuteAsync();
                        Assert.True(ReferenceEquals(nested, execution), "reentrant command receives published task");
                        Task.Run(() => gate.TrySetResult(true));
                        pump.Complete(execution);
                        Assert.Equal(0, target.WrongThreadWrites, "command completion writes UI only on owner thread");
                        Assert.False(command.IsExecuting, "command exits busy state");
                    }
                    command.Dispose();
                    var model = new Model(); int writes = 0;
                    var binding = UiBinding.OneWay(model, nameof(model.Value), () => model.Value, value => writes++);
                    Task.Run(() => model.Value = 2).GetAwaiter().GetResult();
                    binding.Dispose(); pump.Drain();
                    Assert.Equal(1, writes, "queued notifications do not write to a closed binding");
                    model.Dispose();
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
            return Task.CompletedTask;
        }

        internal static Task CleanupContinuesAfterExceptions()
        {
            var vm = new Model();
            using (vm.LifetimeToken.Register(() => throw new InvalidOperationException("cancel callback")))
            {
                try { vm.Dispose(); throw new Exception("cleanup must report cancellation failure"); }
                catch (AggregateException) { }
            }
            Assert.Equal(1, vm.CleanupCalls, "VM cleanup still runs");
            int disposed = 0;
            var bindings = new UiBindingSet();
            bindings.Add(new Disposable(() => disposed++));
            bindings.Add(new Disposable(() => throw new InvalidOperationException("binding")));
            try { bindings.Dispose(); throw new Exception("binding failure must be reported"); }
            catch (AggregateException) { }
            bindings.Dispose();
            Assert.Equal(1, disposed, "other bindings dispose exactly once");
            return Task.CompletedTask;
        }

        internal static Task FailedBindingCreationRollsBack()
        {
            var vm = new Model(); int writes = 0;
            try { UiBinding.OneWay(vm, nameof(vm.Value), () => vm.Value, value => { writes++; throw new InvalidOperationException(); }); }
            catch (InvalidOperationException) { }
            vm.Value = 1;
            Assert.Equal(1, writes, "failed initial write leaves no subscription");
            var adapter = new ThrowingValueAdapter();
            try { UiBinding.TwoWay(vm, nameof(vm.Value), () => vm.Value, value => vm.Value = value, adapter); }
            catch (InvalidOperationException) { }
            Assert.True(adapter.Disposed, "partially created target adapter is disposed");
            vm.Dispose();
            return Task.CompletedTask;
        }

        internal static async Task SettingsSaveKeepsNewDraftAndRestoresPreview()
        {
            var settings = new SettingsService(); var vm = new SettingsViewModel(settings);
            vm.MasterVolume = .4f;
            Task save = vm.ApplyCommand.ExecuteAsync();
            vm.MasterVolume = .7f;
            settings.SaveGate.TrySetResult(true);
            await save.WaitAsync(Timeout);
            Assert.Near(.4f, settings.Current.MasterVolume, "commit saves the captured snapshot");
            Assert.True(vm.IsDirty && vm.ApplyCommand.CanExecute, "later edits remain applicable");
            Assert.Near(.7f, settings.Previewed.MasterVolume, "later draft remains the active preview");
            vm.Dispose();
            Assert.Near(.4f, settings.Previewed.MasterVolume, "forced close rolls preview back to committed settings");
            vm.MasterVolume = .1f;
            Assert.Near(.4f, settings.Previewed.MasterVolume, "disposed VM cannot preview late values");
        }

        internal static async Task SettingsCloseGuardHandlesAllChoices()
        {
            var settings = new SettingsService(); var dialog = new Dialogs();
            var vm = new SettingsViewModel(settings, dialogs: dialog);
            vm.MasterVolume = .2f;
            dialog.Next = UiDialogResult.Alternative;
            Assert.False(await vm.CanCloseAsync(CancellationToken.None), "continue editing keeps the panel open");
            dialog.Next = UiDialogResult.Closed;
            Assert.False(await vm.CanCloseAsync(CancellationToken.None), "Escape from confirmation does not discard edits");
            dialog.Next = UiDialogResult.Canceled;
            Assert.True(await vm.CanCloseAsync(CancellationToken.None), "explicit discard allows close");
            Assert.Near(1, settings.Previewed.MasterVolume, "discard restores committed preview");
            vm.MasterVolume = .6f; dialog.Next = UiDialogResult.Confirmed;
            settings.SaveGate.TrySetResult(true);
            Assert.True(await vm.CanCloseAsync(CancellationToken.None), "save choice commits then closes");
            Assert.Near(.6f, settings.Current.MasterVolume, "close guard persisted the draft");
            vm.Dispose();
        }

        internal static async Task LoadingRetrySharesCancellationAndConcurrency()
        {
            int calls = 0; var started = Signal<bool>();
            using (var vm = new LoadingViewModel(async (progress, token) =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("first attempt");
                started.TrySetResult(true); await Task.Delay(System.Threading.Timeout.Infinite, token);
            }))
            {
                try { await vm.StartCommand.ExecuteAsync(); } catch (InvalidOperationException) { }
                Assert.True(vm.CanRetry, "failure enables retry");
                Task retry = vm.RetryCommand.ExecuteAsync(); await started.Task.WaitAsync(Timeout);
                Assert.False(vm.StartCommand.CanExecute, "retry blocks a second start");
                Assert.True(ReferenceEquals(retry, vm.StartCommand.ExecuteAsync()), "duplicate start shares retry operation");
                Assert.True(vm.CancelCommand.CanExecute, "retry remains cancelable");
                vm.CancelCommand.Execute();
                try { await retry.WaitAsync(Timeout); throw new Exception("retry ignored cancellation"); }
                catch (OperationCanceledException) { }
                Assert.Equal(2, calls, "only one retry ran");
            }
        }

        internal static async Task ResultCapabilitiesAndToastTiming()
        {
            var data = new UiResultData("result", "", null, true);
            using (var unsupported = new ResultViewModel(data, new Flow()))
                Assert.False(unsupported.CanNext || unsupported.NextCommand.CanExecute, "unsupported next-level action is hidden");
            var next = new NextFlow();
            using (var supported = new ResultViewModel(data, next)) await supported.NextCommand.ExecuteAsync();
            Assert.Equal(1, next.Calls, "next command invokes its real service");
            using (var toast = new ToastViewModel(2))
            {
                toast.Enqueue(new UiToastMessage(UiToastKind.Info, "first", 1));
                toast.Enqueue(new UiToastMessage(UiToastKind.Info, "second", 1));
                toast.Tick(.5f); Assert.Equal("first", toast.Current.Message, "toast remains for its duration");
                toast.Tick(.6f); Assert.Equal("second", toast.Current.Message, "unscaled tick advances queue");
                toast.Tick(1); Assert.True(toast.Current == null, "last toast expires");
            }
        }

        internal static async Task PanelRequestCloseHonorsGuard()
        {
            var resource = new InMemoryUiResourceProvider(); resource.Add("guard", new object());
            var panel = new GuardedPanel();
            using (var manager = new UiPanelManager(resource, new DelegateUiPanelFactory((d, r) => panel)))
            {
                var id = new UiPanelId("guard"); manager.Register(new UiPanelDefinition(id, "guard", UiPanelLayer.Modal, true));
                var handle = await manager.OpenAsync(id);
                await manager.RequestCloseTopModalAsync();
                Assert.True(handle.IsOpen, "guard can veto ordinary navigation");
                panel.Allow = true; await manager.RequestCloseTopModalAsync();
                Assert.False(handle.IsOpen || panel.IsAlive, "accepted guard completes close");
            }
        }

        internal static async Task PanelGenerationsCloseRepeatedly()
        {
            for (int iteration = 0; iteration < 20; iteration++)
            {
                var resources = new InMemoryUiResourceProvider(); resources.Add("panel", new object());
                var panels = new System.Collections.Generic.List<DelayedPanel>();
                var manager = new UiPanelManager(resources, new DelegateUiPanelFactory((d, r) =>
                { var panel = new DelayedPanel(); panels.Add(panel); return panel; }));
                var id = new UiPanelId("panel"); manager.Register(new UiPanelDefinition(id, "panel"));
                Task firstClose = Task.CompletedTask;
                try
                {
                    var first = await manager.OpenAsync(id); firstClose = first.CloseAsync();
                    await manager.OpenAsync(id);
                    var all = manager.CloseAllAsync();
                    foreach (var panel in panels) panel.Done.TrySetResult(true);
                    await Task.WhenAll(firstClose, all).WaitAsync(Timeout);
                    Assert.Equal(0, manager.OpenPanelCount, "parallel generation destruction returns to baseline");
                }
                finally
                {
                    foreach (var panel in panels) panel.Done.TrySetResult(true);
                    await manager.CloseAllAsync().WaitAsync(Timeout);
                }
            }
        }

        private sealed class PumpContext : SynchronizationContext, IDisposable
        {
            private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
            public override void Post(SendOrPostCallback callback, object state) => _work.Add(() => callback(state));
            internal void Complete(Task task)
            {
                DateTime deadline = DateTime.UtcNow + Timeout;
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                    if (_work.TryTake(out var action, 10)) action();
                Assert.True(task.IsCompleted, "owner-thread operation timed out"); task.GetAwaiter().GetResult(); Drain();
            }
            internal void Drain() { while (_work.TryTake(out var action)) action(); }
            public void Dispose() { _work.Dispose(); }
        }
        private sealed class Model : ViewModelBase
        {
            private int _value;
            public int Value { get => _value; set => SetProperty(ref _value, value); }
            internal int CleanupCalls;
            protected override void OnDispose() { CleanupCalls++; }
        }
        private sealed class ButtonProbe : IUiCommandAdapter
        {
            private readonly int _owner = Thread.CurrentThread.ManagedThreadId;
            internal int WrongThreadWrites;
            public event Action Clicked { add { } remove { } }
            public void SetInteractable(bool value) { if (Thread.CurrentThread.ManagedThreadId != _owner) WrongThreadWrites++; }
            public void SetBusy(bool value) { if (Thread.CurrentThread.ManagedThreadId != _owner) WrongThreadWrites++; }
            public void Dispose() { }
        }
        private sealed class Disposable : IDisposable
        { private readonly Action _action; internal Disposable(Action action) { _action = action; } public void Dispose() => _action(); }
        private sealed class ThrowingValueAdapter : IUiValueAdapter<int>, IDisposable
        {
            internal bool Disposed;
            public int Value => 0;
            public event Action<int> ValueChanged { add { } remove { } }
            public void SetValueWithoutNotify(int value) => throw new InvalidOperationException("initial control write");
            public void Dispose() { Disposed = true; }
        }
        private sealed class SettingsService : IUiSettingsService
        {
            internal readonly TaskCompletionSource<bool> SaveGate = Signal<bool>();
            public UiSettingsSnapshot Current { get; private set; } = UiSettingsSnapshot.Default;
            internal UiSettingsSnapshot Previewed = UiSettingsSnapshot.Default;
            public void Preview(UiSettingsSnapshot value) { Previewed = value; }
            public async Task SaveAsync(UiSettingsSnapshot value, CancellationToken token) { await SaveGate.Task; Current = value; }
        }
        private sealed class Dialogs : IUiDialogService
        { internal UiDialogResult Next; public Task<UiDialogResult> ShowAsync(UiDialogRequest request, CancellationToken token) => Task.FromResult(Next); }
        private class Flow : IUiGameFlowService
        {
            public Task StartGameAsync(CancellationToken token) => Task.CompletedTask;
            public Task RestartGameAsync(CancellationToken token) => Task.CompletedTask;
            public Task ReturnToLobbyAsync(CancellationToken token) => Task.CompletedTask;
        }
        private sealed class NextFlow : Flow, IUiNextLevelService
        { internal int Calls; public Task NextLevelAsync(CancellationToken token) { Calls++; return Task.CompletedTask; } }
        private sealed class GuardedPanel : IUiPanelInstance, IUiCloseRequest
        {
            internal bool Allow;
            public bool IsAlive { get; private set; } = true;
            public bool RequiresContinuousUpdate => false;
            public Task<bool> CanCloseAsync(CancellationToken token) => Task.FromResult(Allow);
            public void SetVisible(bool value) { } public void OnOpened(object argument) { } public void OnShown() { }
            public void OnHidden() { } public void OnClosed() { } public void OnUpdate(in UiPanelUpdateContext context) { }
            public void Dispose() { IsAlive = false; }
        }
        private sealed class DelayedPanel : IUiPanelInstance, IUiAsyncPanelInstance
        {
            internal readonly TaskCompletionSource<bool> Done = Signal<bool>();
            public bool IsAlive => !Done.Task.IsCompleted;
            public bool RequiresContinuousUpdate => false;
            public Task DisposeAsync() => Done.Task;
            public void Dispose() { Done.TrySetResult(true); }
            public void SetVisible(bool value) { } public void OnOpened(object argument) { } public void OnShown() { }
            public void OnHidden() { } public void OnClosed() { } public void OnUpdate(in UiPanelUpdateContext context) { }
        }
    }
}
