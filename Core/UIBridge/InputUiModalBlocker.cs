using System;
using WFrameWork.Input;
using WFrameWork.UI;

namespace WFrameWork.UIBridge
{
    /// <summary>Bridge assembly owns the cross-module dependency; Input and UI remain independent.</summary>
    public sealed class InputUiModalBlocker : IUiModalInputBlocker
    {
        private readonly InputService _input;
        private readonly string _contextId;
        public InputUiModalBlocker(InputService input, string contextId = "UI")
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("Context id is required.", nameof(contextId));
            _contextId = contextId;
            _input.RegisterContext(_contextId, 100, true, false);
        }
        public IDisposable PushModal(UiPanelId panelId) => _input.AcquireContext(_contextId);
    }
}

