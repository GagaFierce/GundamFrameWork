# UI module sample

1. 创建一个 Canvas、四个 RectTransform 子节点（HUD、Normal、Modal、Overlay），在根节点添加 `UIRoot` 并填好引用。
2. 场景中保留一个 EventSystem 和项目匹配的 InputModule；模块不会静默创建第二个 EventSystem。
3. 创建 `GFramework/UI/Panel Definition` 资产，Panel Id 填 `Settings`，Resource Key 填宿主 Addressables Group 中的 prefab address；将其加入 `UiPanelManagerBehaviour` 的 Definitions。
4. 在空 GameObject 添加 `UiSampleBootstrap`，把同一对象上的 `UiPanelManagerBehaviour` 拖给 Panel Manager。
