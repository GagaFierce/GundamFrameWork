using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>
    /// Fixed-height vertical ScrollRect list. Owns row clones, positions and content height.
    /// Bind must overwrite all row data; unbind must remove item-specific listeners and leases.
    /// Content must be a direct child of viewport, with no enabled layout group or content fitter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiVirtualList : MonoBehaviour
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform itemTemplate;
        [SerializeField, Min(1)] private float rowHeight = 48;
        [SerializeField, Min(0)] private float spacing = 4;
        [SerializeField, Min(0)] private int overscan = 1;
        private sealed class Cell { internal RectTransform View; internal int Index = -1; }
        private readonly List<Cell> _cells = new List<Cell>();
        private Action<RectTransform, int> _bind;
        private Action<RectTransform, int> _unbind;
        private int _count;
        private bool _initialized;
        private bool _refreshing;
        private bool _releasing;
        private Vector2 _viewportSize;
        public int ItemCount => _count;
        public int CreatedRowCount => _cells.Count;

        public void Initialize(ScrollRect scroll, RectTransform template, float height, float gap = 0, int extraRows = 1)
        {
            if (_initialized) throw new InvalidOperationException("List is already initialized.");
            if (scroll == null || scroll.content == null || scroll.viewport == null || template == null)
                throw new ArgumentException("ScrollRect, viewport, content and template are required.");
            if (scroll.content.parent != scroll.viewport || template == scroll.content || template == scroll.viewport)
                throw new ArgumentException("Content must be a direct child of viewport; template must be a separate row.");
            if (!IsFinite(height) || height <= 0 || !IsFinite(gap) || gap < 0 || extraRows < 0 || extraRows > 100)
                throw new ArgumentOutOfRangeException(nameof(height));
            var layout = scroll.content.GetComponent<LayoutGroup>();
            var fitter = scroll.content.GetComponent<ContentSizeFitter>();
            if ((layout != null && layout.enabled) || (fitter != null && fitter.enabled))
                throw new InvalidOperationException("Virtual list controls layout; disable content LayoutGroup and ContentSizeFitter.");
            scrollRect = scroll; itemTemplate = template; rowHeight = height; spacing = gap; overscan = extraRows;
            scrollRect.horizontal = false; scrollRect.vertical = true;
            var content = scrollRect.content;
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = new Vector2(0, content.sizeDelta.y);
            content.anchoredPosition = Vector2.zero;
            itemTemplate.gameObject.SetActive(false);
            _initialized = true;
            if (isActiveAndEnabled) scrollRect.onValueChanged.AddListener(OnScrolled);
        }

        public void SetItems(int count, Action<RectTransform, int> bind, Action<RectTransform, int> unbind = null)
        {
            if (_refreshing || _releasing) throw new InvalidOperationException("Do not replace list data inside a row callback.");
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (bind == null) throw new ArgumentNullException(nameof(bind));
            EnsureInitialized();
            ReleaseAll();
            _count = count; _bind = bind; _unbind = unbind;
            Refresh();
        }
        public void RefreshItems()
        {
            if (_refreshing || _releasing) throw new InvalidOperationException("Do not refresh inside a row callback.");
            ReleaseAll(); Refresh();
        }
        public void ScrollTo(int index)
        {
            EnsureInitialized();
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            scrollRect.StopMovement();
            float max = Mathf.Max(0, TotalHeight() - scrollRect.viewport.rect.height);
            scrollRect.content.anchoredPosition = new Vector2(0, Mathf.Min(index * (rowHeight + spacing), max));
            Refresh();
        }
        private void EnsureInitialized()
        { if (!_initialized) Initialize(scrollRect, itemTemplate, rowHeight, spacing, overscan); }
        private void OnEnable()
        {
            if (!_initialized) return;
            scrollRect.onValueChanged.AddListener(OnScrolled); Refresh();
        }
        private void OnDisable()
        {
            if (_initialized && scrollRect != null) scrollRect.onValueChanged.RemoveListener(OnScrolled);
            ReleaseAll();
        }
        private void OnDestroy()
        {
            ReleaseAll();
            for (int i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].View == null) continue;
                if (Application.isPlaying) Destroy(_cells[i].View.gameObject);
                else DestroyImmediate(_cells[i].View.gameObject);
            }
            _cells.Clear(); _bind = null; _unbind = null;
        }
        private void LateUpdate()
        {
            if (_initialized && scrollRect.viewport.rect.size != _viewportSize) Refresh();
        }
        private void OnScrolled(Vector2 _) { Refresh(); }
        private float TotalHeight() { return _count == 0 ? 0 : _count * (rowHeight + spacing) - spacing; }
        private void Refresh()
        {
            if (!_initialized || !isActiveAndEnabled || _refreshing) return;
            _refreshing = true;
            try
            {
                _viewportSize = scrollRect.viewport.rect.size;
                float height = Mathf.Max(0, _viewportSize.y);
                var content = scrollRect.content;
                float total = TotalHeight();
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(height, total));
                float offset = Mathf.Clamp(content.anchoredPosition.y, 0, Mathf.Max(0, total - height));
                // Data shrink must also clamp the actual scroll position.
                if (content.anchoredPosition.y > Mathf.Max(0, total - height))
                { scrollRect.StopMovement(); content.anchoredPosition = new Vector2(0, offset); }
                int needed = height <= 0 ? 0 : Mathf.Min(_count, Mathf.CeilToInt(height / (rowHeight + spacing)) + 1 + 2 * overscan);
                // A larger modulus changes every slot mapping; release old indices before rebinding.
                if (_cells.Count < needed) ReleaseAll();
                while (_cells.Count < needed)
                {
                    var view = Instantiate(itemTemplate, content, false);
                    view.gameObject.SetActive(false);
                    view.anchorMin = new Vector2(0, 1); view.anchorMax = new Vector2(1, 1); view.pivot = new Vector2(0.5f, 1);
                    view.sizeDelta = new Vector2(0, rowHeight);
                    _cells.Add(new Cell { View = view });
                }
                int first = Mathf.Clamp(Mathf.FloorToInt(offset / (rowHeight + spacing)) - overscan, 0, Mathf.Max(0, _count - needed));
                // Circular assignment preserves existing bindings as the viewport advances one row.
                for (int slot = 0; slot < _cells.Count; slot++)
                {
                    var cell = _cells[slot];
                    int relative = (slot - first % _cells.Count + _cells.Count) % _cells.Count;
                    int index = relative < needed ? first + relative : -1;
                    if (cell.Index == index) continue;
                    Release(cell);
                    if (index < 0 || _bind == null || !isActiveAndEnabled) continue;
                    cell.Index = index;
                    cell.View.anchoredPosition = new Vector2(0, -index * (rowHeight + spacing));
                    try { _bind(cell.View, index); }
                    catch { Release(cell); throw; }
                    if (cell.Index >= 0 && isActiveAndEnabled) cell.View.gameObject.SetActive(true);
                }
            }
            finally { _refreshing = false; }
        }
        private void ReleaseAll()
        {
            if (_releasing) return;
            _releasing = true;
            try { for (int i = 0; i < _cells.Count; i++) Release(_cells[i]); }
            finally { _releasing = false; }
        }
        private void Release(Cell cell)
        {
            if (cell.Index < 0) return;
            int index = cell.Index; cell.Index = -1;
            try { _unbind?.Invoke(cell.View, index); }
            catch (Exception error) { Debug.LogException(error, this); }
            finally { if (cell.View != null) cell.View.gameObject.SetActive(false); }
        }
        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
