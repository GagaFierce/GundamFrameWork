using System;
using TMPro;
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
            AudioClip click = CreateClickClip();
            UiPanelDefinitionAsset definition = CreatePanelDefinition();
            UiPanelDefinitionAsset helpDefinition = CreateHelpPanelDefinition();
            UiPanelDefinitionAsset aboutDefinition = CreateAboutPanelDefinition();
            UiPanelDefinitionAsset menuDefinition = CreateMenuPanelDefinition();
            CombinedSampleConfig config = CreateConfig();
            GameObject help = CreateHelpPrefab();
            GameObject about = CreateAboutPrefab();
            GameObject dialog = CreateDialogPrefab();
            UiPanelDefinitionAsset dialogDefinition = CreateDialogPanelDefinition();
            CreateScene(player, settings, menu, help, about, dialog, definition, helpDefinition, aboutDefinition, dialogDefinition, menuDefinition, config);
            CreateGameplayScene();
            RegisterAddressables(player, settings, menu, help, about, dialog, config, click);
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
            var text = new GameObject("Instructions", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(go.transform, false);
            text.GetComponent<TextMeshProUGUI>().text = "设置\nEscape：关闭\n音量通过 Config/SaveService 保存。";
            var master = new GameObject("MasterVolume", typeof(RectTransform), typeof(Slider)); master.transform.SetParent(go.transform, false);
            var music = new GameObject("MusicVolume", typeof(RectTransform), typeof(Slider)); music.transform.SetParent(go.transform, false);
            var sfx = new GameObject("SfxVolume", typeof(RectTransform), typeof(Slider)); sfx.transform.SetParent(go.transform, false);
            var uiVolume = new GameObject("UiVolume", typeof(RectTransform), typeof(Slider)); uiVolume.transform.SetParent(go.transform, false);
            var toggleObject = new GameObject("Muted", typeof(RectTransform), typeof(Toggle)); toggleObject.transform.SetParent(go.transform, false);
            var applyObject = CreateButton(go.transform, "Apply", "应用");
            var cancelObject = CreateButton(go.transform, "Cancel", "取消");
            var defaultsObject = CreateButton(go.transform, "Defaults", "恢复默认");
            var error = new GameObject("Error", typeof(RectTransform), typeof(TextMeshProUGUI)); error.transform.SetParent(go.transform, false);
            var settingsSerialized = new SerializedObject(go.GetComponent<SampleSettingsPanel>());
            settingsSerialized.FindProperty("masterVolume").objectReferenceValue = master.GetComponent<Slider>();
            settingsSerialized.FindProperty("musicVolume").objectReferenceValue = music.GetComponent<Slider>();
            settingsSerialized.FindProperty("sfxVolume").objectReferenceValue = sfx.GetComponent<Slider>();
            settingsSerialized.FindProperty("uiVolume").objectReferenceValue = uiVolume.GetComponent<Slider>();
            settingsSerialized.FindProperty("muted").objectReferenceValue = toggleObject.GetComponent<Toggle>();
            settingsSerialized.FindProperty("apply").objectReferenceValue = applyObject.GetComponent<Button>();
            settingsSerialized.FindProperty("cancel").objectReferenceValue = cancelObject.GetComponent<Button>();
            settingsSerialized.FindProperty("defaults").objectReferenceValue = defaultsObject.GetComponent<Button>();
            settingsSerialized.FindProperty("errorText").objectReferenceValue = error.GetComponent<TextMeshProUGUI>();
            settingsSerialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateMenuPrefab()
        {
            string path = Prefabs + "/Menu.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var go = new GameObject("Menu", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleMenuPanel));
            var text = new GameObject("Instructions", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(go.transform, false);
            text.GetComponent<TextMeshProUGUI>().text = "GundamFrameWork\nMVVM Combined Sample";
            var button = CreateButton(go.transform, "Start", "开始游戏");
            var settings = CreateButton(go.transform, "Settings", "设置");
            var help = CreateButton(go.transform, "Help", "操作说明");
            var about = CreateButton(go.transform, "About", "关于");
            var exit = CreateButton(go.transform, "Exit", "退出");
            var status = new GameObject("Status", typeof(RectTransform), typeof(TextMeshProUGUI)); status.transform.SetParent(go.transform, false);
            var error = new GameObject("Error", typeof(RectTransform), typeof(TextMeshProUGUI)); error.transform.SetParent(go.transform, false);
            var busy = new GameObject("Busy", typeof(RectTransform), typeof(TextMeshProUGUI)); busy.transform.SetParent(go.transform, false); busy.GetComponent<TextMeshProUGUI>().text = "处理中…"; busy.SetActive(false);
            var serialized = new SerializedObject(go.GetComponent<SampleMenuPanel>());
            serialized.FindProperty("startButton").objectReferenceValue = button.GetComponent<Button>();
            serialized.FindProperty("settingsButton").objectReferenceValue = settings.GetComponent<Button>();
            serialized.FindProperty("helpButton").objectReferenceValue = help.GetComponent<Button>();
            serialized.FindProperty("aboutButton").objectReferenceValue = about.GetComponent<Button>();
            serialized.FindProperty("exitButton").objectReferenceValue = exit.GetComponent<Button>();
            serialized.FindProperty("statusText").objectReferenceValue = status.GetComponent<TextMeshProUGUI>();
            serialized.FindProperty("errorText").objectReferenceValue = error.GetComponent<TextMeshProUGUI>();
            serialized.FindProperty("busyIndicator").objectReferenceValue = busy; serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateHelpPrefab()
        {
            string path = Prefabs + "/Help.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
            var go = new GameObject("Help", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleHelpPanel));
            var content = new GameObject("Content", typeof(RectTransform), typeof(TextMeshProUGUI)); content.transform.SetParent(go.transform, false);
            var serialized = new SerializedObject(go.GetComponent<SampleHelpPanel>()); serialized.FindProperty("content").objectReferenceValue = content.GetComponent<TextMeshProUGUI>(); serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateAboutPrefab()
        {
            string path = Prefabs + "/About.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
            var go = new GameObject("About", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleAboutPanel));
            var name = new GameObject("ProductName", typeof(RectTransform), typeof(TextMeshProUGUI)); name.transform.SetParent(go.transform, false);
            var version = new GameObject("Version", typeof(RectTransform), typeof(TextMeshProUGUI)); version.transform.SetParent(go.transform, false);
            var description = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI)); description.transform.SetParent(go.transform, false);
            var serialized = new SerializedObject(go.GetComponent<SampleAboutPanel>()); serialized.FindProperty("productName").objectReferenceValue = name.GetComponent<TextMeshProUGUI>(); serialized.FindProperty("version").objectReferenceValue = version.GetComponent<TextMeshProUGUI>(); serialized.FindProperty("description").objectReferenceValue = description.GetComponent<TextMeshProUGUI>(); serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateDialogPrefab()
        {
            string path = Prefabs + "/Dialog.prefab"; GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) return existing;
            var go = new GameObject("Dialog", typeof(RectTransform), typeof(CanvasGroup), typeof(SampleDialogPanel));
            var title = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI)); title.transform.SetParent(go.transform, false);
            var message = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI)); message.transform.SetParent(go.transform, false);
            var confirm = CreateButton(go.transform, "Confirm", "确认"); var cancel = CreateButton(go.transform, "Cancel", "取消"); var alternative = CreateButton(go.transform, "Alternative", "其他");
            var confirmLabel = confirm.transform.Find("Label").GetComponent<TextMeshProUGUI>(); var cancelLabel = cancel.transform.Find("Label").GetComponent<TextMeshProUGUI>(); var alternativeLabel = alternative.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            var serialized = new SerializedObject(go.GetComponent<SampleDialogPanel>());
            serialized.FindProperty("titleText").objectReferenceValue = title.GetComponent<TextMeshProUGUI>(); serialized.FindProperty("messageText").objectReferenceValue = message.GetComponent<TextMeshProUGUI>();
            serialized.FindProperty("confirm").objectReferenceValue = confirm.GetComponent<Button>(); serialized.FindProperty("cancel").objectReferenceValue = cancel.GetComponent<Button>(); serialized.FindProperty("alternative").objectReferenceValue = alternative.GetComponent<Button>();
            serialized.FindProperty("confirmLabel").objectReferenceValue = confirmLabel; serialized.FindProperty("cancelLabel").objectReferenceValue = cancelLabel; serialized.FindProperty("alternativeLabel").objectReferenceValue = alternativeLabel; serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go); return prefab;
        }

        private static GameObject CreateButton(Transform parent, string name, string label)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Button)); button.transform.SetParent(parent, false);
            var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); text.transform.SetParent(button.transform, false); text.GetComponent<TextMeshProUGUI>().text = label;
            return button;
        }

        private static AudioClip CreateClickClip()
        {
            string path = Prefabs + "/Click.asset";
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return existing;
            const int frequency = 44100;
            const int samples = 4410;
            AudioClip clip = AudioClip.Create("GFrameworkSampleClick", samples, 1, frequency, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)frequency;
                data[i] = Mathf.Sin(t * 2 * Mathf.PI * 880) * Mathf.Exp(-t * 24);
            }
            clip.SetData(data, 0); AssetDatabase.CreateAsset(clip, path); return clip;
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

        private static UiPanelDefinitionAsset CreateHelpPanelDefinition()
        { return CreatePanelDefinitionAsset("Help", "GFramework.Samples.Help", UiPanelLayer.Modal, true, Configs + "/HelpPanel.asset"); }

        private static UiPanelDefinitionAsset CreateAboutPanelDefinition()
        { return CreatePanelDefinitionAsset("About", "GFramework.Samples.About", UiPanelLayer.Modal, true, Configs + "/AboutPanel.asset"); }

        private static UiPanelDefinitionAsset CreateDialogPanelDefinition()
        { return CreatePanelDefinitionAsset("Dialog", "GFramework.Samples.Dialog", UiPanelLayer.Modal, true, Configs + "/DialogPanel.asset"); }

        private static UiPanelDefinitionAsset CreatePanelDefinitionAsset(string id, string address, UiPanelLayer layer, bool modal, string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UiPanelDefinitionAsset>(path); if (asset == null) { asset = ScriptableObject.CreateInstance<UiPanelDefinitionAsset>(); AssetDatabase.CreateAsset(asset, path); }
            var serialized = new SerializedObject(asset); serialized.FindProperty("panelId").stringValue = id; serialized.FindProperty("resourceKey").stringValue = address; serialized.FindProperty("layer").enumValueIndex = (int)layer; serialized.FindProperty("modal").boolValue = modal; serialized.ApplyModifiedPropertiesWithoutUndo(); return asset;
        }

        private static CombinedSampleConfig CreateConfig()
        {
            string path = Configs + "/CombinedSampleConfig.asset"; var asset = AssetDatabase.LoadAssetAtPath<CombinedSampleConfig>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<CombinedSampleConfig>(); AssetDatabase.CreateAsset(asset, path); }
            return asset;
        }

        private static void CreateScene(GameObject playerPrefab, GameObject settingsPrefab, GameObject menuPrefab,
            GameObject helpPrefab, GameObject aboutPrefab, GameObject dialogPrefab, UiPanelDefinitionAsset definition,
            UiPanelDefinitionAsset helpDefinition, UiPanelDefinitionAsset aboutDefinition, UiPanelDefinitionAsset dialogDefinition,
            UiPanelDefinitionAsset menuDefinition, CombinedSampleConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
            canvasObject.GetComponent<CanvasScaler>().matchWidthOrHeight = 0.5f;
            var rootObject = new GameObject("UIRoot", typeof(RectTransform), typeof(UIRoot), typeof(SafeAreaLayout)); rootObject.transform.SetParent(canvasObject.transform, false);
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
            uiSerialized.FindProperty("modalBarrier").objectReferenceValue = barrierGroup; uiSerialized.FindProperty("definitions").arraySize = 5;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(0).objectReferenceValue = menuDefinition;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(1).objectReferenceValue = definition;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(2).objectReferenceValue = helpDefinition;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(3).objectReferenceValue = aboutDefinition;
            uiSerialized.FindProperty("definitions").GetArrayElementAtIndex(4).objectReferenceValue = dialogDefinition; uiSerialized.ApplyModifiedPropertiesWithoutUndo();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab); player.name = "Player";
            var audio = new GameObject("AudioServices", typeof(WFrameWork.Audio.Unity.AudioServiceBehaviour));
            var services = new GameObject("RuntimeServices", typeof(CombinedSampleRuntimeServices));
            var serviceSerialized = new SerializedObject(services.GetComponent<CombinedSampleRuntimeServices>());
            serviceSerialized.FindProperty("audioBehaviour").objectReferenceValue = audio.GetComponent<WFrameWork.Audio.Unity.AudioServiceBehaviour>();
            serviceSerialized.FindProperty("ui").objectReferenceValue = uiObject.GetComponent<UiPanelManagerBehaviour>();
            serviceSerialized.FindProperty("player").objectReferenceValue = player.GetComponent<WFrameWork.Physics.Unity.RigidbodyFixedStepBehaviour>();
            serviceSerialized.FindProperty("configuration").objectReferenceValue = config;
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

        private static void RegisterAddressables(GameObject player, GameObject settings, GameObject menu, GameObject help, GameObject about, GameObject dialog, CombinedSampleConfig config, AudioClip click)
        {
            AddressableAssetSettings assetSettings = AddressableAssetSettingsDefaultObject.Settings;
            if (assetSettings == null) { Debug.LogWarning("Addressables Settings not found; generated assets are not registered."); return; }
            AddressableAssetGroup group = assetSettings.FindGroup("GFramework Samples") ?? assetSettings.CreateGroup("GFramework Samples", false, false, true, null, typeof(BundledAssetGroupSchema));
            Register(assetSettings, group, AssetDatabase.GetAssetPath(player), "GFramework.Samples.Player");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(settings), "GFramework.Samples.Settings");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(menu), "GFramework.Samples.Menu");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(help), "GFramework.Samples.Help");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(about), "GFramework.Samples.About");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(dialog), "GFramework.Samples.Dialog");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(config), "GFramework.Samples.Config");
            Register(assetSettings, group, AssetDatabase.GetAssetPath(click), "GFramework.Samples.Click");
            Register(assetSettings, group, Scenes + "/Gameplay.unity", "GFramework.Samples.Game");
        }

        private static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
        { string guid = AssetDatabase.AssetPathToGUID(path); if (string.IsNullOrEmpty(guid)) return; var entry = settings.CreateOrMoveEntry(guid, group); entry.address = address; }
        private static void EnsureFolder(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
    }
}
