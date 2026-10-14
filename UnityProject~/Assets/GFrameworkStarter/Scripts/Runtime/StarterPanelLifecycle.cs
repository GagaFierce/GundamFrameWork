using UnityEngine;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace GFramework.Starter
{
    [DisallowMultipleComponent]
    public sealed class StarterPanelLifecycle : MonoBehaviour, IUGuiPanelLifecycle
    {
        [SerializeField] private CanvasGroup canvasGroup;

        public bool RequiresContinuousUpdate => false;

        public void OnPanelOpened(object argument)
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            StarterThemeRuntime theme = FindObjectOfType<StarterThemeRuntime>();
            theme?.ApplyTo(gameObject);
        }

        public void OnPanelShown() { }
        public void OnPanelHidden() { }
        public void OnPanelUpdate(in UiPanelUpdateContext context) { }

        public void OnPanelClosed()
        {
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }
    }
}
