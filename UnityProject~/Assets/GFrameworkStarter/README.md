# GFramework Unity Starter

这是 GFramework 的 Unity 宿主工程入口。宿主工程通过 `Packages/manifest.json` 的 `file:../../` 引用仓库根目录 UPM 包。

## 首次打开

1. 使用 Unity 2022.3 打开 `UnityProject~`。
2. 等待本地包、Addressables 和 TextMeshPro 完成导入，并导入 TMP Essential Resources。
3. 执行菜单：`GFramework > Starter > Generate Game UI Assets`。
4. 执行菜单：`GFramework > Starter > Validate And Build Addressables`。
5. 打开生成的场景：`Assets/GFrameworkStarter/Demo/Combined/Scenes/GFrameworkStarter.unity`。
6. 运行场景，默认打开大厅菜单。

生成器会创建大厅、设置、操作说明、关于、暂停、Loading、结算、弹窗和 Toast 页面，以及游戏场景、玩家预制体、配置和 Addressables 注册。

生成资源位于：

```text
Assets/GFrameworkStarter/Demo/Combined/
├─ Configs/
├─ Prefabs/
└─ Scenes/
```

## 已包含的流程

- 大厅菜单：开始游戏、设置、操作说明、关于、退出；
- Loading、暂停、结算和返回大厅流程；
- 设置页：音量、静音、显示模式、分辨率、画质、应用、取消和恢复默认；
- 设置预览、显示设置确认/回滚，以及 JSON 存档；
- Canvas、CanvasScaler、安全区、EventSystem、模态阻挡和 uGUI/TMP 绑定。

重新生成会重建该目录下的页面、配置和场景，不会修改其他 Addressables 组。运行前需要先构建本地 Addressables 内容。

## 代码位置

- `Assets/GFrameworkStarter/Scripts/Runtime/Combined/`：运行时服务、面板和输入路由；
- `Assets/GFrameworkStarter/Scripts/Editor/Combined/`：UI/场景生成器和 Addressables 校验器；
- `Assets/GFrameworkStarter/Scripts/Runtime/`：Starter 导航、主题和生命周期组件。

## 资源替换

固定的小型 UI 资源可以直接放入 `StarterTheme` 的 Sprite 字段。需要独立加载、换皮或热更新的图片，后续应通过主题资源提供者接入 Addressables，并由统一资源服务持有和释放租约，不要在每个 Image 上单独加载资源。

Starter 生成器使用宿主工程的 Addressables Settings 注册页面、场景、玩家预制体和音效；宿主工程已经声明 Addressables 依赖，运行前需构建本地内容。

## 目录约束

`Demo` 是生成器管理的示例目录。可以修改主题资产和页面内容；如果重新生成，生成器会更新自己的生成物。`Framework` 放通用脚本、UI 资源和可复用组件。不要把 `Library`、`Temp`、`Obj`、日志或构建缓存提交到 Git。
