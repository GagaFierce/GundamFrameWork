# GFramework Modules Tests

从仓库根目录执行：

```powershell
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
```

这组测试不需要 Unity，使用可控完成顺序和释放计数替身覆盖 Input、UI、资源租约、Pool、Save。Unity 集成测试不能在这个纯 UPM 包目录自动发现。

## Unity 测试导入

把 `Tests~/Unity` 目录复制到宿主工程 `Assets/Tests/GFramework`，可运行基础 Unity EditMode/PlayMode 测试。Addressables 是可选依赖；专项测试在 `Tests~/Addressables`，需要宿主安装 Addressables 1.22.x 并提供自己的 Settings、Groups 和本地构建内容。
