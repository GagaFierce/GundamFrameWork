using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.UI;

namespace WFrameWork.Modules.Tests
{
    internal static class UiMvvmTests
    {
        internal static Task PropertyNotificationUsesEquality()
        {
            var model = new ProbeViewModel(); int notifications = 0; string property = null;
            model.PropertyChanged += (sender, args) => { notifications++; property = args.PropertyName; };
            model.Value = 1; model.Value = 1; model.Value = 2;
            Assert.Equal(2, notifications, "equal values do not notify"); Assert.Equal(nameof(ProbeViewModel.Value), property, "property name is explicit");
            return Task.CompletedTask;
        }

        internal static Task TwoWayBindingAvoidsFeedbackLoop()
        {
            var model = new ProbeViewModel(); var view = new ProbeValueAdapter(); int writes = 0;
            using (UiBinding.TwoWay(model, nameof(ProbeViewModel.Value), () => model.Value, value => { writes++; model.Value = value; }, view))
            {
                model.Value = 3; Assert.Equal(3, view.Value, "model is pushed to view without a view event");
                view.Emit(8); Assert.Equal(8, model.Value, "view value reaches model"); Assert.Equal(1, writes, "view write happens once");
            }
            view.Emit(10); Assert.Equal(8, model.Value, "disposed binding removes view listener");
            return Task.CompletedTask;
        }

        internal static Task CommandsProtectCanExecuteAndDuplicateExecution()
        {
            bool allowed = false; int syncRuns = 0; var command = new UiCommand(() => syncRuns++, () => allowed);
            command.Execute(); Assert.Equal(0, syncRuns, "CanExecute blocks command"); allowed = true; command.Execute(); Assert.Equal(1, syncRuns, "allowed command runs");
            return Task.CompletedTask;
        }

        internal static async Task AsyncCommandCancellationAndDuplicateClick()
        {
            int calls = 0; var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = new AsyncUiCommand(async token =>
            {
                calls++; started.TrySetResult(true); await Task.Delay(Timeout.Infinite, token);
            });
            Task first = command.ExecuteAsync(); await started.Task; Task second = command.ExecuteAsync();
            Assert.True(ReferenceEquals(first, second), "duplicate click shares the current execution"); Assert.True(command.IsExecuting, "command exposes busy state");
            command.Cancel();
            try { await first; throw new Exception("canceled command completed successfully"); }
            catch (OperationCanceledException) { }
            Assert.Equal(1, calls, "cancellation does not start another execution"); Assert.False(command.IsExecuting, "command leaves busy state"); command.Dispose();
        }

        internal static async Task SettingsDraftApplyRollbackAndFailure()
        {
            var service = new FakeSettingsService(); var vm = new SettingsViewModel(service);
            vm.MasterVolume = .25f; Assert.True(vm.IsDirty, "editing creates a draft change"); Assert.Near(.25f, service.Previewed.MasterVolume, "edit is previewed");
            vm.CancelCommand.Execute(); Assert.False(vm.IsDirty, "cancel rolls draft back"); Assert.Near(1, service.Previewed.MasterVolume, "cancel restores preview");
            vm.MasterVolume = .5f; service.FailSave = true;
            try { await vm.ApplyCommand.ExecuteAsync(); throw new Exception("save failure was swallowed"); } catch (InvalidOperationException) { }
            Assert.True(vm.IsDirty, "failed save keeps draft"); Assert.True(!string.IsNullOrEmpty(vm.SaveError), "failed save is visible to the view");
            service.FailSave = false; await vm.ApplyCommand.ExecuteAsync(); Assert.False(vm.IsDirty, "successful apply commits draft"); Assert.Near(.5f, service.Current.MasterVolume, "saved value is current"); vm.Dispose();
        }

        internal static Task ClosedBindingStopsLateUpdates()
        {
            var model = new ProbeViewModel(); string text = null; var binding = UiBinding.OneWay(model, nameof(ProbeViewModel.Text), () => model.Text, value => text = value);
            Assert.Equal("", text, "binding synchronizes immediately"); model.Text = "open"; Assert.Equal("open", text, "open view updates"); binding.Dispose(); model.Text = "late"; Assert.Equal("open", text, "closed view ignores late notification");
            return Task.CompletedTask;
        }

        internal static Task DialogCompletesOnceAndToastIsBounded()
        {
            var dialog = new DialogViewModel(new UiDialogRequest("title", "message")); Assert.True(dialog.TryComplete(UiDialogResult.Confirmed), "first result wins"); Assert.False(dialog.TryComplete(UiDialogResult.Canceled), "second result is ignored"); Assert.Equal(UiDialogResult.Confirmed, dialog.Result, "dialog result is stable");
            var toast = new ToastViewModel(2); toast.Enqueue(new UiToastMessage(UiToastKind.Info, "a")); toast.Enqueue(new UiToastMessage(UiToastKind.Info, "b")); toast.Enqueue(new UiToastMessage(UiToastKind.Info, "c")); Assert.Equal(2, toast.Queue.Count, "toast queue is bounded");
            return Task.CompletedTask;
        }

        private sealed class ProbeViewModel : ViewModelBase
        {
            private int _value; private string _text = string.Empty;
            public int Value { get => _value; set => SetProperty(ref _value, value); }
            public string Text { get => _text; set => SetProperty(ref _text, value); }
        }

        private sealed class ProbeValueAdapter : IUiValueAdapter<int>
        {
            public int Value { get; private set; }
            public event Action<int> ValueChanged;
            public void SetValueWithoutNotify(int value) { Value = value; }
            internal void Emit(int value) { Value = value; ValueChanged?.Invoke(value); }
        }

        private sealed class FakeSettingsService : IUiSettingsService
        {
            private UiSettingsSnapshot _current = UiSettingsSnapshot.Default;
            internal UiSettingsSnapshot Previewed { get; private set; } = UiSettingsSnapshot.Default;
            internal bool FailSave;
            public UiSettingsSnapshot Current => _current;
            public void Preview(UiSettingsSnapshot settings) { Previewed = settings; }
            public Task SaveAsync(UiSettingsSnapshot settings, CancellationToken token)
            {
                if (FailSave) return Task.FromException(new InvalidOperationException("save failed"));
                _current = settings; Previewed = settings; return Task.CompletedTask;
            }
        }
    }
}
