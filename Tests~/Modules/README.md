# GFramework Modules Tests

从仓库根目录执行：

```powershell
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
```

这组测试不需要 Unity，使用可控完成顺序和释放计数替身覆盖 Input、UI、资源租约、Pool、Save。Unity 集成测试不能在这个纯 UPM 包目录自动发现。

## Unity 测试导入

把宿主工程中的 `Tests~/Unity` 目录复制到宿主工程 `Assets/Tests/GFramework`，然后在 Unity Test Runner 运行 EditMode/PlayMode。宿主必须已安装 Addressables 1.22.x，并提供自己的 Addressables Settings、Groups 和本地构建内容；包不能替宿主工程创建 Settings。
