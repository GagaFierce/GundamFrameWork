using UnityEngine;
using UnityEngine.EventSystems;

namespace WFrameWork.UI.Unity
{
    /// <summary>Shows help on hover or keyboard/gamepad selection; releases ownership on disable.</summary>
    [DisallowMultipleComponent]
    public sealed class UiTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private UiTooltipView view;
        [SerializeField, TextArea] private string text;
        private bool _hovered;
        private bool _selected;
        public void Configure(UiTooltipView tooltip, string message)
        { if (view != null) view.Hide(this); view = tooltip; text = message; _hovered = false; _selected = false; }
        public void OnPointerEnter(PointerEventData data)
        {
            if (!isActiveAndEnabled || view == null) return;
            _hovered = true; view.Show(this, text, data.position, data.enterEventCamera);
        }
        public void OnPointerExit(PointerEventData data)
        { _hovered = false; if (!_selected && view != null) view.Hide(this); }
        public void OnSelect(BaseEventData data)
        {
            if (!isActiveAndEnabled || view == null) return;
            _selected = true;
            var canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var rect = transform as RectTransform;
            Vector3 center = rect != null ? rect.TransformPoint(rect.rect.center) : transform.position;
            view.Show(this, text, RectTransformUtility.WorldToScreenPoint(camera, center), camera);
        }
        public void OnDeselect(BaseEventData data)
        { _selected = false; if (!_hovered && view != null) view.Hide(this); }
        private void OnDisable() { _hovered = false; _selected = false; if (view != null) view.Hide(this); }
    }
}
