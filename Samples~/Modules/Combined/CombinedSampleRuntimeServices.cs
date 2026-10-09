using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Application;
using WFrameWork.Application.Unity;
using WFrameWork.Audio;
using WFrameWork.Audio.Unity;
using WFrameWork.Config;
using WFrameWork.Config.Unity;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.ResLoad;
using WFrameWork.Core.ResLoad.Unity;
using WFrameWork.Diagnostics.Unity;
using WFrameWork.Input;
using WFrameWork.Input.Unity;
using WFrameWork.Physics;
using WFrameWork.Physics.Unity;
using WFrameWork.PhysicsInputBridge;
using WFrameWork.Pool.Unity;
using WFrameWork.Scene;
using WFrameWork.Scene.Unity;
using WFrameWork.UI;
using WFrameWork.UI.Unity;
using WFrameWork.UIBridge;

namespace WFrameWork.Samples.Combined
{
    [Serializable]
    public sealed class CombinedSampleSettings
    {
        public float MasterVolume = 1;
        public float MusicVolume = 1;
        public float SfxVolume = 1;
        public float UiVolume = 1;
        public bool Muted;
        public int WindowMode;
        public int ResolutionIndex;
        public int QualityIndex;
    }

    /// <summary>Single composition root for the complete sample flow.</summary>
    [DisallowMultipleComponent]
    public sealed partial class CombinedSampleRuntimeServices : MonoBehaviour,
        IUiGameFlowService, IUiNextLevelService, IUiSettingsService, IUiNavigationService, IUiExitService, IUiDialogService
    {
        [SerializeField] private AudioServiceBehaviour audioBehaviour;
        [SerializeField] private UiPanelManagerBehaviour ui;
        [SerializeField] private RigidbodyFixedStepBehaviour player;
        [SerializeField] private CombinedSampleConfig configuration;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private string playerPrefabAddress = "GFramework.Samples.Player";
        [SerializeField] private string sfxAddress = "GFramework.Samples.Click";
        [SerializeField] private string gameSceneAddress = "GFramework.Samples.Game";
        private UnityGameRuntime _unityRuntime;
        private AddressablesResourceService _addressables;
        private AddressableGameObjectPool _playerPool;
        private SceneFlowService _sceneFlow;
        private SaveService<CombinedSampleSettings> _save;
        private InputService _input;
        private InputFrameUpdateAdapter _inputAdapter;
        private PhysicsStepDispatcher _physics;
        private FixedInputParticipant _fixedInput;
        private GameFlowService _flow;
        private Task _readyTask;
        private readonly object _transitionGate = new object();
        private Task _startTask;
        private Task _returnTask;
        private GameObject _gameplayPlayer;
        private PhysicsParticipantHandle _gameplayBinding;
        private AudioPlaybackHandle _gameplaySfx;
        private readonly object _shutdownGate = new object();
        private Task _shutdownTask;
        private bool _ready;
        private UiSettingsSnapshot _settingsSnapshot = UiSettingsSnapshot.Default;
        private UpdateGroup _gameplayGroup;

        public GameRuntime Runtime => _unityRuntime?.Runtime;
        public ResourceService Resources => _addressables?.Service;
        public SceneFlowService SceneFlow => _sceneFlow;
        public AudioService Audio => audioBehaviour?.Service;
        public InputService Input => _input;
        public UiPanelManagerBehaviour UiBehaviour => ui;
        public GameFlowService Flow => _flow;
        public bool IsReady => _ready;
        public Task ReadyTask => _readyTask ?? Task.FromException(new InvalidOperationException("Runtime has not been created."));
        public UiSettingsSnapshot Current => _settingsSnapshot;

        public IReadOnlyList<UiHelpItem> GetHelpItems()
        {
            return new List<UiHelpItem>
            {
                new UiHelpItem("移动/交互", "本示例使用实际配置的输入动作；当前演示提供 Space 跳跃。"),
                new UiHelpItem("暂停/返回", "Escape 打开暂停菜单；在界面中返回上一层。"),
                new UiHelpItem("返回大厅", "R 打开返回确认。"),
                new UiHelpItem("示例结算", "F1 显示成功结算，F2 显示失败结算。")
            };
        }

        private void Awake()
        {
            try
            {
                if (configuration != null)
                {
                    playerPrefabAddress = configuration.PlayerPrefabAddress;
                    sfxAddress = configuration.SfxAddress;
                    gameSceneAddress = configuration.GameSceneAddress;
                }
                _unityRuntime = UnityGameRuntime.Create(new UnityDiagnosticSink());
                var mainThread = _unityRuntime.MainThread;
                _addressables = new AddressablesResourceService(new UnityDiagnosticSink(), mainThread);
                _sceneFlow = new SceneFlowService(new AddressablesSceneBackend(mainThread), new UnityDiagnosticSink());
                _playerPool = new AddressableGameObjectPool(_addressables.Service, playerPrefabAddress, poolRoot, 4,
                    mainThread: mainThread);
                InitializeDisplayOptions();
                _save = new SaveService<CombinedSampleSettings>(new UnityTextFileStore(),
                    new JsonUtilitySerializer<CombinedSampleSettings>(CreateDefaultSettings, MigrateSettings),
                    2, new UnityDiagnosticSink(), UnityEngine.Application.persistentDataPath);
                CreateInputAndPhysics();
                if (audioBehaviour != null) audioBehaviour.Initialize(_addressables.Service, mainThread);
                _unityRuntime.Runtime.SetModuleServices(_addressables.Service, _sceneFlow, audio: audioBehaviour?.Service,
                    input: _input, physics: _physics);
                _flow = new GameFlowService(LoadGameSceneForFlowAsync, UnloadGameSceneForFlowAsync,
                    _sceneFlow.WaitForIdleAsync, () => Runtime.ApplicationScope.CreateChild("Gameplay"), new UnityDiagnosticSink());
                RegisterRuntimeParts();
                _readyTask = InitializeRuntimeAsync();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                _readyTask = Task.FromException(error);
            }
        }

        private void CreateInputAndPhysics()
        {
            var backend = new LegacyInputBackend(new[]
            {
                new LegacyInputBinding { ActionId = "Gameplay.OpenSettings", Kind = LegacyInputBindingKind.Key, Key = KeyCode.Escape },
                new LegacyInputBinding { ActionId = "UI.Back", Kind = LegacyInputBindingKind.Key, Key = KeyCode.Escape },
                new LegacyInputBinding { ActionId = "Gameplay.Return", Kind = LegacyInputBindingKind.Key, Key = KeyCode.R },
                new LegacyInputBinding { ActionId = "Gameplay.Jump", Kind = LegacyInputBindingKind.Key, Key = KeyCode.Space }
                ,new LegacyInputBinding { ActionId = "Gameplay.Success", Kind = LegacyInputBindingKind.Key, Key = KeyCode.F1 }
                ,new LegacyInputBinding { ActionId = "Gameplay.Failure", Kind = LegacyInputBindingKind.Key, Key = KeyCode.F2 }
            });
            _input = new InputService(backend);
            _input.RegisterContext("Gameplay", 0, false, true);
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.OpenSettings"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Jump"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Success"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Failure"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Return"), InputActionType.Button));
            _input.RegisterContext("UI", 100, true, false);
            _input.RegisterAction(new InputActionDefinition(new InputActionId("UI.Back"), InputActionType.Button, "UI"));
            _inputAdapter = new InputFrameUpdateAdapter(_input, _unityRuntime.FrameUpdate, _unityRuntime.Host.Loops.Input);
            _gameplayGroup = _unityRuntime.FrameUpdate.CreateGroup("Sample.Gameplay", UpdateGroupOptions.Default);
            _physics = new PhysicsStepDispatcher(_unityRuntime.FrameUpdate, _unityRuntime.Host.Loops.Physics, group: _gameplayGroup);
            _fixedInput = new FixedInputParticipant(_input, HandleFixedInput);
            _physics.Register(_fixedInput);
            if (player != null) _physics.Register(player);
        }

        private void RegisterRuntimeParts()
        {
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Addressables", _ => _addressables.InitializeAsync(),
                _addressables.CloseAsync, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Scene", shutdown: _sceneFlow.ShutdownAsync, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Pool", _ => _playerPool.WarmupAsync(1), _playerPool.CloseAsync, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Audio", _ =>
            {
                if (audioBehaviour != null && audioBehaviour.Service == null)
                    audioBehaviour.Initialize(_addressables.Service, _unityRuntime.MainThread);
                return Task.CompletedTask;
            }, () => audioBehaviour == null ? Task.CompletedTask : audioBehaviour.ShutdownAsync(), requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Save", async _ =>
            {
                SaveResult<CombinedSampleSettings> settings = await _save.LoadAsync(configuration == null ? "gframework-settings.json" : configuration.SaveFileName);
                ApplySettings(settings.Value);
            }, _save.CloseAsync, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Input", shutdown: () =>
            {
                _inputAdapter?.Dispose(); _input?.Dispose(); return Task.CompletedTask;
            }, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Physics", shutdown: () =>
            {
                _fixedInput?.Dispose(); _physics?.Dispose(); return Task.CompletedTask;
            }, requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("UI", async _ =>
            {
                if (ui != null) await ui.InitializeAsync(_unityRuntime.FrameUpdate, _unityRuntime.Host.Loops,
                    modalBlocker: new UnityUiModalInputBlocker(input: new InputUiModalBlocker(_input)), resourceService: _addressables.Service);
            }, () => ui?.Manager == null ? Task.CompletedTask : ui.Manager.CloseAllAsync(), requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Flow", shutdown: () => _flow == null ? Task.CompletedTask : _flow.ShutdownAsync(), requiresMainThread: true));
        }

        private async Task InitializeRuntimeAsync()
        {
            try
            {
                await _unityRuntime.Runtime.InitializeAsync();
                await _flow.EnterMenuAsync();
                _ready = true;
                _toasts = new ToastViewModel();
                if (ui != null)
                {
                    await ui.OpenAsync(new UiPanelId("Toast"), _toasts);
                    await ui.OpenAsync(new UiPanelId("Menu"), this);
                }
            }
            catch (Exception error) { _ready = false; Debug.LogException(error, this); throw; }
        }

        private void ApplySettings(CombinedSampleSettings settings)
        {
            var snapshot = new UiSettingsSnapshot(settings == null ? 1 : settings.MasterVolume,
                settings == null ? 1 : settings.MusicVolume, settings == null ? 1 : settings.SfxVolume,
                settings == null ? 1 : settings.UiVolume, settings != null && settings.Muted,
                settings == null ? 0 : settings.WindowMode, settings == null ? 0 : settings.ResolutionIndex,
                settings == null ? 0 : settings.QualityIndex);
            _settingsSnapshot = snapshot;
            ApplySettingsSnapshot(snapshot);
            ApplyDisplaySettings(snapshot);
        }

        private void ApplySettingsSnapshot(UiSettingsSnapshot snapshot)
        {
            if (audioBehaviour?.Service == null) return;
            float master = snapshot.MasterVolume;
            audioBehaviour.Service.SetVolume(AudioBus.Bgm, snapshot.MusicVolume * master);
            audioBehaviour.Service.SetVolume(AudioBus.Sfx, snapshot.SfxVolume * master);
            audioBehaviour.Service.SetVolume(AudioBus.Ui, snapshot.UiVolume * master);
            audioBehaviour.Service.SetMuted(snapshot.Muted);
        }

        public async Task SaveSettingsAsync(float masterVolume, bool muted)
        {
            EnsureReady();
            var current = Current;
            await SaveAsync(new UiSettingsSnapshot(masterVolume, current.MusicVolume, current.SfxVolume,
                current.UiVolume, muted, current.WindowMode, current.ResolutionIndex, current.QualityIndex), CancellationToken.None);
        }

        public void Preview(UiSettingsSnapshot settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ApplySettingsSnapshot(settings);
        }

        public Task SaveAsync(UiSettingsSnapshot settings, CancellationToken token)
        {
            EnsureReady();
            Task save = SaveTrackedAsync(settings, token);
            Runtime.ApplicationScope.Track(save, "UI settings save");
            return save;
        }

        private async Task SaveTrackedAsync(UiSettingsSnapshot settings, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Runtime.ApplicationScope.CancellationToken))
                await SaveSettingsSnapshotAsync(settings, linked.Token);
        }

        private async Task SaveSettingsSnapshotAsync(UiSettingsSnapshot settings, CancellationToken token)
        {
            await SaveWithDisplayConfirmationAsync(settings, token);
        }

        Task IUiGameFlowService.StartGameAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return StartGameAsync(); }

        async Task IUiGameFlowService.RestartGameAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); await ReturnToMenuAsync(); await StartGameAsync(); }

        Task IUiGameFlowService.ReturnToLobbyAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ReturnToMenuAsync(); }

        Task IUiNavigationService.OpenSettingsAsync(UiNavigationSource source, CancellationToken token)
        { return OpenPanelAsync(new UiPanelId("Settings"), token); }

        Task IUiNavigationService.OpenHelpAsync(UiNavigationSource source, CancellationToken token)
        { return OpenPanelAsync(new UiPanelId("Help"), token); }

        Task IUiNavigationService.OpenAboutAsync(UiNavigationSource source, CancellationToken token)
        { return OpenPanelAsync(new UiPanelId("About"), token); }

        Task IUiNavigationService.BackAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ui == null ? Task.CompletedTask : ui.RequestCloseTopModalAsync(token); }

        async Task IUiExitService.RequestExitAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await ShutdownAsync();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        async Task<UiDialogResult> IUiDialogService.ShowAsync(UiDialogRequest request, CancellationToken token)
        {
            return await ShowDialogCoreAsync(request, token);
        }

        private async Task OpenPanelAsync(UiPanelId id, CancellationToken token)
        {
            EnsureReady();
            if (ui == null) throw new InvalidOperationException("UI manager is not configured.");
            await ui.OpenAsync(id, this, token);
        }

        public Task StartGameAsync()
        {
            EnsureReady();
            lock (_transitionGate)
            {
                if (_startTask != null && !_startTask.IsCompleted) return _startTask;
                _startTask = StartWithLoadingAsync();
                return _startTask;
            }
        }

        private async Task StartGameCoreAsync(CancellationToken token = default(CancellationToken))
        {
            Task cancellationCleanup = Task.CompletedTask;
            using (token.Register(() => cancellationCleanup = _flow.ReturnToMenuAsync()))
            {
            try
            {
                token.ThrowIfCancellationRequested();
                if (_flow.State == GameFlowState.Failed) await _flow.ReturnToMenuAsync();
                await _flow.StartGameAsync();
                token.ThrowIfCancellationRequested();
                if (_gameplayPlayer != null) return;
                if (_gameplayPlayer == null) _gameplayPlayer = await _unityRuntime.MainThread.RunAsync(() => _playerPool.Rent());
                _gameplayBinding = _physics.Register(_gameplayPlayer.GetComponent<RigidbodyFixedStepBehaviour>());
                if (Audio != null && !string.IsNullOrWhiteSpace(sfxAddress))
                    _gameplaySfx = await Audio.PlayAsync(sfxAddress, new AudioPlayRequest(AudioBus.Sfx, 0.8f));
                if (ui != null) await ui.Manager.HideAsync(new UiPanelId("Menu"));
            }
            catch
            {
                _gameplayBinding?.Dispose(); _gameplayBinding = null;
                if (_gameplaySfx != null) { await _gameplaySfx.StopAsync(); _gameplaySfx = null; }
                if (_gameplayPlayer != null)
                {
                    GameObject playerToReturn = _gameplayPlayer; _gameplayPlayer = null;
                    await _unityRuntime.MainThread.RunAsync(() => _playerPool.TryReturn(playerToReturn));
                }
                if (_flow.State == GameFlowState.Playing || _flow.State == GameFlowState.Returning)
                { try { await _flow.ReturnToMenuAsync(); } catch { } }
                throw;
            }
            finally { await cancellationCleanup; }
            }
        }

        public Task ReturnToMenuAsync()
        {
            EnsureReady();
            lock (_transitionGate)
            {
                if (_returnTask != null && !_returnTask.IsCompleted) return _returnTask;
                _returnTask = ReturnToMenuCoreAsync();
                return _returnTask;
            }
        }

        private async Task ReturnToMenuCoreAsync()
        {
            if (ui != null)
                foreach (string panelId in new[] { "Settings", "Pause", "Result" }) await ui.Manager.CloseAsync(new UiPanelId(panelId));
            _gameplayBinding?.Dispose(); _gameplayBinding = null;
            if (_gameplaySfx != null) { await _gameplaySfx.StopAsync(); _gameplaySfx = null; }
            if (_gameplayPlayer != null)
            {
                GameObject playerToReturn = _gameplayPlayer; _gameplayPlayer = null;
                await _unityRuntime.MainThread.RunAsync(() => _playerPool.TryReturn(playerToReturn));
            }
            await _flow.ReturnToMenuAsync();
            if (ui != null) await ui.Manager.OpenAsync(new UiPanelId("Menu"), this);
        }

        public Task<SceneLease> LoadGameSceneAsync() { EnsureReady(); return _sceneFlow.LoadAsync(gameSceneAddress, SceneLoadMode.Additive); }
        public GameObject RentPlayer() { EnsureReady(); return _playerPool.Rent(); }
        public bool ReturnPlayer(GameObject value) { return _playerPool != null && _playerPool.TryReturn(value); }

        private Task<SceneLease> LoadGameSceneForFlowAsync(CancellationToken token) => _sceneFlow.LoadAsync(gameSceneAddress, SceneLoadMode.Additive, token: token);
        private Task UnloadGameSceneForFlowAsync(SceneLease lease) => _sceneFlow.UnloadAsync(lease);
        private void HandleFixedInput(InputActionEvent inputEvent)
        {
            var actor = _gameplayPlayer != null ? _gameplayPlayer.GetComponent<RigidbodyFixedStepBehaviour>() : player;
            if (inputEvent.ActionId == new InputActionId("Gameplay.Jump") &&
                inputEvent.Phase == InputFixedEventPhase.Pressed && actor != null)
                actor.Velocity += Vector3.up * 2;
        }

        private void EnsureReady()
        { if (!_ready) throw new InvalidOperationException("Combined sample runtime is not ready."); }

        public async Task ShutdownAsync()
        {
            Task shutdown;
            lock (_shutdownGate)
            {
                if (_shutdownTask == null) _shutdownTask = ShutdownCoreAsync();
                shutdown = _shutdownTask;
            }
            await shutdown;
        }

        private async Task ShutdownCoreAsync()
        {
            if (_unityRuntime == null) return;
            _ready = false;
            await _unityRuntime.ShutdownAsync();
            _flow = null; _sceneFlow = null; _playerPool = null; _save = null; _addressables = null; _unityRuntime = null;
        }

        private void OnDestroy() { _ = ShutdownAsync(); }
    }
}
