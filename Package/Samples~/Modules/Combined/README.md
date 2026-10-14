# Combined modules sample

1. 导入 Sample 后执行 `GFramework/Samples/Generate Combined Sample Assets`，生成 UIRoot、EventSystem、Menu/Settings/Help/About/Dialog prefab、Player prefab、配置、场景和 Addressables 组条目。生成器会设置 CanvasScaler、安全区适配和 TMP 文本引用。
2. 执行 `GFramework/Samples/Validate And Build Combined Addressables`；该入口先检查本 Sample 的地址、资源类型和场景，再构建本地 Addressables 内容。也可分别执行 Validate 和 Build 菜单。
3. 打开 `Assets/GFrameworkSamples/Combined/Scenes/CombinedSample.unity`。`CombinedSampleRuntimeServices` 通过一个 `UnityGameRuntime/GameRuntime` 装配 Addressables、场景、池、音频、存档、输入、物理和 UI。
4. 运行流程为 Menu → Start → Playing → Settings（四路音量/静音双向绑定、预览、应用、失败保留草稿）→ Help/About → R 返回 Menu。退出按钮先打开 Dialog VM 确认。Escape 的 UI context 通过 `InputUiModalBlocker` 屏蔽 Gameplay；关闭设置面板后上下文按模态栈恢复。
5. 正常退出应等待 `CombinedSampleRuntimeServices.ShutdownAsync`；`OnDestroy` 只是兜底通知。生成器只修改自己创建的 `Assets/GFrameworkSamples/Combined` 与 `GFramework Samples` 组。

## MVVM 接入点

- `Core/UI/Runtime/UiBindingPrimitives.cs`：`ViewModelBase`、`UiCommand`、`AsyncUiCommand`、显式单/双向绑定和集合通知。
- `Core/UI/Runtime/UiViewModels.cs`：大厅、设置、暂停、加载、结算、Dialog、Help、About 和 Toast VM。
- `Core/UI/Unity/UiPanelViewBehaviour.cs`：每个面板 generation 创建并销毁自己的 VM/绑定集合。
- `Core/UI/Unity/UiControlBindings.cs`：TMP/uGUI 控件适配；绑定注册使用 `nameof` 和显式 getter/setter，不扫描属性路径。

生成器不会覆盖已存在的 prefab；首次升级旧生成物后，重新生成前请确认 prefab 上的序列化引用已指向新字段，或删除生成器拥有的 Combined 目录后再次生成。旧设置 JSON 的 `MasterVolume`/`Muted` 字段仍可读取，新增字段使用默认值。
