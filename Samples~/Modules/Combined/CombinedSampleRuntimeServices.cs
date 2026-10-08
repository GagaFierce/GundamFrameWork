using System;
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
        public bool Muted;
    }

    /// <summary>Single composition root for the complete sample flow.</summary>
    [DisallowMultipleComponent]
    public sealed class CombinedSampleRuntimeServices : MonoBehaviour
    {
        [SerializeField] private AudioServiceBehaviour audioBehaviour;
        [SerializeField] private UiPanelManagerBehaviour ui;
        [SerializeField] private RigidbodyFixedStepBehaviour player;
        [SerializeField] private Transform poolRoot;
        [SerializeField] private string playerPrefabAddress = "GFramework.Samples.Player";
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
        private bool _ready;

        public GameRuntime Runtime => _unityRuntime?.Runtime;
        public ResourceService Resources => _addressables?.Service;
        public SceneFlowService SceneFlow => _sceneFlow;
        public AudioService Audio => audioBehaviour?.Service;
        public InputService Input => _input;
        public UiPanelManagerBehaviour UiBehaviour => ui;
        public GameFlowService Flow => _flow;
        public bool IsReady => _ready;
        public Task ReadyTask => _readyTask ?? Task.FromException(new InvalidOperationException("Runtime has not been created."));

        private void Awake()
        {
            try
            {
                _unityRuntime = UnityGameRuntime.Create(new UnityDiagnosticSink());
                var mainThread = _unityRuntime.MainThread;
                _addressables = new AddressablesResourceService(new UnityDiagnosticSink(), mainThread);
                _sceneFlow = new SceneFlowService(new AddressablesSceneBackend(mainThread), new UnityDiagnosticSink());
                _playerPool = new AddressableGameObjectPool(_addressables.Service, playerPrefabAddress, poolRoot, 4);
                _save = new SaveService<CombinedSampleSettings>(new UnityTextFileStore(), new JsonUtilitySerializer<CombinedSampleSettings>(), 1,
                    new UnityDiagnosticSink(), UnityEngine.Application.persistentDataPath);
                CreateInputAndPhysics();
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
            });
            _input = new InputService(backend);
            _input.RegisterContext("Gameplay", 0, false, true);
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.OpenSettings"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Jump"), InputActionType.Button));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("Gameplay.Return"), InputActionType.Button));
            _input.RegisterContext("UI", 100, true, false);
            _input.RegisterAction(new InputActionDefinition(new InputActionId("UI.Back"), InputActionType.Button, "UI"));
            _inputAdapter = new InputFrameUpdateAdapter(_input, _unityRuntime.FrameUpdate, _unityRuntime.Host.Loops.Input);
            _physics = new PhysicsStepDispatcher(_unityRuntime.FrameUpdate, _unityRuntime.Host.Loops.Physics);
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
                if (audioBehaviour != null) audioBehaviour.Initialize(_addressables.Service, _unityRuntime.MainThread);
                return Task.CompletedTask;
            }, () => audioBehaviour?.Service == null ? Task.CompletedTask : audioBehaviour.Service.CloseAsync(), requiresMainThread: true));
            _unityRuntime.Runtime.AddPart(new GameRuntimePart("Save", async _ =>
            {
                SaveResult<CombinedSampleSettings> settings = await _save.LoadAsync("gframework-settings.json");
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
                if (ui != null) await ui.OpenAsync(new UiPanelId("Menu"));
            }, () => ui?.Manager == null ? Task.CompletedTask : ui.Manager.CloseAllAsync(), requiresMainThread: true));
        }

        private async Task InitializeRuntimeAsync()
        {
            try
            {
                await _unityRuntime.Runtime.InitializeAsync();
                await _flow.EnterMenuAsync();
                _ready = true;
            }
            catch (Exception error) { _ready = false; Debug.LogException(error, this); throw; }
        }

        private void ApplySettings(CombinedSampleSettings settings)
        {
            if (audioBehaviour?.Service == null) return;
            float volume = Mathf.Clamp01(settings?.MasterVolume ?? 1);
            audioBehaviour.Service.SetVolume(AudioBus.Bgm, volume);
            audioBehaviour.Service.SetVolume(AudioBus.Sfx, volume);
            audioBehaviour.Service.SetVolume(AudioBus.Ui, volume);
            audioBehaviour.Service.SetMuted(settings != null && settings.Muted);
        }

        public async Task SaveSettingsAsync(float masterVolume, bool muted)
        {
            EnsureReady();
            var settings = new CombinedSampleSettings { MasterVolume = Mathf.Clamp01(masterVolume), Muted = muted };
            await _save.SaveAsync("gframework-settings.json", settings);
            ApplySettings(settings);
        }

        public async Task StartGameAsync()
        {
            EnsureReady();
            await _flow.StartGameAsync();
            if (ui != null) await ui.Manager.HideAsync(new UiPanelId("Menu"));
        }

        public async Task ReturnToMenuAsync()
        {
            EnsureReady();
            await _flow.ReturnToMenuAsync();
            if (ui != null) await ui.OpenAsync(new UiPanelId("Menu"));
        }

        public Task<SceneLease> LoadGameSceneAsync() { EnsureReady(); return _sceneFlow.LoadAsync(gameSceneAddress, SceneLoadMode.Additive); }
        public GameObject RentPlayer() { EnsureReady(); return _playerPool.Rent(); }
        public bool ReturnPlayer(GameObject value) { return _playerPool != null && _playerPool.TryReturn(value); }

        private Task<SceneLease> LoadGameSceneForFlowAsync(CancellationToken token) => _sceneFlow.LoadAsync(gameSceneAddress, SceneLoadMode.Additive, token: token);
        private Task UnloadGameSceneForFlowAsync(SceneLease lease) => _sceneFlow.UnloadAsync(lease);
        private void HandleFixedInput(InputActionEvent inputEvent)
        {
            if (inputEvent.ActionId == new InputActionId("Gameplay.Jump") &&
                inputEvent.Phase == InputFixedEventPhase.Pressed && player != null)
                player.Velocity += Vector3.up * 2;
        }

        private void EnsureReady()
        { if (!_ready) throw new InvalidOperationException("Combined sample runtime is not ready."); }

        public async Task ShutdownAsync()
        {
            if (_unityRuntime == null) return;
            _ready = false;
            await _unityRuntime.ShutdownAsync();
            _flow = null; _sceneFlow = null; _playerPool = null; _save = null; _addressables = null;
        }

        private void OnDestroy() { _ = ShutdownAsync(); }
    }
}
