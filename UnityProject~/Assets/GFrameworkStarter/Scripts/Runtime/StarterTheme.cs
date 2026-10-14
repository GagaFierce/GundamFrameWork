using UnityEngine;

namespace GFramework.Starter
{
    public enum StarterThemeSlotKind
    {
        Background,
        Panel,
        Button,
        Accent,
        Logo
    }

    [CreateAssetMenu(menuName = "GFramework/Starter/UI Theme", fileName = "StarterTheme")]
    public sealed class StarterTheme : ScriptableObject
    {
        [Header("Colors")]
        [SerializeField] private Color backgroundColor = new Color(0.035f, 0.055f, 0.09f, 1f);
        [SerializeField] private Color panelColor = new Color(0.08f, 0.12f, 0.2f, 0.98f);
        [SerializeField] private Color buttonColor = new Color(0.12f, 0.3f, 0.52f, 1f);
        [SerializeField] private Color accentColor = new Color(0.2f, 0.75f, 0.95f, 1f);
        [SerializeField] private Color textColor = Color.white;

        [Header("Replaceable direct-reference assets")]
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite logoSprite;

        [Header("Animation")]
        [Min(0f)] [SerializeField] private float panelFadeSeconds = 0.16f;
        [SerializeField] private bool animationsEnabled = true;

        public Color TextColor => textColor;
        public float PanelFadeSeconds => animationsEnabled ? panelFadeSeconds : 0f;

        public Color GetColor(StarterThemeSlotKind slot)
        {
            switch (slot)
            {
                case StarterThemeSlotKind.Background: return backgroundColor;
                case StarterThemeSlotKind.Panel: return panelColor;
                case StarterThemeSlotKind.Button: return buttonColor;
                case StarterThemeSlotKind.Accent: return accentColor;
                case StarterThemeSlotKind.Logo: return Color.white;
                default: return Color.white;
            }
        }

        public Sprite GetSprite(StarterThemeSlotKind slot)
        {
            switch (slot)
            {
                case StarterThemeSlotKind.Background: return backgroundSprite;
                case StarterThemeSlotKind.Panel: return panelSprite;
                case StarterThemeSlotKind.Button: return buttonSprite;
                case StarterThemeSlotKind.Logo: return logoSprite;
                default: return null;
            }
        }
    }
}
