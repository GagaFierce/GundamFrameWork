using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WFrameWork.Scheduling;

namespace WFrameWork.UI.Unity
{
    /// <summary>Inject a scheduler (normally in an unscaled UI group). Exit, disable and focus loss cancel the hold.</summary>
    [DisallowMultipleComponent]
    public sealed class UiLongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Min(0)] private float holdSeconds = 0.5f;
        [SerializeField, Min(0)] private float repeatSeconds;
        [SerializeField] private UnityEvent pressed = new UnityEvent();
        private TimerScheduler _scheduler;
        private TimerHandle _timer;
        private Selectable _selectable;
        private bool _holding;
        private int _pointer;
        public UnityEvent Pressed => pressed;
        public void Initialize(TimerScheduler scheduler, float delay = 0.5f, float repeat = 0)
        {
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
            if (!scheduler.IsAlive) throw new ObjectDisposedException(nameof(scheduler));
            if (float.IsNaN(delay) || float.IsInfinity(delay) || delay < 0 || float.IsNaN(repeat) || float.IsInfinity(repeat) || repeat < 0)
                throw new ArgumentOutOfRangeException(nameof(delay));
            CancelHold(); _scheduler = scheduler; holdSeconds = delay; repeatSeconds = repeat;
            _selectable = GetComponent<Selectable>();
        }
        public void OnPointerDown(PointerEventData data)
        {
            if (!isActiveAndEnabled || _holding || data.button != PointerEventData.InputButton.Left) return;
            if (_selectable != null && !_selectable.IsInteractable()) return;
            if (_scheduler == null) throw new InvalidOperationException("Initialize UiLongPress with a scheduler first.");
            _holding = true; _pointer = data.pointerId;
            try { _timer = _scheduler.Delay(holdSeconds, FireFirst); }
            catch { _holding = false; throw; }
        }
        private bool CanFire => _holding && isActiveAndEnabled && (_selectable == null || _selectable.IsInteractable());
        private void FireFirst()
        {
            if (!CanFire) { CancelHold(); return; }
            try { pressed.Invoke(); }
            catch { CancelHold(); throw; }
            if (CanFire && repeatSeconds > 0) _timer = _scheduler.Repeat(repeatSeconds, FireRepeat);
        }
        private void FireRepeat()
        {
            if (!CanFire) { CancelHold(); return; }
            try { pressed.Invoke(); }
            catch { CancelHold(); throw; }
        }
        public void OnPointerUp(PointerEventData data) { if (_holding && data.pointerId == _pointer) CancelHold(); }
        public void OnPointerExit(PointerEventData data) { if (_holding && data.pointerId == _pointer) CancelHold(); }
        public void CancelHold() { _holding = false; _timer?.Cancel(); _timer = null; }
        private void OnDisable() { CancelHold(); }
        private void OnDestroy() { CancelHold(); _scheduler = null; }
        private void OnApplicationFocus(bool focus) { if (!focus) CancelHold(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelHold(); }
    }
}
