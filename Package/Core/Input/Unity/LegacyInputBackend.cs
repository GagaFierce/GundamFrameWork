using System;
using System.Collections.Generic;
using UnityEngine;
using LegacyInput = UnityEngine.Input;

namespace WFrameWork.Input.Unity
{
    public enum LegacyInputBindingKind
    {
        Key,
        MouseButton,
        NamedButton,
        NamedAxis
    }

    [Serializable]
    public sealed class LegacyInputBinding
    {
        [Tooltip("Stable logical action id, for example Gameplay.Jump.")]
        public string ActionId;
        public LegacyInputBindingKind Kind;
        public KeyCode Key = KeyCode.None;
        [Range(0, 2)] public int MouseButton;
        public string LegacyName;
    }

    /// <summary>Samples Unity's Legacy Input API once per render frame.</summary>
    public sealed class LegacyInputBackend : IInputBackend
    {
        private readonly LegacyInputBinding[] _bindings;

        public LegacyInputBackend(IReadOnlyList<LegacyInputBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            _bindings = new LegacyInputBinding[bindings.Count];
            for (int i = 0; i < _bindings.Length; i++)
            {
                _bindings[i] = bindings[i] ?? throw new ArgumentException("Binding cannot be null.", nameof(bindings));
                if (string.IsNullOrWhiteSpace(_bindings[i].ActionId)) throw new ArgumentException("Binding action id is required.", nameof(bindings));
                if (_bindings[i].Kind == LegacyInputBindingKind.Key && _bindings[i].Key == KeyCode.None)
                    throw new ArgumentException("A key binding requires a KeyCode.", nameof(bindings));
                if (_bindings[i].Kind == LegacyInputBindingKind.NamedButton || _bindings[i].Kind == LegacyInputBindingKind.NamedAxis)
                    if (string.IsNullOrWhiteSpace(_bindings[i].LegacyName)) throw new ArgumentException("A named binding requires LegacyName.", nameof(bindings));
            }
        }

        public void Sample(InputSampleWriter writer)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                var binding = _bindings[i];
                var actionId = new InputActionId(binding.ActionId);
                switch (binding.Kind)
                {
                    case LegacyInputBindingKind.Key:
                        writer.SetButton(actionId, LegacyInput.GetKey(binding.Key));
                        break;
                    case LegacyInputBindingKind.MouseButton:
                        writer.SetButton(actionId, LegacyInput.GetMouseButton(binding.MouseButton));
                        break;
                    case LegacyInputBindingKind.NamedButton:
                        writer.SetButton(actionId, LegacyInput.GetButton(binding.LegacyName));
                        break;
                    case LegacyInputBindingKind.NamedAxis:
                        writer.SetAxis1D(actionId, LegacyInput.GetAxisRaw(binding.LegacyName));
                        break;
                    default:
                        throw new InvalidOperationException("Unknown Legacy input binding kind.");
                }
            }
        }

        public void Reset() { }
        public void Dispose() { }
    }

    /// <summary>Optional convenience driver. It owns the service only when configured as owner.</summary>
    [DisallowMultipleComponent]
    public sealed class LegacyInputDriver : MonoBehaviour
    {
        private InputService _service;
        private bool _ownsService;

        public void Attach(InputService service, bool ownsService = false)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ownsService = ownsService;
        }

        private void Update()
        {
            if (_service == null || _service.IsDisposed) return;
            _service.Update(Time.frameCount);
        }

        private void OnApplicationFocus(bool focused) => _service?.SetApplicationFocus(focused);
        private void OnApplicationPause(bool paused) => _service?.SetApplicationPaused(paused);

        private void OnDestroy()
        {
            if (_ownsService) _service?.Dispose();
            _service = null;
        }
    }
}
