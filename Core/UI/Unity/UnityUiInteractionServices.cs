using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WFrameWork.UI.Unity
{
    public sealed class EventSystemUiFocusService : IUiFocusService
    {
        private readonly EventSystem _eventSystem;
        public EventSystemUiFocusService(EventSystem eventSystem = null) { _eventSystem = eventSystem ?? EventSystem.current; }
        public object CaptureFocusedElement() => _eventSystem == null ? null : _eventSystem.currentSelectedGameObject;
        public void RestoreFocusedElement(object focusedElement)
        {
            if (_eventSystem == null) return;
            var gameObject = focusedElement as GameObject;
            _eventSystem.SetSelectedGameObject(gameObject != null && gameObject ? gameObject : null);
        }
    }

    /// <summary>Composes the logical Input context blocker with a CanvasGroup raycast barrier.</summary>
    public sealed class UnityUiModalInputBlocker : IUiModalInputBlocker
    {
        private readonly IUiModalInputBlocker _input;
        private readonly CanvasGroup _barrier;
        private int _depth;
        public UnityUiModalInputBlocker(CanvasGroup barrier = null, IUiModalInputBlocker input = null)
        { _input = input; _barrier = barrier; }
        public IDisposable PushModal(UiPanelId panelId)
        {
            IDisposable inputToken = _input == null ? null : _input.PushModal(panelId);
            _depth++;
            if (_barrier != null) { _barrier.alpha = 1; _barrier.blocksRaycasts = true; _barrier.interactable = true; }
            return new Token(() =>
            {
                inputToken?.Dispose(); if (_depth > 0) _depth--;
                if (_depth == 0 && _barrier != null) { _barrier.alpha = 0; _barrier.blocksRaycasts = false; _barrier.interactable = false; }
            });
        }
        private sealed class Token : IDisposable
        {
            private Action _release; internal Token(Action release) { _release = release; }
            public void Dispose() { var release = _release; _release = null; release?.Invoke(); }
        }
    }
}
