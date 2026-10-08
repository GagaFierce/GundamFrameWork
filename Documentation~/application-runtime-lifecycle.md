# GFramework 应用运行时与生命周期

## 程序集与依赖

```text
Diagnostics ── Threading ── Threading.Unity
      │             │              │
      ├─ 各 Runtime 模块 ── 各 Unity 适配模块
      │
      └─ Application(Runtime) ── Application.Unity
                                      │
                              Combined Sample
```

`WFrameWork.Application` 是纯 C# 装配契约，引用 Runtime 模块但不引用 UnityEngine。`Application.Unity` 创建一个 `FrameUpdateManager`、一个 `UnityFrameUpdateHost` 和一个显式 `UnityMainThreadDispatcher`。Combined 示例只创建这一套对象。FrameUpdate 核心仍保持自己的线程所有者校验和 `noEngineReferences=true`。

## 应用、模块与业务作用域

`GameRuntime` 的状态为 `Created → Initializing → Running → Stopping → Stopped`，初始化失败进入 `Failed`。同一时刻的重复初始化共享同一个 Task；初始化中关闭会取消初始化并等待已开始部分的回滚；重复关闭共享同一个 Task。`GameRuntimePart` 的 `RuntimeOwnership.Borrowed` 只观察服务、不执行关闭，`Owned` 才执行关闭。

`RuntimeScope` 用于应用、场景和临时 UI/功能作用域。父作用域拥有子作用域，后注册的使用者先关闭。`Track` 会等待待完成业务操作；关闭先取消作用域令牌，再等待已跟踪操作，随后按反向依赖顺序执行 Owned 清理。Borrowed 依赖不会被销毁，关闭失败会继续其他清理并聚合报告。

```text
Application scope
└─ Gameplay scene scope
   ├─ input/fixed-step registrations
   ├─ pool and scene leases
   └─ temporary UI/audio handles
```

## Unity 主线程边界

Unity 适配器接收同一个 `IMainThreadDispatcher`。Addressables 的创建、释放、场景卸载、AudioSource 操作和 GameObject 销毁都通过该边界执行。`UnityMainThreadDispatcher` 在根创建时显式捕获 Unity 主线程上下文；业务代码不会在每个 `await` 处依赖偶然的上下文捕获。清理使用不受 Gameplay 分组暂停和 `Time.timeScale` 影响的 Unity Update 驱动。

纯计算和存档文件 I/O 可以异步执行。跨线程直接使用 Unity 后端会被拒绝。`Dispose` 是兼容的非等待入口；需要知道清理已经完成时必须等待 `CloseAsync`、`ShutdownAsync` 或 `ReleaseAsync`。

## 资源、场景、池和音频语义

- `ResourceService` 为共享资产发放独立租约；一个等待者取消不会取消其他等待者。服务关闭会等待外部资产租约和实例租约归还，也会等待底层加载收尾，之后才关闭后端。
- `SceneLease.ReleaseAsync` 完成时才表示 Addressables `UnloadSceneAsync` 完成。取消加载只取消调用方等待，迟到成功会被卸载；SceneFlow 在迟到清理结束前拒绝新的加载请求。
- `AddressableGameObjectPool.CloseAsync` 先阻止迟到 Warmup，再逐个销毁实例，等待 `UnityObjectLifetime.DestroyAndWait`，最后释放 prefab 租约。`Task.Yield` 不再作为销毁完成证明。
- `AudioService.CloseAsync` 先停止播放、等待加载中的播放请求完成收尾，再释放 Clip。AudioSource 操作由 Unity 主线程边界执行。
- `SaveService.CloseAsync` 等待正在执行的读写；不会在同步对象仍被使用时提前释放。

## 最小游戏流程

`GameFlowService` 只负责业务意图，不把流程硬编码进 Scene backend：

```text
Boot → Menu → Loading → Playing → Returning → Menu
                         │             │
                         └── Failed ◄──┘
```

重复开始请求共享当前 Loading Task；返回会取消调用方的加载等待，等待 SceneFlow 的迟到操作完成，再关闭场景作用域和场景租约。旧场景已被 Single 卸载时不会伪造回滚成功。

## 综合示例

导入 `Combined modules` Sample 后：

1. 执行 `GFramework/Samples/Generate Combined Sample Assets`。
2. 检查宿主 Addressables Settings，并执行 `GFramework/Samples/Validate Combined Addressables`。
3. 执行 `GFramework/Samples/Build Combined Addressables Content`。
4. 打开 `Assets/GFrameworkSamples/Combined/Scenes/CombinedSample.unity`。
5. 运行后等待 `CombinedSampleRuntimeServices.ReadyTask` 完成。Menu 面板的 Start 按钮进入 Loading/Playing 并以 Additive 方式加载 `Gameplay.unity`；R 返回 Menu，Escape 打开/关闭 Settings 模态，Settings 的 Save 按钮修改音量/静音并写入存档，Space 通过固定步输入触发示例跳跃。宿主也可调用 `StartGameAsync` 和 `ReturnToMenuAsync`。

生成器只处理 `Assets/GFrameworkSamples/Combined` 和 `GFramework Samples` 组，不修改宿主其他 Addressables Groups。宿主必须提供 Addressables Settings 和本地构建内容。

## 诊断与验证

`DiagnosticRegistry.Capture()` 按需采样，保留有限历史；应用根注册应用状态、作用域状态、资源租约、场景数量、UI 面板/模态深度和音频活动数。正常取消不自动作为错误；清理错误带有服务/作用域名称。

本机验证入口：

```powershell
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj"
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
./Validation~/validate-package.ps1
```

当前真实纯 C# 结果为 FrameUpdate `55/55`、Modules `28/28`。Unity 包导入、Addressables 本地内容构建、EditMode/PlayMode、关闭 Domain Reload、前后台、20 次 Player 流程和 Player 构建需要宿主 Unity 工程及可用 Unity 2022.3 编辑器；当前执行环境没有该编辑器，因此这些项目保持未验证。

## 兼容变化

现有 `Dispose`、`SceneLease.Dispose`、`AddressableGameObjectPool.Dispose` 和同步资源入口保留为兼容入口，但它们不承诺等待异步清理。新增调用方应优先使用 `CloseAsync`/`ShutdownAsync`/`ReleaseAsync`。`ResourcesManager` 继续保留为迁移期 API，新综合装配不再创建第二个 Resources 服务或资源服务。
