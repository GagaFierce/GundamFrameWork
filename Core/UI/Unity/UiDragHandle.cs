using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace WFrameWork.UI.Unity
{
    /// <summary>Drags a RectTransform in its parent plane, preserving the initial pointer offset.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class UiDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private bool constrainToParent = true;
        [SerializeField] private CanvasGroup raycastGroup;
        [SerializeField] private UnityEvent dragStarted = new UnityEvent();
        [SerializeField] private UnityEvent dragEnded = new UnityEvent();
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _parent;
        private Camera _camera;
        private Vector2 _startPointer;
        private Vector3 _startPosition;
        private bool _wasBlocking;
        private int _pointer;
        public bool IsDragging { get; private set; }
        public UnityEvent DragStarted => dragStarted;
        public UnityEvent DragEnded => dragEnded;
        public void Configure(RectTransform dragTarget, CanvasGroup group = null, bool clamp = true)
        { CancelDrag(); target = dragTarget; raycastGroup = group; constrainToParent = clamp; }
        public void OnBeginDrag(PointerEventData data)
        {
            if (!isActiveAndEnabled || IsDragging || data.button != PointerEventData.InputButton.Left) return;
            if (target == null) target = (RectTransform)transform;
            _parent = target.parent as RectTransform;
            _camera = data.pressEventCamera;
            if (_parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, data.position, _camera, out _startPointer)) return;
            _startPosition = target.localPosition; _pointer = data.pointerId; IsDragging = true;
            if (raycastGroup != null) { _wasBlocking = raycastGroup.blocksRaycasts; raycastGroup.blocksRaycasts = false; }
            try { dragStarted.Invoke(); }
            catch { Finish(false, false); throw; }
        }
        public void OnDrag(PointerEventData data)
        {
            if (!IsDragging || data.pointerId != _pointer) return;
            if (target == null || _parent == null || target.parent != _parent) { CancelDrag(); return; }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, data.position, _camera, out var pointer)) return;
            target.localPosition = _startPosition + (Vector3)(pointer - _startPointer);
            if (constrainToParent) ClampToParent();
        }
        private void ClampToParent()
        {
            target.GetWorldCorners(_corners);
            Vector2 min = _parent.InverseTransformPoint(_corners[0]);
            Vector2 max = min;
            for (int i = 1; i < 4; i++)
            { Vector2 point = _parent.InverseTransformPoint(_corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            Rect bounds = _parent.rect;
            float x = Correction(min.x, max.x, bounds.xMin, bounds.xMax);
            float y = Correction(min.y, max.y, bounds.yMin, bounds.yMax);
            target.localPosition += new Vector3(x, y, 0);
        }
        private static float Correction(float min, float max, float lower, float upper)
        {
            if (max - min > upper - lower) return (lower + upper - min - max) * 0.5f;
            if (min < lower) return lower - min;
            return max > upper ? upper - max : 0;
        }
        public void OnEndDrag(PointerEventData data) { if (IsDragging && data.pointerId == _pointer) Finish(false, true); }
        public void CancelDrag(bool restorePosition = false) { Finish(restorePosition, true); }
        private void Finish(bool restorePosition, bool notify)
        {
            if (!IsDragging) return;
            IsDragging = false;
            if (restorePosition && target != null && target.parent == _parent) target.localPosition = _startPosition;
            if (raycastGroup != null) raycastGroup.blocksRaycasts = _wasBlocking;
            _parent = null; _camera = null;
            if (notify) dragEnded.Invoke();
        }
        private void OnDisable() { CancelDrag(); }
        private void OnApplicationFocus(bool focus) { if (!focus) CancelDrag(); }
        private void OnApplicationPause(bool paused) { if (paused) CancelDrag(); }
    }
}
