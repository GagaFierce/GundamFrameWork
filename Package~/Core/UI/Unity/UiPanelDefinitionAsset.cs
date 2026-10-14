using UnityEngine;

namespace WFrameWork.UI.Unity
{
    [CreateAssetMenu(menuName = "GFramework/UI/Panel Definition")]
    public sealed class UiPanelDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string panelId;
        [SerializeField] private string resourceKey;
        [SerializeField] private UiPanelLayer layer = UiPanelLayer.Normal;
        [SerializeField] private bool modal;
        [SerializeField] private UiPanelInstanceMode instanceMode = UiPanelInstanceMode.Single;

        public UiPanelDefinition ToDefinition()
        {
            return new UiPanelDefinition(new UiPanelId(panelId), resourceKey, layer, modal, instanceMode);
        }
    }
}
