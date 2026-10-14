using UnityEngine;
using UnityEngine.UI;

namespace GFramework.Starter.DefaultUi
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class DefaultUiSafeArea : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            ApplySafeArea(true);
        }

        private void OnEnable()
        {
            ApplySafeArea(true);
        }

        private void Update()
        {
            ApplySafeArea(false);
        }

        private void ApplySafeArea(bool force)
        {
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0) return;

            Rect safeArea = Screen.safeArea;
            if (!force && safeArea == _lastSafeArea && width == _lastScreenWidth && height == _lastScreenHeight)
                return;

            _lastSafeArea = safeArea;
            _lastScreenWidth = width;
            _lastScreenHeight = height;
            _rectTransform.anchorMin = new Vector2(safeArea.xMin / width, safeArea.yMin / height);
            _rectTransform.anchorMax = new Vector2(safeArea.xMax / width, safeArea.yMax / height);
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }
    }

    public enum DefaultUiScreen
    {
        Main,
        Lobby,
        Settings,
        Dialog,
        Toast
    }

    public sealed class DefaultUiRoot : MonoBehaviour
    {
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject dialogPanel;
        [SerializeField] private GameObject toastPanel;

        public void Configure(GameObject main, GameObject lobby, GameObject settings,
            GameObject dialog, GameObject toast)
        {
            mainPanel = main;
            lobbyPanel = lobby;
            settingsPanel = settings;
            dialogPanel = dialog;
            toastPanel = toast;
        }

        private void Awake()
        {
            Show(DefaultUiScreen.Main);
        }

        public void Show(DefaultUiScreen screen)
        {
            CancelInvoke(nameof(ShowMain));
            if (mainPanel != null) mainPanel.SetActive(screen == DefaultUiScreen.Main);
            if (lobbyPanel != null) lobbyPanel.SetActive(screen == DefaultUiScreen.Lobby);
            if (settingsPanel != null) settingsPanel.SetActive(screen == DefaultUiScreen.Settings);
            if (dialogPanel != null) dialogPanel.SetActive(screen == DefaultUiScreen.Dialog);
            if (toastPanel != null) toastPanel.SetActive(screen == DefaultUiScreen.Toast);
            if (screen == DefaultUiScreen.Toast) Invoke(nameof(ShowMain), 2.5f);
        }

        public void ShowMain() => Show(DefaultUiScreen.Main);
        public void ShowLobby() => Show(DefaultUiScreen.Lobby);
        public void ShowSettings() => Show(DefaultUiScreen.Settings);
        public void ShowDialog() => Show(DefaultUiScreen.Dialog);
        public void ShowToast() => Show(DefaultUiScreen.Toast);
    }

    [RequireComponent(typeof(Button))]
    public sealed class DefaultUiButton : MonoBehaviour
    {
        [SerializeField] private DefaultUiRoot root;
        [SerializeField] private DefaultUiScreen target;
        private Button _button;

        public void Configure(DefaultUiRoot owner, DefaultUiScreen destination)
        {
            root = owner;
            target = destination;
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }

        private void HandleClick()
        {
            if (root != null) root.Show(target);
        }
    }
}
