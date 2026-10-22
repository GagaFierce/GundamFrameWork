using UnityEngine;

namespace WFrameWork.Samples.Combined
{
    [CreateAssetMenu(menuName = "GFramework/Starter/Game Configuration")]
    public sealed class CombinedSampleConfig : ScriptableObject
    {
        public string MenuSceneAddress = "GFramework.Starter.Menu";
        public string GameSceneAddress = "GFramework.Starter.Game";
        public string SettingsPanelAddress = "GFramework.Starter.Settings";
        public string HelpPanelAddress = "GFramework.Starter.Help";
        public string AboutPanelAddress = "GFramework.Starter.About";
        public string PlayerPrefabAddress = "GFramework.Starter.Player";
        public string SfxAddress = "GFramework.Starter.Click";
        public string SaveFileName = "gframework-settings.json";
        public int SaveVersion = 2;
        public bool PauseNativeSimulation = true;
        public TMPro.TMP_FontAsset UiFont;
    }
}
