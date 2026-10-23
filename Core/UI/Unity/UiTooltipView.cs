using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>Shared overlay. Owners can only hide their own tooltip. Keep the view object active.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class UiTooltipView : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Vector2 offset = new Vector2(16, -16);
        private RectTransform _rect;
        private CanvasGroup _group;
        private object _owner;
        private readonly Vector3[] _corners = new Vector3[4];
        public bool IsVisible => _owner != null;
        public void Initialize(TMP_Text text)
        { label = text != null ? text : throw new ArgumentNullException(nameof(text)); EnsureComponents(); HideAll(); }
        private void Awake() { EnsureComponents(); HideAll(); }
        private void EnsureComponents()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (_group == null) _group = GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false; _group.interactable = false;
        }
        public void Show(object owner, string text, Vector2 screenPosition, Camera eventCamera = null)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (label == null) throw new InvalidOperationException("Assign the tooltip text component first.");
            EnsureComponents();
            if (!(_rect.parent is RectTransform parent)) throw new InvalidOperationException("Tooltip requires a RectTransform parent.");
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
                eventCamera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, eventCamera, out var point)) return;
            _owner = owner; label.text = text ?? string.Empty; label.raycastTarget = false;
            _group.alpha = 1;
            _rect.SetAsLastSibling();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
            _rect.localPosition = new Vector3(point.x + offset.x, point.y + offset.y, _rect.localPosition.z);
            Clamp(parent);
        }
        private void Clamp(RectTransform parent)
        {
            _rect.GetWorldCorners(_corners);
            Vector2 min = parent.InverseTransformPoint(_corners[0]), max = min;
            for (int i = 1; i < 4; i++)
            { Vector2 p = parent.InverseTransformPoint(_corners[i]); min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            var bounds = parent.rect;
            float x = max.x - min.x > bounds.width ? bounds.center.x - (min.x + max.x) / 2 : Mathf.Clamp(0, bounds.xMin - min.x, bounds.xMax - max.x);
            float y = max.y - min.y > bounds.height ? bounds.center.y - (min.y + max.y) / 2 : Mathf.Clamp(0, bounds.yMin - min.y, bounds.yMax - max.y);
            _rect.localPosition += new Vector3(x, y, 0);
        }
        public void Hide(object owner) { if (ReferenceEquals(_owner, owner)) HideAll(); }
        private void HideAll() { _owner = null; if (_group != null) _group.alpha = 0; }
        private void OnDisable() { HideAll(); }
    }
}
