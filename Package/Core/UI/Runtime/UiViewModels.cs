using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WFrameWork.UI
{
    public enum UiNavigationSource { Lobby, Pause, Loading, Result, Dialog }

    public interface IUiNavigationService
    {
        Task OpenSettingsAsync(UiNavigationSource source, CancellationToken token);
        Task OpenHelpAsync(UiNavigationSource source, CancellationToken token);
        Task OpenAboutAsync(UiNavigationSource source, CancellationToken token);
        Task BackAsync(CancellationToken token);
    }

    public interface IUiExitService
    {
        Task RequestExitAsync(CancellationToken token);
    }

    public interface IUiDialogService
    {
        Task<UiDialogResult> ShowAsync(UiDialogRequest request, CancellationToken token);
    }

    public interface IUiGameFlowService
    {
        Task StartGameAsync(CancellationToken token);
        Task RestartGameAsync(CancellationToken token);
        Task ReturnToLobbyAsync(CancellationToken token);
    }

    public interface IUiPauseService
    {
        IDisposable AcquirePause(string owner);
    }

    public interface IUiCloseRequest
    {
        Task<bool> CanCloseAsync(CancellationToken token);
    }

    public interface IUiDisplaySettingsService
    {
        IReadOnlyList<string> WindowModes { get; }
        IReadOnlyList<string> Resolutions { get; }
        IReadOnlyList<string> QualityLevels { get; }
    }

    public interface IUiNextLevelService
    {
        Task NextLevelAsync(CancellationToken token);
    }

    public sealed class UiSettingsSnapshot : IEquatable<UiSettingsSnapshot>
    {
        public float MasterVolume { get; }
        public float MusicVolume { get; }
        public float SfxVolume { get; }
        public float UiVolume { get; }
        public bool Muted { get; }
        public int WindowMode { get; }
        public int ResolutionIndex { get; }
        public int QualityIndex { get; }

        public UiSettingsSnapshot(float masterVolume, float musicVolume, float sfxVolume, float uiVolume,
            bool muted, int windowMode = 0, int resolutionIndex = 0, int qualityIndex = 0)
        {
            MasterVolume = Clamp01(masterVolume); MusicVolume = Clamp01(musicVolume);
            SfxVolume = Clamp01(sfxVolume); UiVolume = Clamp01(uiVolume); Muted = muted;
            WindowMode = windowMode; ResolutionIndex = resolutionIndex; QualityIndex = qualityIndex;
        }

        public static UiSettingsSnapshot Default => new UiSettingsSnapshot(1, 1, 1, 1, false);
        public bool Equals(UiSettingsSnapshot other)
        {
            if (ReferenceEquals(other, null)) return false;
            return MasterVolume.Equals(other.MasterVolume) && MusicVolume.Equals(other.MusicVolume) &&
                SfxVolume.Equals(other.SfxVolume) && UiVolume.Equals(other.UiVolume) && Muted == other.Muted &&
                WindowMode == other.WindowMode && ResolutionIndex == other.ResolutionIndex && QualityIndex == other.QualityIndex;
        }
        public override bool Equals(object obj) => Equals(obj as UiSettingsSnapshot);
        public override int GetHashCode() => MasterVolume.GetHashCode() ^ MusicVolume.GetHashCode() ^ SfxVolume.GetHashCode() ^ UiVolume.GetHashCode();
        private static float Clamp01(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value));
    }

    public interface IUiSettingsService
    {
        UiSettingsSnapshot Current { get; }
        void Preview(UiSettingsSnapshot settings);
        Task SaveAsync(UiSettingsSnapshot settings, CancellationToken token);
    }

    public sealed class UiSettingsDraft
    {
        public float MasterVolume;
        public float MusicVolume;
        public float SfxVolume;
        public float UiVolume;
        public bool Muted;
        public int WindowMode;
        public int ResolutionIndex;
        public int QualityIndex;

        public UiSettingsDraft(UiSettingsSnapshot value) { CopyFrom(value); }
        public void CopyFrom(UiSettingsSnapshot value)
        {
            MasterVolume = value.MasterVolume; MusicVolume = value.MusicVolume; SfxVolume = value.SfxVolume;
            UiVolume = value.UiVolume; Muted = value.Muted; WindowMode = value.WindowMode;
            ResolutionIndex = value.ResolutionIndex; QualityIndex = value.QualityIndex;
        }
        public UiSettingsSnapshot ToSnapshot() => new UiSettingsSnapshot(MasterVolume, MusicVolume, SfxVolume, UiVolume,
            Muted, WindowMode, ResolutionIndex, QualityIndex);
    }

    public sealed class LobbyViewModel : ViewModelBase
    {
        private readonly IUiGameFlowService _flow;
        private readonly IUiNavigationService _navigation;
        private readonly IUiExitService _exit;
        private readonly IUiDialogService _dialogs;
        private bool _isReady;
        private string _status;
        private string _error;

        public LobbyViewModel(IUiGameFlowService flow, IUiNavigationService navigation, IUiExitService exit,
            bool isReady = true, IUiDialogService dialogs = null)
        {
            _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _exit = exit ?? throw new ArgumentNullException(nameof(exit));
            _dialogs = dialogs;
            _isReady = isReady;
            StartCommand = new AsyncUiCommand(StartCoreAsync, () => IsReady);
            StartCommand.StateChanged += OnStartStateChanged;
            SettingsCommand = new AsyncUiCommand(_ => NavigateAsync(_navigation.OpenSettingsAsync));
            HelpCommand = new AsyncUiCommand(_ => NavigateAsync(_navigation.OpenHelpAsync));
            AboutCommand = new AsyncUiCommand(_ => NavigateAsync(_navigation.OpenAboutAsync));
            ExitCommand = new AsyncUiCommand(ExitCoreAsync);
        }

        public bool IsReady { get => _isReady; set { if (SetProperty(ref _isReady, value)) StartCommand.NotifyCanExecuteChanged(); } }
        public bool IsBusy => StartCommand.IsExecuting;
        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public string Error { get => _error; private set => SetProperty(ref _error, value); }
        public AsyncUiCommand StartCommand { get; }
        public AsyncUiCommand SettingsCommand { get; }
        public AsyncUiCommand HelpCommand { get; }
        public AsyncUiCommand AboutCommand { get; }
        public AsyncUiCommand ExitCommand { get; }

        private async Task StartCoreAsync(CancellationToken token)
        {
            Error = null; Status = "Loading";
            try { await _flow.StartGameAsync(token); Status = "Started"; }
            catch (OperationCanceledException) { Status = "Canceled"; throw; }
            catch (Exception error) { Error = error.Message; Status = "Failed"; throw; }
        }

        private Task NavigateAsync(Func<UiNavigationSource, CancellationToken, Task> open)
        { return open(UiNavigationSource.Lobby, LifetimeToken); }
        private void OnStartStateChanged(object sender, EventArgs args) => RaisePropertyChanged(nameof(IsBusy));

        private async Task ExitCoreAsync(CancellationToken token)
        {
            if (_dialogs != null)
            {
                UiDialogResult result = await _dialogs.ShowAsync(new UiDialogRequest("退出游戏", "确定要退出吗？"), token);
                if (result != UiDialogResult.Confirmed) return;
            }
            await _exit.RequestExitAsync(token);
        }

        protected override void OnDispose()
        {
            StartCommand.StateChanged -= OnStartStateChanged;
            UiCleanup.All(StartCommand.Dispose, SettingsCommand.Dispose, HelpCommand.Dispose, AboutCommand.Dispose, ExitCommand.Dispose);
        }
    }

    public sealed class SettingsViewModel : ViewModelBase, IUiCloseRequest
    {
        private readonly IUiSettingsService _settings;
        private UiSettingsSnapshot _applied;
        private UiSettingsDraft _draft;
        private bool _isDirty;
        private string _saveError;
        private readonly IUiNavigationService _navigation;
        private readonly IUiDialogService _dialogs;
        private Task<bool> _closeRequest;

        public SettingsViewModel(IUiSettingsService settings, IUiNavigationService navigation = null, IUiDialogService dialogs = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _navigation = navigation;
            _dialogs = dialogs;
            _applied = settings.Current ?? UiSettingsSnapshot.Default;
            _draft = new UiSettingsDraft(_applied);
            ApplyCommand = new AsyncUiCommand(ApplyCoreAsync, () => IsDirty);
            CancelCommand = new UiCommand(Cancel, () => !ApplyCommand.IsExecuting);
            DefaultsCommand = new UiCommand(ResetDefaults, () => !ApplyCommand.IsExecuting);
            DiscardAndCloseCommand = new AsyncUiCommand(DiscardAndCloseCoreAsync, () => _navigation != null && !ApplyCommand.IsExecuting);
            ApplyCommand.StateChanged += OnApplyCommandStateChanged;
        }

        public UiSettingsDraft Draft => _draft;
        public IReadOnlyList<string> WindowModes => (_settings as IUiDisplaySettingsService)?.WindowModes ?? Array.Empty<string>();
        public IReadOnlyList<string> Resolutions => (_settings as IUiDisplaySettingsService)?.Resolutions ?? Array.Empty<string>();
        public IReadOnlyList<string> QualityLevels => (_settings as IUiDisplaySettingsService)?.QualityLevels ?? Array.Empty<string>();
        public float MasterVolume { get => _draft.MasterVolume; set => SetDraftFloat(ref _draft.MasterVolume, value, nameof(MasterVolume)); }
        public float MusicVolume { get => _draft.MusicVolume; set => SetDraftFloat(ref _draft.MusicVolume, value, nameof(MusicVolume)); }
        public float SfxVolume { get => _draft.SfxVolume; set => SetDraftFloat(ref _draft.SfxVolume, value, nameof(SfxVolume)); }
        public float UiVolume { get => _draft.UiVolume; set => SetDraftFloat(ref _draft.UiVolume, value, nameof(UiVolume)); }
        public bool Muted { get => _draft.Muted; set => SetDraft(ref _draft.Muted, value, nameof(Muted)); }
        public int WindowMode { get => _draft.WindowMode; set => SetDraft(ref _draft.WindowMode, value, nameof(WindowMode)); }
        public int ResolutionIndex { get => _draft.ResolutionIndex; set => SetDraft(ref _draft.ResolutionIndex, value, nameof(ResolutionIndex)); }
        public int QualityIndex { get => _draft.QualityIndex; set => SetDraft(ref _draft.QualityIndex, value, nameof(QualityIndex)); }
        public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) ApplyCommand.NotifyCanExecuteChanged(); } }
        public string SaveError { get => _saveError; private set => SetProperty(ref _saveError, value); }
        public UiSettingsSnapshot AppliedSettings => _applied;
        public AsyncUiCommand ApplyCommand { get; }
        public UiCommand CancelCommand { get; }
        public UiCommand DefaultsCommand { get; }
        public AsyncUiCommand DiscardAndCloseCommand { get; }

        private void SetDraft<T>(ref T field, T value, string propertyName)
        {
            if (IsDisposed) return;
            VerifyAccess();
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value; RaisePropertyChanged(propertyName); PreviewDraft();
        }

        private void SetDraftFloat(ref float field, float value, string propertyName)
        { SetDraft(ref field, float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value)), propertyName); }

        private void PreviewDraft()
        {
            try { _settings.Preview(_draft.ToSnapshot()); SaveError = null; }
            catch (Exception error) { SaveError = error.Message; }
            bool dirty = !_applied.Equals(_draft.ToSnapshot());
            IsDirty = dirty;
        }

        private async Task ApplyCoreAsync(CancellationToken token)
        {
            SaveError = null;
            UiSettingsSnapshot next = _draft.ToSnapshot();
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, token))
                    await _settings.SaveAsync(next, linked.Token);
                await OnUiAsync(() =>
                {
                    if (IsDisposed) return;
                    _applied = next;
                    RaisePropertyChanged(nameof(AppliedSettings));
                    IsDirty = !_applied.Equals(_draft.ToSnapshot());
                    if (IsDirty) _settings.Preview(_draft.ToSnapshot());
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { SaveError = error.Message; throw; }
        }

        private void Cancel()
        {
            _draft.CopyFrom(_applied);
            _settings.Preview(_applied);
            SaveError = null;
            RaiseAllDraftProperties();
            IsDirty = false;
        }

        private void ResetDefaults()
        {
            _draft.CopyFrom(UiSettingsSnapshot.Default);
            RaiseAllDraftProperties();
            PreviewDraft();
        }

        private async Task DiscardAndCloseCoreAsync(CancellationToken token)
        {
            Cancel();
            await _navigation.BackAsync(token);
        }

        private void OnApplyCommandStateChanged(object sender, EventArgs args)
        {
            CancelCommand.NotifyCanExecuteChanged(); DefaultsCommand.NotifyCanExecuteChanged(); DiscardAndCloseCommand.NotifyCanExecuteChanged();
        }

        private void RaiseAllDraftProperties()
        {
            RaisePropertyChanged(nameof(MasterVolume)); RaisePropertyChanged(nameof(MusicVolume)); RaisePropertyChanged(nameof(SfxVolume));
            RaisePropertyChanged(nameof(UiVolume)); RaisePropertyChanged(nameof(Muted)); RaisePropertyChanged(nameof(WindowMode));
            RaisePropertyChanged(nameof(ResolutionIndex)); RaisePropertyChanged(nameof(QualityIndex));
        }

        public Task<bool> CanCloseAsync(CancellationToken token)
        {
            if (_closeRequest != null && !_closeRequest.IsCompleted) return _closeRequest;
            _closeRequest = RequestCloseCoreAsync(token);
            return _closeRequest;
        }

        private async Task<bool> RequestCloseCoreAsync(CancellationToken token)
        {
            if (ApplyCommand.IsExecuting)
            {
                try { await ApplyCommand.ExecuteAsync(); } catch { return false; }
            }
            if (!IsDirty) return true;
            if (_dialogs == null) return false;
            var result = await _dialogs.ShowAsync(new UiDialogRequest("未保存的设置", "如何处理当前修改？", "保存", "放弃", "继续编辑"), token);
            if (result == UiDialogResult.Canceled) { Cancel(); return true; }
            if (result != UiDialogResult.Confirmed) return false;
            try { await ApplyCommand.ExecuteAsync(); return !IsDirty; } catch { return false; }
        }

        protected override void OnDispose()
        {
            ApplyCommand.StateChanged -= OnApplyCommandStateChanged;
            UiCleanup.All(ApplyCommand.Dispose, CancelCommand.Dispose, DefaultsCommand.Dispose,
                DiscardAndCloseCommand.Dispose, () => _settings.Preview(_settings.Current ?? _applied));
        }
    }

    public sealed class PauseViewModel : ViewModelBase
    {
        private readonly IUiGameFlowService _flow;
        private readonly IUiNavigationService _navigation;
        private readonly IUiDialogService _dialogs;
        private IDisposable _pauseToken;
        private string _error;

        public PauseViewModel(IUiGameFlowService flow, IUiNavigationService navigation, IUiPauseService pause, IUiDialogService dialogs = null)
        {
            _flow = flow ?? throw new ArgumentNullException(nameof(flow)); _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _dialogs = dialogs;
            if (pause == null) throw new ArgumentNullException(nameof(pause));
            _pauseToken = pause.AcquirePause("PauseMenu");
            ContinueCommand = new AsyncUiCommand(ContinueCoreAsync);
            SettingsCommand = new AsyncUiCommand(_ => _navigation.OpenSettingsAsync(UiNavigationSource.Pause, LifetimeToken));
            HelpCommand = new AsyncUiCommand(_ => _navigation.OpenHelpAsync(UiNavigationSource.Pause, LifetimeToken));
            RestartCommand = new AsyncUiCommand(RestartCoreAsync);
            ReturnToLobbyCommand = new AsyncUiCommand(ReturnCoreAsync);
        }

        public string Error { get => _error; private set => SetProperty(ref _error, value); }
        public AsyncUiCommand ContinueCommand { get; }
        public AsyncUiCommand SettingsCommand { get; }
        public AsyncUiCommand HelpCommand { get; }
        public AsyncUiCommand RestartCommand { get; }
        public AsyncUiCommand ReturnToLobbyCommand { get; }

        private async Task ContinueCoreAsync(CancellationToken token)
        { await _navigation.BackAsync(token); ReleasePause(); }
        private async Task RestartCoreAsync(CancellationToken token)
        { try { if (await ConfirmAsync("重新开始", token)) await _flow.RestartGameAsync(token); } catch (Exception error) { Error = error.Message; throw; } }
        private async Task ReturnCoreAsync(CancellationToken token)
        { try { if (await ConfirmAsync("返回大厅", token)) { await _flow.ReturnToLobbyAsync(token); ReleasePause(); } } catch (Exception error) { Error = error.Message; throw; } }
        private async Task<bool> ConfirmAsync(string title, CancellationToken token) => _dialogs == null ||
            await _dialogs.ShowAsync(new UiDialogRequest(title, "确定要" + title + "吗？"), token) == UiDialogResult.Confirmed;
        private void ReleasePause() { var token = Interlocked.Exchange(ref _pauseToken, null); token?.Dispose(); }

        protected override void OnDispose()
        {
            UiCleanup.All(ReleasePause, ContinueCommand.Dispose, SettingsCommand.Dispose, HelpCommand.Dispose,
                RestartCommand.Dispose, ReturnToLobbyCommand.Dispose);
        }
    }

    public readonly struct UiLoadingProgress
    {
        public string Stage { get; }
        public float Value { get; }
        public bool HasValue { get; }
        public UiLoadingProgress(string stage, float value, bool hasValue = true) { Stage = stage; Value = value; HasValue = hasValue; }
    }

    public sealed class LoadingViewModel : ViewModelBase, IUiCloseRequest
    {
        private readonly Func<IProgress<UiLoadingProgress>, CancellationToken, Task> _operation;
        private string _stage = "Preparing";
        private float _progress;
        private bool _hasProgress;
        private bool _isCanceling;
        private string _error;

        public LoadingViewModel(Func<IProgress<UiLoadingProgress>, CancellationToken, Task> operation, Func<Task> close = null)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            StartCommand = new AsyncUiCommand(RunCoreAsync, () => !IsCanceling);
            CancelCommand = new UiCommand(() => { IsCanceling = true; StartCommand.Cancel(); }, () => StartCommand.IsExecuting && !IsCanceling);
            RetryCommand = StartCommand; // Both buttons target the same in-flight operation.
            BackCommand = new AsyncUiCommand(async token => { await CanCloseAsync(token); if (close != null) await close(); }, () => close != null);
            StartCommand.StateChanged += OnStartCommandStateChanged;
        }

        public string Stage { get => _stage; private set => SetProperty(ref _stage, value); }
        public float Progress { get => _progress; private set => SetProperty(ref _progress, value); }
        public bool HasProgress { get => _hasProgress; private set => SetProperty(ref _hasProgress, value); }
        public bool IsCanceling { get => _isCanceling; private set { if (SetProperty(ref _isCanceling, value)) { StartCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } } }
        public string Error { get => _error; private set { if (SetProperty(ref _error, value)) RaisePropertyChanged(nameof(CanRetry)); } }
        public bool CanRetry => !string.IsNullOrEmpty(Error) && !StartCommand.IsExecuting && !IsCanceling;
        public AsyncUiCommand StartCommand { get; }
        public UiCommand CancelCommand { get; }
        public AsyncUiCommand RetryCommand { get; }
        public AsyncUiCommand BackCommand { get; }

        private async Task RunCoreAsync(CancellationToken token)
        {
            Error = null; IsCanceling = false;
            var progress = new Progress<UiLoadingProgress>(value => { Stage = value.Stage; HasProgress = value.HasValue; Progress = value.HasValue ? value.Value : 0; });
            try
            {
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(LifetimeToken, token))
                    await _operation(progress, linked.Token);
                Stage = "Complete";
            }
            catch (OperationCanceledException) { Stage = "Canceled"; throw; }
            catch (Exception error) { Error = error.Message; Stage = "Failed"; throw; }
            finally { IsCanceling = false; RaisePropertyChanged(nameof(CanRetry)); }
        }

        private void OnStartCommandStateChanged(object sender, EventArgs args)
        { CancelCommand.NotifyCanExecuteChanged(); RaisePropertyChanged(nameof(CanRetry)); }

        public async Task<bool> CanCloseAsync(CancellationToken token)
        {
            if (StartCommand.IsExecuting)
            {
                StartCommand.Cancel();
                try { await StartCommand.Execution; } catch (OperationCanceledException) { } catch { }
            }
            return true;
        }

        protected override void OnDispose() { StartCommand.StateChanged -= OnStartCommandStateChanged; UiCleanup.All(StartCommand.Dispose, CancelCommand.Dispose, BackCommand.Dispose); }
    }

    public sealed class UiStatistic
    {
        public string Label { get; }
        public string Value { get; }
        public UiStatistic(string label, string value) { Label = label ?? string.Empty; Value = value ?? string.Empty; }
    }

    public sealed class UiResultData
    {
        public string Title { get; }
        public string Description { get; }
        public IReadOnlyList<UiStatistic> Statistics { get; }
        public bool CanNext { get; }
        public UiResultData(string title, string description, IReadOnlyList<UiStatistic> statistics, bool canNext = false)
        { Title = title ?? string.Empty; Description = description ?? string.Empty; Statistics = statistics ?? new List<UiStatistic>(); CanNext = canNext; }
    }

    public sealed class ResultViewModel : ViewModelBase
    {
        private readonly IUiGameFlowService _flow;
        private readonly UiResultData _data;
        private readonly IDisposable _pause;
        public ResultViewModel(UiResultData data, IUiGameFlowService flow, IUiPauseService pause = null)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data)); _flow = flow ?? throw new ArgumentNullException(nameof(flow));
            _pause = pause?.AcquirePause("Result");
            RestartCommand = new AsyncUiCommand(token => _flow.RestartGameAsync(token));
            ReturnToLobbyCommand = new AsyncUiCommand(token => _flow.ReturnToLobbyAsync(token));
            NextCommand = new AsyncUiCommand(token => ((IUiNextLevelService)_flow).NextLevelAsync(token), () => CanNext);
        }
        public string Title => _data.Title;
        public string Description => _data.Description;
        public IReadOnlyList<UiStatistic> Statistics => _data.Statistics;
        public bool CanNext => _data.CanNext && _flow is IUiNextLevelService;
        public AsyncUiCommand RestartCommand { get; }
        public AsyncUiCommand ReturnToLobbyCommand { get; }
        public AsyncUiCommand NextCommand { get; }
        protected override void OnDispose() { UiCleanup.All(RestartCommand.Dispose, ReturnToLobbyCommand.Dispose, NextCommand.Dispose, () => _pause?.Dispose()); }
    }

    public enum UiDialogResult { None, Confirmed, Canceled, Alternative, Closed }

    public sealed class UiDialogRequest
    {
        public string Title { get; }
        public string Message { get; }
        public string ConfirmLabel { get; }
        public string CancelLabel { get; }
        public string AlternativeLabel { get; }
        public UiDialogRequest(string title, string message, string confirmLabel = "确认", string cancelLabel = "取消", string alternativeLabel = null)
        { Title = title ?? string.Empty; Message = message ?? string.Empty; ConfirmLabel = confirmLabel ?? "确认"; CancelLabel = cancelLabel ?? "取消"; AlternativeLabel = alternativeLabel; }
    }

    public sealed class DialogViewModel : ViewModelBase
    {
        private readonly TaskCompletionSource<UiDialogResult> _result = new TaskCompletionSource<UiDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _completed;
        public DialogViewModel(UiDialogRequest request) { Request = request ?? throw new ArgumentNullException(nameof(request)); ConfirmCommand = new UiCommand(() => TryComplete(UiDialogResult.Confirmed)); CancelCommand = new UiCommand(() => TryComplete(UiDialogResult.Canceled)); AlternativeCommand = new UiCommand(() => TryComplete(UiDialogResult.Alternative), () => !string.IsNullOrEmpty(Request.AlternativeLabel)); }
        public UiDialogRequest Request { get; }
        public UiDialogResult Result => _result.Task.Status == TaskStatus.RanToCompletion ? _result.Task.Result : UiDialogResult.None;
        public UiCommand ConfirmCommand { get; }
        public UiCommand CancelCommand { get; }
        public UiCommand AlternativeCommand { get; }
        public Task<UiDialogResult> WaitForResultAsync() => _result.Task;
        public bool TryComplete(UiDialogResult result)
        { if (Interlocked.Exchange(ref _completed, 1) != 0) return false; _result.TrySetResult(result); RaisePropertyChanged(nameof(Result)); return true; }
        protected override void OnDispose() { TryComplete(UiDialogResult.Closed); ConfirmCommand.Dispose(); CancelCommand.Dispose(); AlternativeCommand.Dispose(); }
    }

    public sealed class UiHelpItem
    {
        public string Action { get; }
        public string Description { get; }
        public UiHelpItem(string action, string description) { Action = action ?? string.Empty; Description = description ?? string.Empty; }
    }

    public sealed class HelpViewModel : ViewModelBase
    {
        public HelpViewModel(IEnumerable<UiHelpItem> items, IUiNavigationService navigation = null)
        { Items = new UiObservableList<UiHelpItem>(); if (items != null) Items.ReplaceAll(items); BackCommand = new AsyncUiCommand(t => navigation.BackAsync(t), () => navigation != null); }
        public UiObservableList<UiHelpItem> Items { get; }
        public AsyncUiCommand BackCommand { get; }
        protected override void OnDispose() { BackCommand.Dispose(); }
    }

    public sealed class AboutViewModel : ViewModelBase
    {
        public AboutViewModel(string productName, string version, string description, IUiNavigationService navigation = null)
        { ProductName = productName ?? string.Empty; Version = version ?? string.Empty; Description = description ?? string.Empty; BackCommand = new AsyncUiCommand(t => navigation.BackAsync(t), () => navigation != null); }
        public string ProductName { get; }
        public string Version { get; }
        public string Description { get; }
        public AsyncUiCommand BackCommand { get; }
        protected override void OnDispose() { BackCommand.Dispose(); }
    }

    public enum UiToastKind { Success, Info, Warning, Error }

    public sealed class UiToastMessage
    {
        public UiToastKind Kind { get; }
        public string Message { get; }
        public float DurationSeconds { get; }
        public UiToastMessage(UiToastKind kind, string message, float durationSeconds = 2.5f)
        { Kind = kind; Message = message ?? string.Empty; DurationSeconds = durationSeconds < 0 ? 0 : durationSeconds; }
    }

    /// <summary>Bounded toast state; a host decides when to advance it using unscaled time.</summary>
    public sealed class ToastViewModel : ViewModelBase
    {
        private readonly int _capacity;
        private UiToastMessage _current;
        private float _remaining;
        public ToastViewModel(int capacity = 8) { _capacity = capacity < 1 ? 1 : capacity; Queue = new UiObservableList<UiToastMessage>(); }
        public UiObservableList<UiToastMessage> Queue { get; }
        public UiToastMessage Current { get => _current; private set { _remaining = value?.DurationSeconds ?? 0; SetProperty(ref _current, value); } }

        public void Enqueue(UiToastMessage message, bool mergeSameMessage = true)
        {
            if (IsDisposed) return;
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (mergeSameMessage && Current != null && Current.Kind == message.Kind && Current.Message == message.Message) { Current = message; return; }
            for (int i = 0; i < Queue.Count; i++)
                if (mergeSameMessage && Queue[i].Kind == message.Kind && Queue[i].Message == message.Message) return;
            while (Queue.Count >= _capacity) Queue.Remove(Queue[0]);
            Queue.Add(message);
            if (Current == null) ShowNext();
        }

        public void ShowNext()
        {
            Current = Queue.Count == 0 ? null : Queue[0];
            if (Queue.Count > 0) Queue.Remove(Queue[0]);
        }

        public void Dismiss() { if (Current != null) ShowNext(); }
        public void Tick(float unscaledDeltaTime)
        {
            if (IsDisposed || Current == null || float.IsNaN(unscaledDeltaTime) || unscaledDeltaTime < 0) return;
            _remaining -= unscaledDeltaTime;
            if (_remaining <= 0) ShowNext();
        }
        protected override void OnDispose() { Queue.Clear(); _current = null; }
    }
}
