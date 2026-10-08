# GFramework Input、Physics、UI 模块

本页描述 `com.gagafierce.gundamframework` 的 Input、Physics、UI 模块。最低 Unity 版本为 2022.3；UI 的 Unity 默认资源入口依赖包声明的 Addressables 1.22.3。三个模块都要求调用方显式创建服务并在结束时释放，不能把它们当作隐藏全局单例。

## 程序集边界

```text
WFrameWork.FrameUpdate (纯 C#)
             ↑                 ↑
WFrameWork.Input          WFrameWork.Physics        WFrameWork.UI
      ↑                         ↑                         ↑
WFrameWork.Input.Unity   WFrameWork.Physics.Unity   WFrameWork.UI.Unity
      └──────────────┐          │          ┌──────────────┘
 WFrameWork.Input.UIBridge     │     WFrameWork.Physics.InputBridge
                              │
                         业务装配层
```

`WFrameWork.Input` 不依赖 FrameUpdate；`WFrameWork.Physics`、`WFrameWork.UI` 只依赖纯 FrameUpdate；Unity 适配只向上依赖纯层。`Input.UIBridge` 和 `Physics.InputBridge` 是可选交叉装配程序集，避免三个模块相互引用。`WFrameWork.Input.NewInputSystem` 有独立 asmdef、`GFRAMEWORK_INPUT_SYSTEM` 编译约束和 package version define；未安装 Input System 时基础包不引用其类型。

## 初始化与释放

独立模块装配的最小骨架如下。宿主是调用方唯一创建的 FrameUpdate 驱动，Input、Physics、UI 不会另建驱动；Combined Sample 使用 `UnityGameRuntime` 和 `GameRuntime` 统一持有这些对象。

```csharp
var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
var host = UnityFrameUpdateHost.Install(manager);

var input = new InputService(new LegacyInputBackend(bindings));
input.RegisterContext("Gameplay", priority: 0, blocksLowerPriority: false, enabledByDefault: true);
input.RegisterAction(new InputActionDefinition(
    new InputActionId("Gameplay.Jump"), InputActionType.Button));
var inputBinding = new InputFrameUpdateAdapter(input, manager, host.Loops.Input);

var physics = new PhysicsStepDispatcher(manager, host.Loops.Physics);
var moverBinding = physics.Register(mover, workClass: UpdateWorkClass.Required);

var ui = new UiPanelManager(resources, factory,
    modalBlocker: new InputUiModalBlocker(input));
ui.Register(new UiPanelDefinition(new UiPanelId("Settings"), "UI/Settings",
    UiPanelLayer.Modal, isModal: true));
ui.AttachToFrameUpdate(manager, host.Loops.Presentation);

// 关闭顺序：业务绑定/服务 → 宿主 → manager。每个 Dispose 可重复调用。
ui.Dispose();
moverBinding.Dispose(); physics.Dispose(); inputBinding.Dispose();
host.Dispose(); input.Dispose(); manager.Dispose();
```

Unity 适配 API 只能在主线程调用。`UnityFrameUpdateHost` 复用 Input、Simulation、Physics、Presentation 四个循环：输入适配注册在 Input Early，物理参与者注册到 Physics，UI 默认注册在 Presentation Normal。`FixedUpdate` 可能早于同一渲染帧的 `Update`，也可能一帧多次或零次；宿主不会替输入或 UI 凭空制造固定步。

## Input

动作 ID 是字符串形式的逻辑标识，业务不直接依赖键码。`Button`、`Axis1D`、`Axis2D` 都通过 `InputActionDefinition` 配置死区、反转和缩放。`InputSnapshot` 是当前采样帧的只读视图；重复查询不会清掉 Pressed/Released 状态。状态转换为 `Pressed`、`Held`、`Released`、`Canceled`，失焦、挂起和上下文屏蔽都会清理卡键。使用 `InputLifecycleBehaviour` 可把 Unity 的 Focus、Pause、Disable、Destroy 直接接到服务清理；它只负责生命周期，不会创建第二个采样驱动。

上下文按优先级路由。高优先级且 `blocksLowerPriority=true` 的启用上下文会屏蔽低优先级动作；切换时低优先级已按住动作发出一次 `Canceled`，恢复后下一次采样重新形成按下边沿。`InputSubscription.Dispose` 可安全重复调用，订阅者异常不会阻断状态清理。

固定步使用 `CreateFixedEventReader` 获得独立消费游标。每个 reader 都有自己的游标，不会抢走渲染快照；Pressed/Released/Canceled 事件进入有界环形缓冲。容量满时丢弃最旧事件并递增 `MissedEventCount`/`DroppedFixedEventCount`；超过配置有效帧数的事件被跳过。连续轴和 Held 状态直接读 `InputService.Snapshot`，不会因某个 reader 消费边沿而消失。采样发生在 Update 而固定步可能更早，因此不承诺不存在一个固定步的输入延迟；缓冲只保证容量和有效期范围内的事件不丢失。

基础后端是 `WFrameWork.Input.Unity.LegacyInputBackend`，不需要新包。安装 Input System 后才导入 `WFrameWork.Input.NewInputSystem`；New-only 模式下它只调用 `InputAction`，Legacy 适配不会被该程序集引用。Both 模式应由应用为每个 `InputService` 选择一个后端，不能把两套后端装进同一个实例。UI 导航仍使用场景中已经配置的对应 `EventSystem`/`InputModule`，模块不重复派发 UI 事件。

## Physics

`PhysicsQueryService3D` 和 `PhysicsQueryService2D` 使用 Unity 2022.3 的显式 `PhysicsScene`/`PhysicsScene2D` 与 NonAlloc 缓冲。Raycast、Sphere/CircleCast、BoxCast、Overlap 已覆盖，返回 `PhysicsQueryResult.Count` 和 `MayBeTruncated`；有效结果只在 `[0, Count)`，调用者必须在 `MayBeTruncated=true` 时增大缓冲并重新查询。默认不排序，Cast 可开启距离插入排序；`TryGetNearest` 在原始结果中选择最近命中。空结果返回 0，无效参数抛出参数异常，无效/已卸载场景返回 0。

`PhysicsStepDispatcher` 将 `IPhysicsFixedStepParticipant` 注册到调用方的 Physics loop，默认 `EveryStep + Required`，包含第一次零 delta 步的明确回调。注销和重复 Dispose 都是安全的。`RigidbodyFixedStepBehaviour` 只是示例业务运动：通过 `MovePosition` 或施力移动，不调用 `Physics.Simulate`，不改变 `autoSimulation`、`simulationMode`、`Time.timeScale` 或 `fixedDeltaTime`。暂停 FrameUpdate 分组只暂停对应业务回调，不等于暂停 Unity 原生刚体、Animator 或整个物理世界。

`PhysicsCollisionBridge2D/3D` 的 Enter/Stay/Exit 订阅返回可释放句柄。payload 只在 Unity 原生回调期间有效，不缓存 Collision/Collider 引用；组件销毁时清空订阅并隔离用户回调异常。Unity 原生碰撞回调与 Physics loop 的先后关系不作额外保证。

## UI

`UiPanelManager` 需要 `UiPanelDefinition`、窄资源提供者和窄面板工厂。Unity 默认提供者为 `AddressablesUiResourceProvider`，由 `ResourceService` 持有类型化租约；面板关闭时实例和租约各释放一次。旧的 `ResourcesUiResourceProvider` 仅保留给迁移期兼容代码，不再是默认入口。

单实例面板在 Loading 期间共享底层加载和 handle；不带取消令牌的调用方共享结果，带令牌的调用方拥有独立等待者，取消一个调用方不会取消其他有效调用方。加载失败会移除状态并清理已得到的资源。关闭或 CancellationToken 取消是逻辑取消：底层 Addressables 请求不能中止时，过时结果会被失效标记拦截，返回的资源立刻释放且不会重新打开面板。关闭后立即重开创建新请求。首版明确采用关闭即销毁、不缓存策略。

面板状态为 Loading、Visible、Hidden、Closing、Closed/Failed。Modal 面板维护栈、焦点保存/恢复和 `IUiModalInputBlocker`；`InputUiModalBlocker` 会为每个模态层获取一个独立的高优先级 UI 上下文，多层关闭时不会提前解除其他模态的阻挡。`UIRoot` 要求显式 Canvas、HUD/Normal/Modal/Overlay 层和已经存在的 EventSystem；缺失时抛出诊断，不静默创建第二个 EventSystem。

UI 管理器挂到 Presentation 的 Unscaled group，只有脏面板或显式持续更新面板才刷新。玩法暂停或 `Time.timeScale=0` 不会阻止依赖渲染循环的菜单打开/关闭，但没有 FixedUpdate 时仍不会有物理步。鼠标/触摸是否被 UI 射线拦截由 EventSystem/InputModule 决定；不能仅凭可能滞后的 `IsPointerOverGameObject` 对同帧无穿透作保证。

## Samples 与测试

Package Manager 导入 Samples 后提供：

- `Input module`：Legacy 动作、快照、上下文切换和订阅释放。
- `Physics module`：固定步刚体移动、3D Raycast 和排序结果。
- `UI module`：显式 UIRoot/EventSystem、Addressables 面板和模态管理。
- `Combined modules`：生成器创建本地场景、预制体、配置和 Addressables 组，展示 Input → Physics fixed step → Pool/Audio/Save → UI。

各 Sample 都是可添加到空 GameObject 的 bootstrap；UI Sample 仍需在场景中配置一个 UIRoot、四层 RectTransform、EventSystem 和 PanelDefinitionAsset，这是有意的显式资源所有权。

纯逻辑测试工程位于 `Tests~/Modules/Core`，Unity 不会自动发现 `Tests~` 目录。运行：

```text
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj"
```

前一命令覆盖输入状态/上下文/快照/固定步缓冲、Physics 注册注销、UI 并发/取消/失败/模态/脏刷新、资源租约、对象池和 Save；后一命令是既有 FrameUpdate 回归。`Tests~` 需要导入临时 Unity 工程后，另建 EditMode/PlayMode asmdef 才能执行 Unity 场景验证。

## 已验证结果与限制

当前已实测纯 C# 模块测试 `28/28` 和 FrameUpdate 回归 `55/55`。本执行环境只有 Unity 启动包装器，没有可运行的 Unity 2022.3 编辑器，因此未宣称 UnityEngine 编译、UPM 导入、EditMode/PlayMode、关闭 Domain Reload 重复 Play、前后台场景、EventSystem/InputModule、Addressables 内容构建或 Profiler/Player 验收通过。使用 `Validation~/validate-package.ps1` 并提供真实宿主 Unity 路径后，才会执行编辑器 Addressables 校验。

首版明确不包含完整重绑定编辑器、多本地玩家、录制回放、ECS/DOTS/确定性物理、回滚、角色控制器、PlayerLoop 原生物理后回调、远程内容更新、CDN 管理台、UI Toolkit、MVVM/绑定 DSL 和资源系统重构。
