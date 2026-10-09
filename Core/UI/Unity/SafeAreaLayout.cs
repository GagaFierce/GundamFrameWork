using UnityEngine;

namespace WFrameWork.UI.Unity
{
    /// <summary>Applies the device safe area only when screen metrics change.</summary>
    [DisallowMultipleComponent]
    public sealed class SafeAreaLayout : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private int _lastWidth;
        private int _lastHeight;

        private void Awake() { _rect = transform as RectTransform; }
        private void OnEnable() { ApplyIfChanged(true); }
        private void Update() { ApplyIfChanged(false); }

        private void ApplyIfChanged(bool force)
        {
            if (_rect == null) return;
            Rect safeArea = Screen.safeArea;
            if (!force && safeArea == _lastSafeArea && Screen.width == _lastWidth && Screen.height == _lastHeight) return;
            _lastSafeArea = safeArea; _lastWidth = Screen.width; _lastHeight = Screen.height;
            Vector2 min = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
            Vector2 max = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
            _rect.anchorMin = min; _rect.anchorMax = max; _rect.offsetMin = Vector2.zero; _rect.offsetMax = Vector2.zero;
        }
    }
}
