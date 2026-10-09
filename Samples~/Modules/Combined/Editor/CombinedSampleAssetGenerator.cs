using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;
using WFrameWork.Physics.Unity;

namespace WFrameWork.Samples.Combined.Editor
{
    public static class CombinedSampleAssetGenerator
    {
        private const string Root = "Assets/GFrameworkSamples/Combined";
        private static TMP_FontAsset _font;
        private static readonly Dictionary<string, GameObject> Panels = new Dictionary<string, GameObject>();

        [MenuItem("GFramework/Samples/Generate Combined Sample Assets")]
        public static void Generate()
        {
            Folder("Assets", "GFrameworkSamples"); Folder("Assets/GFrameworkSamples", "Combined");
            foreach (string name in new[] { "Prefabs", "Configs", "Scenes" }) Folder(Root, name);
            var config = Asset<CombinedSampleConfig>(Root + "/Configs/CombinedSampleConfig.asset");
            _font = config.UiFont != null ? config.UiFont : TMP_Settings.defaultFontAsset;
            if (_font == null) throw new InvalidOperationException("Import TMP Essential Resources and assign a TMP font to CombinedSampleConfig.UiFont before generation.");
            if (!_font.HasCharacters("大厅设置暂停加载结算确认取消")) Debug.LogWarning("The selected TMP font does not cover Chinese labels. Assign a CJK TMP font to CombinedSampleConfig.UiFont and regenerate.");
            Panels.Clear();
            BuildMenu(); BuildSettings(); BuildPause(); BuildLoading(); BuildResult(); BuildHelp(); BuildAbout(); BuildDialog(); BuildToast();
            var definitions = new List<UiPanelDefinitionAsset>();
            foreach (var pair in Panels)
            {
                var definition = Asset<UiPanelDefinitionAsset>(Root + "/Configs/" + pair.Key + "Panel.asset");
                Set(definition, "panelId", pair.Key); Set(definition, "resourceKey", "GFramework.Samples." + pair.Key);
                Set(definition, "layer", (int)(pair.Key == "Menu" ? UiPanelLayer.Normal : pair.Key == "Toast" ? UiPanelLayer.Overlay : UiPanelLayer.Modal));
                Set(definition, "modal", pair.Key != "Menu" && pair.Key != "Toast"); definitions.Add(definition);
            }
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule); player.name = "Player";
            player.AddComponent<Rigidbody>(); player.AddComponent<RigidbodyFixedStepBehaviour>();
            var playerPrefab = PrefabUtility.SaveAsPrefabAsset(player, Root + "/Prefabs/Player.prefab"); UnityEngine.Object.DestroyImmediate(player);
            CreateScene(definitions, config);
            CreateGameplayScene();
            RegisterContent(config, playerPrefab, CreateSound());
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("Generated MVVM UI under " + Root + ". Existing generated UI prefabs are rebuilt with stable asset GUIDs. Validate and build local Addressables content before Play.");
        }

        [MenuItem("GFramework/Samples/Build Combined Addressables Content")]
        public static void BuildContent()
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null) throw new InvalidOperationException("Create host Addressables Settings first.");
            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (!string.IsNullOrEmpty(result.Error)) throw new InvalidOperationException(result.Error);
            Debug.Log("GFramework Addressables content built.");
        }

        private static GameObject Panel(string id, Type view, string title)
        {
            var go = new GameObject(id, typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.AddComponent(view); var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(620, 700);
            go.GetComponent<Image>().color = new Color(.07f, .09f, .15f, .98f);
            var layout = go.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(24, 24, 20, 20); layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (!string.IsNullOrEmpty(title)) Text(go.transform, "Heading", title, 34, 48).alignment = TextAlignmentOptions.Center;
            return go;
        }
        private static void Save(string id, GameObject go)
        { Panels[id] = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/" + id + ".prefab"); UnityEngine.Object.DestroyImmediate(go); }
        private static TMP_Text Text(Transform parent, string name, string value, float size = 24, float height = 36)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>(); text.font = _font; text.text = value; text.fontSize = size; text.color = Color.white;
            text.enableWordWrapping = true; text.raycastTarget = false; go.GetComponent<LayoutElement>().preferredHeight = height; return text;
        }
        private static Button Button(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement)); go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(.15f, .32f, .55f); go.GetComponent<LayoutElement>().preferredHeight = 44;
            var button = go.GetComponent<Button>(); button.targetGraphic = go.GetComponent<Image>();
            var text = Text(go.transform, "Label", label, 24); Stretch(text.rectTransform); text.alignment = TextAlignmentOptions.Center; return button;
        }
        private static RectTransform Rect(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>(); }
        private static Image Image(Transform parent, string name, Color color)
        { var rect = Rect(parent, name); var image = rect.gameObject.AddComponent<Image>(); image.color = color; return image; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static Slider Slider(Transform parent, string name, string label)
        {
            Text(parent, name + "Label", label, 22, 28);
            var rect = Rect(parent, name); var background = rect.gameObject.AddComponent<Image>(); background.color = new Color(.2f, .23f, .3f);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
            var fill = Image(rect, "Fill", new Color(.2f, .65f, .8f)); Stretch(fill.rectTransform); fill.raycastTarget = false;
            var handleArea = Rect(rect, "HandleArea"); Stretch(handleArea); handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
            var handle = Image(handleArea, "Handle", Color.white); handle.rectTransform.sizeDelta = new Vector2(20, 32); handle.raycastTarget = false;
            var slider = rect.gameObject.AddComponent<Slider>(); slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.minValue = 0; slider.maxValue = 1; slider.SetValueWithoutNotify(1); return slider;
        }
        private static Toggle Toggle(Transform parent, string name, string label)
        {
            var rect = Rect(parent, name); rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 36;
            var hit = rect.gameObject.AddComponent<Image>(); hit.color = new Color(0, 0, 0, .01f);
            var box = Image(rect, "Box", new Color(.2f, .23f, .3f)); box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0, .5f); box.rectTransform.pivot = new Vector2(0, .5f); box.rectTransform.sizeDelta = new Vector2(28, 28);
            var check = Image(box.transform, "Checkmark", new Color(.25f, .8f, .8f)); Stretch(check.rectTransform); check.rectTransform.offsetMin = Vector2.one * 5; check.rectTransform.offsetMax = -Vector2.one * 5;
            var text = Text(rect, "Label", label); Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(40, 0);
            var toggle = rect.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = box; toggle.graphic = check; return toggle;
        }
        private static TMP_Dropdown Dropdown(Transform parent, string name, string label)
        {
            Text(parent, name + "Label", label, 22, 28);
            var rect = Rect(parent, name); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.15f, .25f, .35f);
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            var caption = Text(rect, "Caption", ""); Stretch(caption.rectTransform); caption.rectTransform.offsetMin = new Vector2(10, 0);
            var template = Rect(rect, "Template"); template.anchorMin = Vector2.zero; template.anchorMax = Vector2.right; template.pivot = new Vector2(.5f, 1); template.sizeDelta = new Vector2(0, 200);
            template.gameObject.AddComponent<Image>().color = new Color(.1f, .15f, .23f);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Rect(template, "Viewport"); Stretch(viewport); viewport.gameObject.AddComponent<Image>(); viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var content = Rect(viewport, "Content"); content.anchorMin = Vector2.up; content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 36);
            var item = Toggle(content, "Item", "Option"); var itemRect = item.GetComponent<RectTransform>(); itemRect.anchorMin = new Vector2(0, .5f); itemRect.anchorMax = new Vector2(1, .5f); itemRect.sizeDelta = new Vector2(0, 36);
            var dropdown = rect.gameObject.AddComponent<TMP_Dropdown>(); dropdown.targetGraphic = image; dropdown.captionText = caption;
            dropdown.template = template; dropdown.itemText = item.transform.Find("Label").GetComponent<TMP_Text>();
            scroll.viewport = viewport; scroll.content = content; template.gameObject.SetActive(false); return dropdown;
        }
        private static void Bind(GameObject go, string field, UnityEngine.Object value) => Set(go.GetComponent<MonoBehaviour>(), field, value);
        private static void Set(UnityEngine.Object target, string field, object value)
        {
            var serialized = new SerializedObject(target); var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException(target.name + "." + field + " is missing.");
            if (value is UnityEngine.Object reference) property.objectReferenceValue = reference;
            else if (value is string text) property.stringValue = text;
            else if (value is bool boolean) property.boolValue = boolean;
            else if (value is int number) { if (property.propertyType == SerializedPropertyType.Enum) property.enumValueIndex = number; else property.intValue = number; }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Wire(GameObject go, string field, UnityEngine.Object value)
        {
            foreach (var component in go.GetComponents<MonoBehaviour>())
            {
                var serialized = new SerializedObject(component);
                if (serialized.FindProperty(field) != null) { Set(component, field, value); return; }
            }
            throw new InvalidOperationException(go.name + "." + field + " is missing.");
        }
        private static void BuildMenu()
        {
            var go = Panel("Menu", typeof(SampleMenuPanel), "大厅");
            foreach (var pair in new[] { ("startButton", "开始游戏"), ("settingsButton", "设置"), ("helpButton", "操作说明"), ("aboutButton", "关于"), ("exitButton", "退出") }) Wire(go, pair.Item1, Button(go.transform, pair.Item1, pair.Item2));
            Wire(go, "statusText", Text(go.transform, "Status", "")); Wire(go, "errorText", Text(go.transform, "Error", "", 20, 48));
            var busy = Text(go.transform, "Busy", "处理中…").gameObject; Wire(go, "busyIndicator", busy); busy.SetActive(false); Save("Menu", go);
        }
        private static void BuildSettings()
        {
            var go = Panel("Settings", typeof(SampleSettingsPanel), "设置");
            foreach (var pair in new[] { ("masterVolume", "主音量"), ("musicVolume", "音乐音量"), ("sfxVolume", "音效音量"), ("uiVolume", "界面音量") }) Wire(go, pair.Item1, Slider(go.transform, pair.Item1, pair.Item2));
            Wire(go, "muted", Toggle(go.transform, "Muted", "静音"));
            Wire(go, "windowMode", Dropdown(go.transform, "WindowMode", "显示模式")); Wire(go, "resolution", Dropdown(go.transform, "Resolution", "分辨率")); Wire(go, "quality", Dropdown(go.transform, "Quality", "画质"));
            Wire(go, "apply", Button(go.transform, "Apply", "应用")); Wire(go, "cancel", Button(go.transform, "Cancel", "放弃修改并返回")); Wire(go, "defaults", Button(go.transform, "Defaults", "恢复默认"));
            Wire(go, "errorText", Text(go.transform, "Error", "", 18, 48)); Save("Settings", go);
        }
        private static void BuildPause()
        {
            var go = Panel("Pause", typeof(SamplePausePanel), "暂停");
            foreach (var pair in new[] { ("resume", "继续游戏"), ("settings", "设置"), ("help", "操作说明"), ("restart", "重新开始"), ("lobby", "返回大厅") }) Wire(go, pair.Item1, Button(go.transform, pair.Item1, pair.Item2));
            Wire(go, "error", Text(go.transform, "Error", "", 20, 48)); Save("Pause", go);
        }
        private static void BuildLoading()
        {
            var go = Panel("Loading", typeof(SampleLoadingPanel), "加载中"); Wire(go, "stage", Text(go.transform, "Stage", "准备中")); Wire(go, "error", Text(go.transform, "Error", "", 20, 64));
            var progress = Slider(go.transform, "Progress", "进度"); progress.interactable = false; Wire(go, "progress", progress);
            Wire(go, "cancel", Button(go.transform, "Cancel", "取消加载")); Wire(go, "retry", Button(go.transform, "Retry", "重试")); Wire(go, "back", Button(go.transform, "Back", "返回")); Save("Loading", go);
        }
        private static void BuildResult()
        {
            var go = Panel("Result", typeof(SampleResultPanel), ""); Wire(go, "title", Text(go.transform, "Title", "结算", 34, 48)); Wire(go, "description", Text(go.transform, "Description", "", 24, 64)); Wire(go, "statistics", Text(go.transform, "Statistics", "", 22, 80));
            Wire(go, "restart", Button(go.transform, "Restart", "重新开始")); Wire(go, "lobby", Button(go.transform, "Lobby", "返回大厅")); Wire(go, "next", Button(go.transform, "Next", "下一关")); Save("Result", go);
        }
        private static void BuildHelp()
        { var go = Panel("Help", typeof(SampleHelpPanel), "操作说明"); Wire(go, "content", Text(go.transform, "Content", "", 24, 260)); Wire(go, "close", Button(go.transform, "Close", "返回")); Save("Help", go); }
        private static void BuildAbout()
        { var go = Panel("About", typeof(SampleAboutPanel), "关于"); Wire(go, "productName", Text(go.transform, "Name", "")); Wire(go, "version", Text(go.transform, "Version", "")); Wire(go, "description", Text(go.transform, "Description", "", 24, 120)); Wire(go, "close", Button(go.transform, "Close", "返回")); Save("About", go); }
        private static void BuildDialog()
        {
            var go = Panel("Dialog", typeof(SampleDialogPanel), ""); Wire(go, "titleText", Text(go.transform, "Title", "", 30, 48)); Wire(go, "messageText", Text(go.transform, "Message", "", 24, 96));
            foreach (string name in new[] { "confirm", "cancel", "alternative" }) { var button = Button(go.transform, name, name); Wire(go, name, button); Wire(go, name + "Label", button.transform.Find("Label").GetComponent<TMP_Text>()); }
            Save("Dialog", go);
        }
        private static void BuildToast()
        {
            var go = Panel("Toast", typeof(SampleToastPanel), ""); var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .92f);
            Wire(go, "message", Text(go.transform, "Message", "", 24, 48)); go.GetComponent<CanvasGroup>().blocksRaycasts = false; Save("Toast", go);
        }
        private static void CreateScene(List<UiPanelDefinitionAsset> definitions, CombinedSampleConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 3, -8); camera.transform.LookAt(Vector3.up);
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var rootRect = Rect(canvas.transform, "UIRoot"); Stretch(rootRect); var root = rootRect.gameObject.AddComponent<UIRoot>(); rootRect.gameObject.AddComponent<SafeAreaLayout>(); Set(root, "canvas", canvas.GetComponent<Canvas>());
            var layers = new RectTransform[4]; string[] fields = { "hudLayer", "normalLayer", "modalLayer", "overlayLayer" };
            for (int i = 0; i < 4; i++) { layers[i] = Rect(rootRect, ((UiPanelLayer)i).ToString()); Stretch(layers[i]); Set(root, fields[i], layers[i]); }
            var barrier = Image(layers[2], "ModalBarrier", new Color(0, 0, 0, .5f)); Stretch(barrier.rectTransform); var barrierGroup = barrier.gameObject.AddComponent<CanvasGroup>(); barrierGroup.alpha = 0; barrierGroup.blocksRaycasts = barrierGroup.interactable = false;
            var ui = new GameObject("UI Manager").AddComponent<UiPanelManagerBehaviour>(); ui.transform.SetParent(rootRect, false); Set(ui, "root", root); Set(ui, "modalBarrier", barrierGroup);
            var serializedUi = new SerializedObject(ui); var items = serializedUi.FindProperty("definitions"); items.arraySize = definitions.Count; for (int i = 0; i < definitions.Count; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i]; serializedUi.ApplyModifiedPropertiesWithoutUndo();
            var audio = new GameObject("Audio").AddComponent<WFrameWork.Audio.Unity.AudioServiceBehaviour>();
            var services = new GameObject("Runtime").AddComponent<CombinedSampleRuntimeServices>(); Set(services, "audioBehaviour", audio); Set(services, "ui", ui); Set(services, "configuration", config); Set(services, "poolRoot", new GameObject("PlayerPool").transform);
            var bootstrap = new GameObject("Input Routes").AddComponent<CombinedSampleBootstrap>(); Set(bootstrap, "services", services);
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/CombinedSample.unity");
        }
        private static void CreateGameplayScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Ground"; ground.transform.position = Vector3.down;
            new GameObject("Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
            EditorSceneManager.SaveScene(scene, Root + "/Scenes/Gameplay.unity"); EditorSceneManager.OpenScene(Root + "/Scenes/CombinedSample.unity");
        }
        private static AudioClip CreateSound()
        {
            string path = Root + "/Prefabs/Click.wav";
            if (!File.Exists(path))
            {
                const int count = 4410;
                using (var writer = new BinaryWriter(File.Create(path)))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(44100); writer.Write(88200); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int i = 0; i < count; i++) { float t = i / 44100f; writer.Write((short)(Mathf.Sin(t * Mathf.PI * 1760) * Mathf.Exp(-t * 24) * 12000)); }
                }
            }
            AssetDatabase.ImportAsset(path); return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        private static void RegisterContent(CombinedSampleConfig config, GameObject player, AudioClip sound)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { Debug.LogWarning("Create host Addressables Settings, then regenerate to register content."); return; }
            var group = settings.FindGroup("GFramework Samples") ?? settings.CreateGroup("GFramework Samples", false, false, true, null, typeof(BundledAssetGroupSchema));
            foreach (var pair in Panels) Register(settings, group, AssetDatabase.GetAssetPath(pair.Value), "GFramework.Samples." + pair.Key);
            Register(settings, group, AssetDatabase.GetAssetPath(config), "GFramework.Samples.Config"); Register(settings, group, AssetDatabase.GetAssetPath(player), "GFramework.Samples.Player"); Register(settings, group, AssetDatabase.GetAssetPath(sound), "GFramework.Samples.Click"); Register(settings, group, Root + "/Scenes/Gameplay.unity", "GFramework.Samples.Game");
        }
        private static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string address)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            foreach (var old in new List<AddressableAssetEntry>(group.entries)) if (old.address == address && old.guid != guid) settings.RemoveAssetEntry(old.guid);
            settings.CreateOrMoveEntry(guid, group).address = address;
        }
        private static T Asset<T>(string path) where T : ScriptableObject
        { var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset; asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset; }
        private static void Folder(string parent, string child) { if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child); }
    }
}
