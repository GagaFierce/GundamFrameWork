#if GFRAMEWORK_INPUT_SYSTEM
using System;
using UnityEngine.InputSystem;
using SystemInputActionType = UnityEngine.InputSystem.InputActionType;

namespace WFrameWork.Input.NewInputSystem
{
    public sealed class NewInputActionBinding
    {
        public InputActionId ActionId;
        public InputAction Action;
        public bool IsAxis2D;
        public bool OwnsAction;
    }

    /// <summary>
    /// Optional adapter compiled only when com.unity.inputsystem is installed. It never calls
    /// UnityEngine.Input and leaves the project's Active Input Handling/update mode untouched.
    /// </summary>
    public sealed class NewInputSystemBackend : IInputBackend
    {
        private readonly NewInputActionBinding[] _bindings;

        public NewInputSystemBackend(NewInputActionBinding[] bindings)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i] == null || _bindings[i].Action == null)
                    throw new ArgumentException("Each binding requires an InputAction.", nameof(bindings));
                _bindings[i].Action.Enable();
            }
        }

        public void Sample(InputSampleWriter writer)
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                var binding = _bindings[i];
                var action = binding.Action;
                if (action.type == SystemInputActionType.Button)
                {
                    writer.SetButton(binding.ActionId, action.IsPressed());
                }
                else if (binding.IsAxis2D)
                {
                    var value = action.ReadValue<UnityEngine.Vector2>();
                    writer.SetAxis2D(binding.ActionId, value.x, value.y);
                }
                else writer.SetAxis1D(binding.ActionId, action.ReadValue<float>());
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _bindings.Length; i++) _bindings[i].Action.Reset();
        }

        public void Dispose()
        {
            for (int i = 0; i < _bindings.Length; i++)
            {
                if (_bindings[i].OwnsAction) _bindings[i].Action.Disable();
            }
        }
    }
}
#endif
