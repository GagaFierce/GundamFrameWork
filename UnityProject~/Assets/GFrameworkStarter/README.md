# GFramework Unity Starter

这是 GFramework 的 Unity 宿主工程入口。宿主工程通过 `Packages/manifest.json` 的 `file:../../` 引用仓库根目录 UPM 包。

## 首次打开

1. 使用 Unity 2022.3 打开 `UnityProject~`。
2. 等待 Package Manager 完成本地包和 Addressables 解析。
3. 在 Package Manager 中导入 TextMeshPro Essential Resources。
4. 执行菜单：`GFramework > Starter > Generate UI Starter Assets`。
5. 打开生成的场景：`Assets/GFrameworkStarter/Demo/Scenes/GFrameworkStarter.unity`。
6. 运行场景，默认会打开 Menu 页面。

生成器会创建：

- UIRoot、Canvas、CanvasScaler、安全区和四层 UI 层级；
- 一个 EventSystem 和 UI Manager；
- 按类型分类的 Prefab：UI、Character、Environment、Effects、Audio；
- Menu、Settings、Dialog、Toast 页面 Prefab；
- 两个可切换的灰盒主题；
- Panel Definition 和直接引用资源配置；
- 可扩展的主题插槽、按钮动作和导航入口。

资源目录按用途分组：

```text
Assets/GFrameworkStarter/
├─ Framework/
│  ├─ Scripts/Runtime
│  ├─ Scripts/Editor
│  ├─ UI/
│  ├─ Prefabs/UI/Components
│  └─ Configs
└─ Demo/
   ├─ Scenes
   ├─ Prefabs/UI
   ├─ Prefabs/Character
   ├─ Prefabs/Environment
   ├─ Prefabs/Effects
   ├─ Prefabs/Audio
   ├─ Art
   ├─ Animations
   ├─ Materials
   └─ Addressables
```

## 资源替换

固定的小型 UI 资源可以直接放入 `StarterTheme` 的 Sprite 字段。需要独立加载、换皮或热更新的图片，后续应通过主题资源提供者接入 Addressables，并由统一资源服务持有和释放租约，不要在每个 Image 上单独加载资源。

当前生成器先生成直接引用版 Starter，保证在没有远程内容的情况下也能打开和运行。宿主工程已经声明 Addressables 依赖，Addressables 内容组和主题资源接入应在真实资源准备后配置。

## 目录约束

`Demo` 是生成器管理的示例目录。可以修改主题资产和页面内容；如果重新生成，生成器会更新自己的生成物。`Framework` 放通用脚本、UI 资源和可复用组件。不要把 `Library`、`Temp`、`Obj`、日志或构建缓存提交到 Git。
