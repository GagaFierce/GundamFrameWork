using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Application;
using WFrameWork.UI;

namespace WFrameWork.Samples.Combined
{
    public sealed partial class CombinedSampleRuntimeServices : IUiPauseService, IUiDisplaySettingsService
    {
        private readonly SemaphoreSlim _dialogs = new SemaphoreSlim(1, 1);
        private readonly List<Vector2Int> _resolutionValues = new List<Vector2Int>();
        private readonly List<string> _resolutionNames = new List<string>();
        private readonly string[] _windowModes = { "窗口", "无边框全屏" };
        private string[] _qualityNames;
        private ToastViewModel _toasts;
        private int _pauseDepth;
        private float _originalTimeScale;
        private bool _ownsTimeScalePause;

        public IReadOnlyList<string> WindowModes => _windowModes;
        public IReadOnlyList<string> Resolutions => _resolutionNames;
        public IReadOnlyList<string> QualityLevels => _qualityNames;

        private void InitializeDisplayOptions()
        {
            _qualityNames = QualitySettings.names;
            _resolutionValues.Add(Vector2Int.zero); _resolutionNames.Add("保持当前分辨率");
            foreach (var resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (_resolutionValues.Contains(size)) continue;
                _resolutionValues.Add(size); _resolutionNames.Add(size.x + " × " + size.y);
            }
        }

        private CombinedSampleSettings CreateDefaultSettings() => new CombinedSampleSettings
        { WindowMode = Screen.fullScreen ? 1 : 0, QualityIndex = QualitySettings.GetQualityLevel() };

        private CombinedSampleSettings MigrateSettings(CombinedSampleSettings value, int from, int to)
        {
            if (from < 2)
            {
                value.MusicVolume = value.SfxVolume = value.UiVolume = 1;
                value.WindowMode = Screen.fullScreen ? 1 : 0;
                value.ResolutionIndex = 0; value.QualityIndex = QualitySettings.GetQualityLevel();
            }
            return value;
        }

        private async Task SaveWithDisplayConfirmationAsync(UiSettingsSnapshot next, CancellationToken token)
        {
            int width = Screen.width, height = Screen.height, quality = QualitySettings.GetQualityLevel();
            FullScreenMode mode = Screen.fullScreenMode;
            bool displayChanged = next.WindowMode != Current.WindowMode || next.ResolutionIndex != Current.ResolutionIndex || next.QualityIndex != Current.QualityIndex;
            try
            {
                if (displayChanged)
                {
                    ApplyDisplaySettings(next);
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var result = await ShowDialogCoreAsync(new UiDialogRequest("保留显示设置？", "请在 10 秒内确认，否则自动恢复。", "保留", "恢复"), timeout.Token);
                        if (result != UiDialogResult.Confirmed) throw new InvalidOperationException("显示设置已恢复，修改尚未保存。");
                    }
                }
                var data = new CombinedSampleSettings
                {
                    MasterVolume = next.MasterVolume, MusicVolume = next.MusicVolume, SfxVolume = next.SfxVolume,
                    UiVolume = next.UiVolume, Muted = next.Muted, WindowMode = next.WindowMode,
                    ResolutionIndex = next.ResolutionIndex, QualityIndex = next.QualityIndex
                };
                // POCO snapshot only: JSON and file I/O do not touch live Unity objects.
                string file = configuration == null ? "gframework-settings.json" : configuration.SaveFileName;
                await Task.Run(() => _save.SaveAsync(file, data, token), token);
                _settingsSnapshot = next;
                ApplySettingsSnapshot(next);
                _toasts?.Enqueue(new UiToastMessage(UiToastKind.Success, "设置已保存"));
            }
            catch
            {
                if (displayChanged) { Screen.SetResolution(width, height, mode); QualitySettings.SetQualityLevel(quality); }
                throw;
            }
        }

        private void ApplyDisplaySettings(UiSettingsSnapshot settings)
        {
            var size = _resolutionValues[Mathf.Clamp(settings.ResolutionIndex, 0, _resolutionValues.Count - 1)];
            int width = size.x == 0 ? Screen.width : size.x;
            int height = size.y == 0 ? Screen.height : size.y;
            var mode = settings.WindowMode == 0 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                Screen.SetResolution(width, height, mode);
            if (_qualityNames.Length > 0)
                QualitySettings.SetQualityLevel(Mathf.Clamp(settings.QualityIndex, 0, _qualityNames.Length - 1));
        }

        private async Task<UiDialogResult> ShowDialogCoreAsync(UiDialogRequest request, CancellationToken token)
        {
            EnsureReady();
            await _dialogs.WaitAsync(token);
            DialogViewModel vm = null; UiPanelHandle handle = null;
            try
            {
                vm = new DialogViewModel(request);
                handle = await ui.OpenAsync(new UiPanelId("Dialog"), vm, token);
                using (token.Register(() => vm.TryComplete(UiDialogResult.Closed)))
                    return await vm.WaitForResultAsync();
            }
            finally
            {
                try { if (handle != null) await handle.CloseAsync(); else vm?.Dispose(); }
                finally { _dialogs.Release(); }
            }
        }

        public IDisposable AcquirePause(string owner)
        {
            var groupPause = _unityRuntime.FrameUpdate.PauseGroup(_gameplayGroup);
            if (_pauseDepth++ == 0)
            {
                _originalTimeScale = Time.timeScale;
                _ownsTimeScalePause = configuration == null || configuration.PauseNativeSimulation;
                if (_ownsTimeScalePause) Time.timeScale = 0;
            }
            return new PauseLease(() =>
            {
                groupPause.Dispose();
                if (_pauseDepth > 0 && --_pauseDepth == 0)
                {
                    if (_ownsTimeScalePause && Time.timeScale == 0) Time.timeScale = _originalTimeScale;
                    _ownsTimeScalePause = false;
                }
            });
        }

        private sealed class PauseLease : IDisposable
        {
            private Action _release;
            internal PauseLease(Action release) { _release = release; }
            public void Dispose() { Interlocked.Exchange(ref _release, null)?.Invoke(); }
        }

        public Task OpenPauseOrSettingsAsync()
        {
            EnsureReady();
            return ui.OpenAsync(new UiPanelId(_flow.State == GameFlowState.Playing ? "Pause" : "Settings"), this);
        }

        public Task BackAsync() => ui.RequestCloseTopModalAsync();

        public async Task ConfirmReturnAsync()
        {
            if (await ShowDialogCoreAsync(new UiDialogRequest("返回大厅", "确定结束本次游戏并返回大厅吗？"), CancellationToken.None) == UiDialogResult.Confirmed)
                await ReturnToMenuAsync();
        }

        public async Task ShowResultAsync(bool success)
        {
            if (_flow.State != GameFlowState.Playing) return;
            await ui.Manager.CloseAsync(new UiPanelId("Pause"));
            var data = new UiResultData(success ? "挑战成功" : "挑战结束", "这是由 F1 / F2 触发的示例结算。",
                new[] { new UiStatistic("当前关卡", gameSceneAddress) }, canNext: success);
            await ui.OpenAsync(new UiPanelId("Result"), new SampleResultContext(this, data));
        }

        /// <summary>
        /// The combined sample has one playable scene, so advancing to the next level
        /// deliberately reloads that scene through the normal menu/loading transition.
        /// It is a real flow action rather than a completed task placeholder, and gives
        /// a future multi-level sample one clear extension point.
        /// </summary>
        public async Task NextLevelAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await ReturnToMenuAsync();
            token.ThrowIfCancellationRequested();
            await StartGameAsync();
        }

        private async Task StartWithLoadingAsync()
        {
            UiPanelHandle handle = null;
            var vm = new LoadingViewModel(async (progress, token) =>
            {
                progress.Report(new UiLoadingProgress("正在加载关卡…", 0, false));
                await StartGameCoreAsync(token);
            }, () => handle == null ? Task.CompletedTask : handle.CloseAsync());
            handle = await ui.OpenAsync(new UiPanelId("Loading"), vm);
            vm.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(vm.Stage) && (vm.Stage == "Complete" || vm.Stage == "Canceled"))
                    _ = CloseLoadingAsync(handle);
            };
            try { await vm.StartCommand.ExecuteAsync(); }
            catch (OperationCanceledException) { }
            // Failure stays visible on Loading; Retry uses the same operation and command.
        }

        private static async Task CloseLoadingAsync(UiPanelHandle handle)
        { try { await handle.CloseAsync(); } catch (Exception error) { Debug.LogException(error); } }
    }
}
