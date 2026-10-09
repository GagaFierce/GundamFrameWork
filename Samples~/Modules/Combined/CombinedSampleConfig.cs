using UnityEngine;

namespace WFrameWork.Samples.Combined
{
    [CreateAssetMenu(menuName = "GFramework/Samples/Combined Configuration")]
    public sealed class CombinedSampleConfig : ScriptableObject
    {
        public string MenuSceneAddress = "GFramework.Samples.Menu";
        public string GameSceneAddress = "GFramework.Samples.Game";
        public string SettingsPanelAddress = "GFramework.Samples.Settings";
        public string HelpPanelAddress = "GFramework.Samples.Help";
        public string AboutPanelAddress = "GFramework.Samples.About";
        public string PlayerPrefabAddress = "GFramework.Samples.Player";
        public string SfxAddress = "GFramework.Samples.Click";
        public string SaveFileName = "gframework-settings.json";
        public int SaveVersion = 1;
    }
}
