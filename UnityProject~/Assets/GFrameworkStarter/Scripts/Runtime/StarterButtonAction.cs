using UnityEngine;
using UnityEngine.UI;

namespace GFramework.Starter
{
    public enum StarterButtonActionKind
    {
        OpenPanel,
        ClosePanel,
        CloseTopModal,
        ToggleTheme,
        ShowToast
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class StarterButtonAction : MonoBehaviour
    {
        [SerializeField] private StarterButtonActionKind action = StarterButtonActionKind.OpenPanel;
        [SerializeField] private string panelId;
        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(InvokeAction);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(InvokeAction);
        }

        private void InvokeAction()
        {
            StarterNavigation navigation = FindObjectOfType<StarterNavigation>();
            StarterRuntimeBootstrap bootstrap = FindObjectOfType<StarterRuntimeBootstrap>();
            if (navigation == null) return;
            switch (action)
            {
                case StarterButtonActionKind.OpenPanel: navigation.OpenPanel(panelId); break;
                case StarterButtonActionKind.ClosePanel: navigation.ClosePanel(panelId); break;
                case StarterButtonActionKind.CloseTopModal: navigation.CloseTopModal(); break;
                case StarterButtonActionKind.ToggleTheme: bootstrap?.ToggleTheme(); break;
                case StarterButtonActionKind.ShowToast: navigation.ShowToast(); break;
            }
        }
    }
}
