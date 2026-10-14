using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GFramework.Starter
{
    [DisallowMultipleComponent]
    public sealed class StarterThemeSlot : MonoBehaviour
    {
        [SerializeField] private StarterThemeSlotKind slot = StarterThemeSlotKind.Panel;
        private Image _image;

        public void Apply(StarterTheme theme)
        {
            if (theme == null) return;
            if (_image == null) _image = GetComponent<Image>();
            if (_image == null) return;
            _image.color = theme.GetColor(slot);
            Sprite sprite = theme.GetSprite(slot);
            if (sprite != null || _image.sprite != null) _image.sprite = sprite;
        }
    }

    [DisallowMultipleComponent]
    public sealed class StarterThemeRuntime : MonoBehaviour
    {
        [SerializeField] private StarterTheme primaryTheme;
        [SerializeField] private StarterTheme alternateTheme;
        [SerializeField] private bool useAlternateTheme;

        public StarterTheme CurrentTheme { get; private set; }

        private void Awake()
        {
            CurrentTheme = useAlternateTheme && alternateTheme != null ? alternateTheme : primaryTheme;
            ApplyTo(gameObject);
        }

        public void SetTheme(StarterTheme theme)
        {
            if (theme == null) throw new System.ArgumentNullException(nameof(theme));
            CurrentTheme = theme;
            useAlternateTheme = ReferenceEquals(theme, alternateTheme);
            ApplyTo(gameObject);
        }

        public void ToggleTheme()
        {
            if (primaryTheme == null || alternateTheme == null) return;
            SetTheme(ReferenceEquals(CurrentTheme, primaryTheme) ? alternateTheme : primaryTheme);
        }

        public void ApplyTo(GameObject target)
        {
            if (target == null || CurrentTheme == null) return;
            StarterThemeSlot[] slots = target.GetComponentsInChildren<StarterThemeSlot>(true);
            for (int i = 0; i < slots.Length; i++) slots[i].Apply(CurrentTheme);

            TMP_Text[] texts = target.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++) texts[i].color = CurrentTheme.TextColor;
        }
    }
}
