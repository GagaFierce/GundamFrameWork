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
4. 运行 PlayMode 的初始化、实例/场景释放、UI 焦点、前后台和池/音频重复创建测试。
5. 在目标 Player 上重复运行并记录资源持有、GC 和构建结果。

当前包仓库不包含完整 Unity 宿主，且本机没有可执行的 Unity 2022.3 编辑器；因此复制测试源本身不等于已完成 Unity 验收。先在宿主执行 `GFramework/Samples/Validate Combined Addressables` 和 Build Player Content，再运行这些测试。
