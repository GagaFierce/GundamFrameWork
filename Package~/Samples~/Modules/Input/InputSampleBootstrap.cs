using System;
using UnityEngine;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.Input;
using WFrameWork.Input.Unity;

namespace WFrameWork.Samples.Input
{
    /// <summary>
    /// Add this component to an empty GameObject in a sample scene. It installs only the
    /// explicitly owned manager/host and demonstrates logical actions plus context switching.
    /// </summary>
    public sealed class InputSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode toggleUiKey = KeyCode.Tab;
        private FrameUpdateManager _manager;
        private UnityFrameUpdateHost _host;
        private InputService _input;
        private InputFrameUpdateAdapter _adapter;
        private IDisposable _subscription;
        private InputContextToken _uiContext;
        private readonly InputActionId _jump = new InputActionId("Gameplay.Jump");

        private void Awake()
        {
            _manager = new FrameUpdateManager(FrameUpdateConfig.Default);
            _input = new InputService(new LegacyInputBackend(new[]
            {
                new LegacyInputBinding { ActionId = _jump.Value, Kind = LegacyInputBindingKind.Key, Key = jumpKey },
                new LegacyInputBinding { ActionId = "UI.Toggle", Kind = LegacyInputBindingKind.Key, Key = toggleUiKey }
            }));
            _input.RegisterContext("Gameplay", 0, false, true);
            _input.RegisterContext("UI", 100, true, false);
            _input.RegisterAction(new InputActionDefinition(_jump, InputActionType.Button, "Gameplay"));
            _input.RegisterAction(new InputActionDefinition(new InputActionId("UI.Toggle"), InputActionType.Button, "UI"));
            _host = UnityFrameUpdateHost.Install(_manager);
            _adapter = new InputFrameUpdateAdapter(_input, _manager, _host.Loops.Input);
            _subscription = _input.Subscribe(_jump, e => Debug.Log("Jump event: " + e.Phase));
        }

        private void Update()
        {
            var snapshot = _input.Snapshot;
            if (snapshot.IsPressed(new InputActionId("UI.Toggle")))
            {
                if (_uiContext == null || !_uiContext.IsActive) _uiContext = _input.AcquireContext("UI");
                else { _uiContext.Dispose(); _uiContext = null; }
            }
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(20, 20, 500, 24), "Input sample: Space=Gameplay.Jump, Tab=toggle UI context");
            GUI.Label(new Rect(20, 44, 500, 24), "Jump held: " + _input.Snapshot.IsHeld(_jump));
        }

        private void OnDestroy()
        {
            _subscription?.Dispose(); _uiContext?.Dispose(); _adapter?.Dispose(); _host?.Dispose();
            _input?.Dispose(); _manager?.Dispose();
        }
    }
}

