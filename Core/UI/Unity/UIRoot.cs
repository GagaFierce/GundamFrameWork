using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WFrameWork.UI.Unity
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform hudLayer;
        [SerializeField] private RectTransform normalLayer;
        [SerializeField] private RectTransform modalLayer;
        [SerializeField] private RectTransform overlayLayer;

        public Canvas Canvas => canvas;

        public void Configure(Canvas value, RectTransform hud, RectTransform normal, RectTransform modal, RectTransform overlay)
        {
            canvas = value;
            hudLayer = hud;
            normalLayer = normal;
            modalLayer = modal;
            overlayLayer = overlay;
        }

        public bool TryValidate(out string error)
        {
            if (canvas == null) { error = "UIRoot requires a Canvas."; return false; }
            if (hudLayer == null || normalLayer == null || modalLayer == null || overlayLayer == null)
            { error = "UIRoot requires HUD, Normal, Modal and Overlay layer transforms."; return false; }
            if (EventSystem.current == null)
            { error = "No configured EventSystem exists. Add exactly one EventSystem with the matching InputModule; UIRoot will not create a second one."; return false; }
            error = null; return true;
        }

        public Transform ResolveLayer(UiPanelLayer layer)
        {
            switch (layer)
            {
                case UiPanelLayer.Hud: return hudLayer;
                case UiPanelLayer.Normal: return normalLayer;
                case UiPanelLayer.Modal: return modalLayer;
                case UiPanelLayer.Overlay: return overlayLayer;
                default: throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }
    }
}
