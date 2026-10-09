# Unity 集成测试导入

此目录为包仓库中的可导入测试源，不会被 UPM 自动编译。将整个目录复制到宿主工程：

```powershell
Copy-Item -Recurse -Force "<package>/Tests~/Unity" "<host-project>/Assets/Tests/GFramework"
```

随后在 Unity 2022.3 Test Runner 中运行 `GFramework Unity Integration Tests`。PlayMode 场景需要在 Inspector 中配置本地 Addressables prefab/scene address；测试通过 `AddressablesResourceService`/`ResourceService` 验证框架 API，不使用 AssetDatabase 模式冒充运行时内容。

宿主验收顺序：

1. Package Manager 成功导入包并无 asmdef 编译错误。
2. Addressables Groups 使用本地 Build & Load Paths，执行 Build Player Content。
3. 运行 EditMode 的输入/UI/资源所有权回归测试。
4. 运行 PlayMode 的 `UnityGameRuntime` 初始化/关闭、Addressables 租约异步释放、实例池销毁屏障、场景实际卸载、UI 焦点、前后台和池/音频重复创建测试。
5. 在目标 Player 上重复运行并记录资源持有、GC 和构建结果。

新增 `GFrameworkUiMvvmUnityTests` 覆盖 Slider/Toggle 双向同步、TMP 文本同步、Button 监听解绑。Combined Sample 生成器还应手工验证 Menu/Settings/Help/About/Dialog 的 prefab 引用、TMP 中文字体、CanvasScaler/安全区、模态焦点和反复打开关闭 20 次后的 Addressables 租约与监听基线。

当前包仓库不包含完整 Unity 宿主。本机检测到 Unity 2022.3.62f3 并尝试创建临时宿主，但 UPM 在解析包时报告 `The "path" argument must be of type string. Received undefined`（正式包声明的 TextMeshPro 3.0.9 不在本机缓存，缓存只有 3.0.7），因此本轮未完成 Unity asmdef/Addressables/PlayMode/Player 验收。复制测试源后，仍需在依赖可解析的宿主中执行 `GFramework/Samples/Validate Combined Addressables`、Build Player Content，再运行这些测试。
