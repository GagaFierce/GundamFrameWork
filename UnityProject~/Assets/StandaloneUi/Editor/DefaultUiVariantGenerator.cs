using System;
using System.Collections.Generic;
using GFramework.Starter.DefaultUi;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GFramework.Starter.DefaultUi.Editor
{
    public static class DefaultUiVariantGenerator
    {
        private const string Root = "Assets/GFrameworkStarter/Demo/Variants";

        private struct UiProfile
        {
            public string Id;
            public string DisplayName;
            public Vector2 ReferenceResolution;
            public float CardWidth;
            public float CardHeight;
            public float TitleSize;
            public float BodySize;
            public float ButtonHeight;
            public Color Background;
            public Color Card;
            public Color Button;

            public UiProfile(string id, string displayName, Vector2 referenceResolution,
                float cardWidth, float cardHeight, float titleSize, float bodySize,
                float buttonHeight, Color background, Color card, Color button)
            {
                Id = id;
                DisplayName = displayName;
                ReferenceResolution = referenceResolution;
                CardWidth = cardWidth;
                CardHeight = cardHeight;
                TitleSize = titleSize;
                BodySize = bodySize;
                ButtonHeight = buttonHeight;
                Background = background;
                Card = card;
                Button = button;
            }
        }

        [MenuItem("GFramework/Standalone UI/Generate Mobile + Desktop UI")]
        public static void Generate()
        {
            UiProfile mobile = new UiProfile(
                "Mobile", "手游竖屏", new Vector2(1080f, 1920f),
                900f, 1320f, 44f, 25f, 78f,
                new Color(0.025f, 0.04f, 0.075f, 1f),
                new Color(0.07f, 0.1f, 0.17f, 0.98f),
                new Color(0.1f, 0.32f, 0.58f, 1f));
            UiProfile desktop = new UiProfile(
                "Desktop", "端游横屏", new Vector2(1920f, 1080f),
                1180f, 720f, 38f, 21f, 58f,
                new Color(0.025f, 0.035f, 0.06f, 1f),
                new Color(0.07f, 0.095f, 0.15f, 0.98f),
                new Color(0.1f, 0.3f, 0.55f, 1f));

            string mobileScene = GenerateVariant(mobile);
            string desktopScene = GenerateVariant(desktop);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(mobileScene, true),
                new EditorBuildSettingsScene(desktopScene, true)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(mobileScene, OpenSceneMode.Single);
            Debug.Log("Generated default UI variants: Mobile and Desktop.");
        }

        private static string GenerateVariant(UiProfile profile)
        {
            string variantRoot = Root + "/" + profile.Id;
            string prefabRoot = variantRoot + "/Prefabs";
            string sceneRoot = variantRoot + "/Scenes";
            EnsureFolder("Assets", "GFrameworkStarter");
            EnsureFolder("Assets/GFrameworkStarter", "Demo");
            EnsureFolder("Assets/GFrameworkStarter/Demo", "Variants");
            EnsureFolder(Root, profile.Id);
            EnsureFolder(variantRoot, "Prefabs");
            EnsureFolder(variantRoot, "Scenes");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = profile.ReferenceResolution;
            scaler.matchWidthOrHeight = profile.Id == "Mobile" ? 0f : 0.5f;

            GameObject rootObject = new GameObject("UIRoot", typeof(RectTransform),
                typeof(DefaultUiRoot), typeof(DefaultUiSafeArea));
            rootObject.transform.SetParent(canvasObject.transform, false);
            Stretch(rootObject.GetComponent<RectTransform>());
            RectTransform normal = CreateLayer(rootObject.transform, "Normal");
            RectTransform modal = CreateLayer(rootObject.transform, "Modal");
            RectTransform overlay = CreateLayer(rootObject.transform, "Overlay");

            GameObject main = CreatePanel(profile, normal, "Main", "主界面", "欢迎进入基础 UI 框架");
            AddButton(profile, main.transform, "Lobby", "进入大厅", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Lobby);
            AddButton(profile, main.transform, "Settings", "设置", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Settings);
            AddButton(profile, main.transform, "Dialog", "打开弹窗", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Dialog);
            AddButton(profile, main.transform, "Toast", "显示提示", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Toast);

            GameObject lobby = CreatePanel(profile, normal, "Lobby", "大厅界面", "这里放角色、房间、任务和开始游戏入口");
            AddButton(profile, lobby.transform, "Start", "开始游戏", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Toast);
            AddButton(profile, lobby.transform, "LobbySettings", "大厅设置", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Settings);
            AddButton(profile, lobby.transform, "LobbyBack", "返回主界面", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Main);

            GameObject settings = CreatePanel(profile, normal, "Settings", "设置界面", "音频、画面、操作和账号设置的基础入口");
            AddButton(profile, settings.transform, "Graphics", "画面设置", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Toast);
            AddButton(profile, settings.transform, "Audio", "音频设置", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Toast);
            AddButton(profile, settings.transform, "SettingsBack", "返回主界面", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Main);

            GameObject dialog = CreatePanel(profile, modal, "Dialog", "确认弹窗", "这里是可复用的 Modal Dialog 基础样式");
            AddButton(profile, dialog.transform, "Confirm", "确认", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Main);
            AddButton(profile, dialog.transform, "Cancel", "取消", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Main);

            GameObject toast = CreatePanel(profile, overlay, "Toast", "提示", "操作已完成");
            AddButton(profile, toast.transform, "ToastClose", "关闭", rootObject.GetComponent<DefaultUiRoot>(), DefaultUiScreen.Main);

            DefaultUiRoot uiRoot = rootObject.GetComponent<DefaultUiRoot>();
            uiRoot.Configure(main, lobby, settings, dialog, toast);
            SavePrefab(main, prefabRoot + "/Main.prefab");
            SavePrefab(lobby, prefabRoot + "/Lobby.prefab");
            SavePrefab(settings, prefabRoot + "/Settings.prefab");
            SavePrefab(dialog, prefabRoot + "/Dialog.prefab");
            SavePrefab(toast, prefabRoot + "/Toast.prefab");

            string scenePath = sceneRoot + "/" + profile.Id + "UI.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            return scenePath;
        }

        private static GameObject CreatePanel(UiProfile profile, Transform parent, string name,
            string title, string body)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(profile.CardWidth, profile.CardHeight);
            Image image = panel.GetComponent<Image>();
            image.color = profile.Card;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 34, 34);
            layout.spacing = profile.Id == "Mobile" ? 18f : 12f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            CreateText(panel.transform, title, (int)profile.TitleSize, profile.ButtonHeight, Color.white);
            CreateText(panel.transform, body, (int)profile.BodySize, profile.ButtonHeight * 1.25f, new Color(0.78f, 0.84f, 0.92f, 1f));
            return panel;
        }

        private static void AddButton(UiProfile profile, Transform parent, string name, string label,
            DefaultUiRoot root, DefaultUiScreen target)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(LayoutElement), typeof(DefaultUiButton));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<LayoutElement>().preferredHeight = profile.ButtonHeight;
            Image image = buttonObject.GetComponent<Image>();
            image.color = profile.Button;
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.colors = new ColorBlock
            {
                normalColor = profile.Button,
                highlightedColor = Color.Lerp(profile.Button, Color.white, 0.18f),
                pressedColor = Color.Lerp(profile.Button, Color.black, 0.18f),
                selectedColor = profile.Button,
                disabledColor = Color.gray,
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            buttonObject.GetComponent<DefaultUiButton>().Configure(root, target);
            Text text = CreateText(buttonObject.transform, label, (int)profile.BodySize, profile.ButtonHeight, Color.white);
            Stretch(text.rectTransform);
        }

        private static Text CreateText(Transform parent, string value, int size, float height, Color color)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            textObject.GetComponent<LayoutElement>().preferredHeight = height;
            return text;
        }

        private static RectTransform CreateLayer(Transform parent, string name)
        {
            GameObject layer = new GameObject(name, typeof(RectTransform));
            layer.transform.SetParent(parent, false);
            RectTransform rect = layer.GetComponent<RectTransform>();
            Stretch(rect);
            return rect;
        }

        private static void SavePrefab(GameObject instance, string path)
        {
            PrefabUtility.SaveAsPrefabAssetAndConnect(instance, path, InteractionMode.AutomatedAction);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
        }
    }
}
