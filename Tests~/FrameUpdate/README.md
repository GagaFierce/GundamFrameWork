# 帧更新框架测试

`Core` 是不依赖 Unity 的独立 .NET 测试工程，直接链接 `Core/FrameUpdate/Runtime/**/*.cs`。

在仓库根目录执行：

```powershell
dotnet run --project Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj
```

可按名称筛选单组场景：

```powershell
dotnet run --project Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj -- --filter "T03"
```

基准入口只用于采集相对数据，不是性能验收结论：

```powershell
dotnet run --project Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj -- --benchmark 1000
```

当前核心验证结果：55 项通过。仓库本身是 Unity 包目录，不是完整 Unity 工程；本次命令行探测也未完成适配编译（环境报告 Licensing Client access token unavailable）。因此 Unity 适配尚未在 Unity 2022.3 EditMode/PlayMode 工程中运行；实施清单中的 59 个场景也不能据此视为全部通过。
