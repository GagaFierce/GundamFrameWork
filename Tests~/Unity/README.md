# Unity 集成测试导入

此目录为包仓库中的可导入基础 Unity 测试源，不会被 UPM 自动编译。将整个目录复制到宿主工程：

```powershell
Copy-Item -Recurse -Force "<package>/Tests~/Unity" "<host-project>/Assets/Tests/GFramework"
```

随后在 Unity 2022.3 Test Runner 中运行 `GFramework Unity Integration Tests`。基础测试不要求 Addressables。

宿主验收顺序：

1. Package Manager 成功导入包并无 asmdef 编译错误。
2. 运行 EditMode 的输入/UI/资源所有权回归测试。
3. 运行 PlayMode 的 `UnityGameRuntime` 初始化/关闭、实例池销毁屏障、UI 焦点、前后台和池/音频重复创建测试。
4. 在目标 Player 上重复运行并记录资源持有、GC 和构建结果。

新增 `GFrameworkUiMvvmUnityTests` 覆盖 Slider/Toggle 双向同步、TMP 文本同步、Button 监听解绑。导入 `Combined modules` Sample 时需要另行安装 Addressables；生成器还应手工验证 Menu/Settings/Help/About/Dialog 的 prefab 引用、TMP 中文字体、CanvasScaler/安全区、模态焦点和反复打开关闭 20 次后的 Addressables 租约与监听基线。

Addressables 专项 PlayMode 测试位于 `Tests~/Addressables`，仅在宿主安装 Addressables 并配置本地 Settings、Groups 和构建内容后导入运行。当前包仓库不包含完整 Unity 宿主；编辑器和 Player 验收仍需在宿主工程执行。
