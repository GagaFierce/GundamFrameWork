using System;
using UnityEngine;
using UnityEngine.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>Reusable uGUI construction helpers; all assets/fonts are explicit references.</summary>
    public static class RuntimeUiBuilder
    {
        public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        public static RectTransform Fill(Transform parent, string name)
        { var rect = Rect(parent, name, 0, 0, 0, 0); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; return rect; }
        public static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
        { var rect = Rect(parent, name, x, y, width, height); var image = rect.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        public static Text Label(Transform parent, Font font, string text, float x, float y, float width, float height, int size = 22, Color? color = null)
        {
            var rect = Rect(parent, "Text", x, y, width, height); var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.text = text; label.fontSize = size;
            label.color = color ?? new Color(0.96f, 0.93f, 0.83f); label.raycastTarget = false; label.supportRichText = false; label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        public static Button Button(Transform parent, Font font, string text, float x, float y, float width, float height, Action action)
        {
            var image = Box(parent, text, x, y, width, height, new Color(0.24f, 0.36f, 0.32f)); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(() => action?.Invoke());
            var label = Label(image.transform, font, text, 8, 5, width - 16, height - 10, 21); label.alignment = TextAnchor.MiddleCenter; return button;
        }
        public static RectTransform Scroll(Transform parent, float x, float y, float width, float height)
        {
            var frame = Rect(parent, "Scroll", x, y, width, height); var scroll = frame.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Fill(frame, "Viewport"); var image = viewport.gameObject.AddComponent<Image>(); image.color = new Color(0, 0, 0, 0.001f); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, "Content", 0, 0, width - 20, height); scroll.viewport = viewport; scroll.content = content; scroll.scrollSensitivity = 40; return content;
        }
    }
}
