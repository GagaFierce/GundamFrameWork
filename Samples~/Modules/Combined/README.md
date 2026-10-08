# Combined modules sample

1. 导入 Sample 后执行 `GFramework/Samples/Generate Combined Sample Assets`，生成 UIRoot、EventSystem、Settings prefab、Player prefab、配置、场景和 Addressables 组条目。
2. 执行 `GFramework/Samples/Build Combined Addressables Content`，再打开 `Assets/GFrameworkSamples/Combined/Scenes/CombinedSample.unity`。
3. `CombinedSampleBootstrap` 演示 Escape 打开/返回设置、Space 仅在 Pressed 固定步事件跳跃；`CombinedSampleRuntimeServices` 初始化 Addressables、场景、池、音频和 persistentDataPath 存档。
4. `InputUiModalBlocker` 通过独立 UI context 屏蔽 Gameplay；关闭设置面板后上下文按模态栈恢复。生成器只修改自己创建的 `Assets/GFrameworkSamples/Combined` 与 `GFramework Samples` 组。
