using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WFrameWork.UI.Unity;
using WFrameWork.UI;

namespace WFrameWork.Samples.Combined.Editor
{
    public static class CombinedSampleAssetGenerator
    {
        private const string Root = "Assets/GFrameworkSamples/Combined";
        private const string Prefabs = Root + "/Prefabs";
        private const string Configs = Root + "/Configs";
        private const string Scenes = Root + "/Scenes";

        [MenuItem("GFramework/Samples/Generate Combined Sample Assets")]
        public static void Generate()
        {
            EnsureFolder("Assets", "GFrameworkSamples"); EnsureFolder("Assets/GFrameworkSamples", "Combined");
            EnsureFolder(Root, "Prefabs"); EnsureFolder(Root, "Configs"); EnsureFolder(Root, "Scenes");
            GameObject player = CreatePlayerPrefab();
            GameObject settings = CreateSettingsPrefab();
            GameObject menu = CreateMenuPrefab();
            UiPanelDefinitionAsset definition = CreatePanelDefinition();
            UiPanelDefinitionAsset menuDefinition = CreateMenuPanelDefinition();
            CombinedSampleConfig config = CreateConfig();
            CreateScene(player, settings, menu, definition, menuDefinition);
            CreateGameplayScene();
            RegisterAddressables(player, settings, config);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("GFramework combined sample generated under " + Root + ". Build Addressables content before Play.");
        }

        [MenuItem("GFramework/Samples/Build Combined Addressables Content")]
        public static void BuildContent()
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
            { Debug.LogError("Addressables Settings is missing. Create it from Window > Asset Management > Addressables > Groups."); return; }
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error)) Debug.LogError(result.Error); else Debug.Log("GFramework Addressables content built.");
        }

        private static GameObject CreatePlayerPrefab()
        {
            string path = Prefabs + "/Player.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("GFrameworkSamplePlayer"); go.AddComponent<Rigidbody>(); go.AddComponent<WFrameWork.Physics.Unity.RigidbodyFixedStepBehaviour>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateSettingsPrefab()
        {
            string path = Prefabs + "/Settings.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("Settings", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleSettingsPanel));
            var text = new GameObject("Instructions", typeof(RectTransform), typeof(Text)); text.transform.SetParent(go.transform, false);
            text.GetComponent<Text>().text = "Settings\nEscape: close\nVolume is stored in persistentDataPath.";
            var sliderObject = new GameObject("Volume", typeof(RectTransform), typeof(Slider)); sliderObject.transform.SetParent(go.transform, false);
            var toggleObject = new GameObject("Muted", typeof(RectTransform), typeof(Toggle)); toggleObject.transform.SetParent(go.transform, false);
            var saveObject = new GameObject("Save", typeof(RectTransform), typeof(Button)); saveObject.transform.SetParent(go.transform, false);
            var saveLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)); saveLabel.transform.SetParent(saveObject.transform, false); saveLabel.GetComponent<Text>().text = "Save";
            var settingsSerialized = new SerializedObject(go.GetComponent<SampleSettingsPanel>());
            settingsSerialized.FindProperty("volume").objectReferenceValue = sliderObject.GetComponent<Slider>();
            settingsSerialized.FindProperty("muted").objectReferenceValue = toggleObject.GetComponent<Toggle>();
            settingsSerialized.FindProperty("save").objectReferenceValue = saveObject.GetComponent<Button>();
            settingsSerialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateMenuPrefab()
        {
            string path = Prefabs + "/Menu.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("Menu", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleMenuPanel));
            var text = new GameObject("Instructions", typeof(RectTransform), typeof(Text)); text.transform.SetParent(go.transform, false);
            text.GetComponent<Text>().text = "GFramework\nStart Game";
            var button = new GameObject("Start", typeof(RectTransform), typeof(Button)); button.transform.SetParent(go.transform, false);
            var buttonLabel = new GameObject("Label", typeof(RectTransform), typeof(Text)); buttonLabel.transform.SetParent(button.transform, false); buttonLabel.GetComponent<Text>().text = "Start Game";
            var serialized = new SerializedObject(go.GetComponent<SampleMenuPanel>());
            serialized.FindProperty("startButton").objectReferenceValue = button.GetComponent<Button>(); serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static UiPanelDefinitionAsset CreatePanelDefinition()
        {
            string path = Configs + "/SettingsPanel.asset"; var asset = AssetDatabase.LoadAssetAtPath<UiPanelDefinitionAsset>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<UiPanelDefinitionAsset>(); AssetDatabase.CreateAsset(asset, path); }
            var serialized = new SerializedObject(asset); serialized.FindProperty("panelId").stringValue = "Settings";
            serialized.FindProperty("resourceKey").stringValue = "GFramework.Samples.Settings"; serialized.FindProperty("modal").boolValue = true;
            serialized.FindProperty("layer").enumValueIndex = (int)UiPanelLayer.Modal; serialized.ApplyModifiedPropertiesWithoutUndo(); return asset;
        }

        private static UiPanelDefinitionAsset CreateMenuPanelDefinition()
        {
            string path = Configs + "/MenuPanel.asset"; var asset = AssetDatabase.LoadAssetAtPath<UiPanelDefinitionAsset>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<UiPanelDefinitionAsset>(); AssetDatabase.CreateAsset(asset, path); }
            var serialized = new SerializedObject(asset); serialized.FindProperty("panelId").stringValue = "Menu";
            serialized.FindProperty("resourceKey").stringValue = "GFramework.Samples.Menu";
            serialized.FindProperty("layer").enumValueIndex = (int)UiPanelLayer.Normal; serialized.FindProperty("modal").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo(); return asset;
        }

        private static CombinedSampleConfig CreateConfig()
        {
            string path = Configs + "/CombinedSampleConfig.asset"; var asset = AssetDatabase.LoadAssetAtPath<CombinedSampleConfig>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<CombinedSampleConfig>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }

        private static void CreateScene(GameObject playerPrefab, GameObject settingsPrefab, GameObject menuPrefab,
            UiPanelDefinitionAsset definition, UiPanelDefinitionAsset menuDefinition)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var rootObject = new GameObject("UIRoot", typeof(RectTransform), typeof(UIRoot)); rootObject.transform.SetParent(canvasObject.transform, false);
            var root = rootObject.GetComponent<UIRoot>(); RectTransform[] layers = new RectTransform[4];
            for (int i = 0; i < layers.Length; i++) { var layer = new GameObject(((UiPanelLayer)i).ToString(), typeof(RectTransform)); layer.transform.SetParent(rootObject.transform, false); layers[i] = layer.GetComponent<RectTransform>(); }
            var rootSerialized = new SerializedObject(root); rootSerialized.FindProperty("canvas").objectReferenceValue = canvasObject.GetComponent<Canvas>();
            rootSerialized.FindProperty("hudLayer").objectReferenceValue = layers[0]; rootSerialized.FindProperty("normalLayer").objectReferenceValue = layers[1];
            rootSerialized.FindProperty("modalLayer").objectReferenceValue = layers[2]; rootSerialized.FindProperty("overlayLayer").objectReferenceValue = layers[3]; rootSerialized.ApplyModifiedPropertiesWithoutUndo();
            var barrier = new GameObject("ModalBarrier", typeof(RectTransform), typeof(CanvasGroup), typeof(Image)); barrier.transform.SetParent(layers[2], false);
            var barrierRect = barrier.GetComponent<RectTransform>(); barrierRect.anchorMin = Vector2.zero; barrierRect.anchorMax = Vector2.one; barrierRect.offsetMin = Vector2.zero; barrierRect.offsetMax = Vector2.zero;
            var barrierGroup = barrier.GetComponent<CanvasGroup>(); barrierGroup.alpha = 0; barrierGroup.blocksRaycasts = false; barrierGroup.interactable = false; barrier.GetComponent<Image>().color = new Color(0, 0, 0, 0.45f);
            var uiObject = new GameObject("UiPanelManager", typeof(UiPanelManagerBehaviour)); uiObject.transform.SetParent(rootObject.transform, false);
            var uiSerialized = new SerializedObject(uiObject.GetComponent<UiPanelManagerBehaviour>()); uiSerialized.FindProperty("root").objectReferenceValue = root;
            uiSerialized.FindProperty("modalBarrier").objectReferenceValue = barrierGroup; uiSerialized.FindProperty("definitions").arraySize = 2;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(0).objectReferenceValue = menuDefinition;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(1).objectReferenceValue = definition; uiSerialized.ApplyModifiedPropertiesWithoutUndo();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab); player.name = "Player";
            var audio = new GameObject("AudioServices", typeof(WFrameWork.Audio.Unity.AudioServiceBehaviour));
            var services = new GameObject("RuntimeServices", typeof(CombinedSampleRuntimeServices));
            var serviceSerialized = new SerializedObject(services.GetComponent<CombinedSampleRuntimeServices>());
            serviceSerialized.FindProperty("audioBehaviour").objectReferenceValue = audio.GetComponent<WFrameWork.Audio.Unity.AudioServiceBehaviour>();
            serviceSerialized.FindProperty("ui").objectReferenceValue = uiObject.GetComponent<UiPanelManagerBehaviour>();
            serviceSerialized.FindProperty("player").objectReferenceValue = player.GetComponent<WFrameWork.Physics.Unity.RigidbodyFixedStepBehaviour>();
            serviceSerialized.FindProperty("poolRoot").objectReferenceValue = rootObject.transform; serviceSerialized.ApplyModifiedPropertiesWithoutUndo();
            var bootstrap = new GameObject("CombinedSampleBootstrap", typeof(CombinedSampleBootstrap)); var bootstrapSerialized = new SerializedObject(bootstrap.GetComponent<CombinedSampleBootstrap>());
            bootstrapSerialized.FindProperty("services").objectReferenceValue = services.GetComponent<CombinedSampleRuntimeServices>(); bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, Scenes + "/CombinedSample.unity");
        }

        private static void CreateGameplayScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("GFrameworkGameplayContent");
            EditorSceneManager.SaveScene(scene, Scenes + "/Gameplay.unity");
            EditorSceneManager.OpenScene(Scenes + "/CombinedSample.unity", OpenSceneMode.Single);
        }

        private static void RegisterAddressables(GameObject player, GameObject settings, CombinedSampleConfig config)
        {
            AddressableAssetSettings assetSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (assetSettings == null) { Debug.LogWarning("Addressables Settings not found; generated assets are not registered."); return; }
            AddressableAssetGroup group = assetSettings.FindGroup("GFramework Samples") ?? assetSettings.CreateGroup("GFramework Samples", false, false, true, null, typeof(BundledAssetGroupSchema));
            Register(assetSettings, group, AssetDatabase.GetAssetPath(player), "GFramework.Samples.Player");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(settings), "GFramework.Samples.Settings");
            GameObject menu = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "/Menu.prefab");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(menu), "GFramework.Samples.Menu");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(config), "GFramework.Samples.Config");
            Register(assetSettings, group, Scenes + "/Gameplay.unity", "GFramework.Samples.Game");
        }

        private static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
        { string guid = AssetDatabase.AssetPathToGUID(path); if (string.IsNullOrEmpty(guid)) return; var entry = settings.CreateOrMoveEntry(guid, group); entry.address = address; }
        private static void EnsureFolder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
    }
}
