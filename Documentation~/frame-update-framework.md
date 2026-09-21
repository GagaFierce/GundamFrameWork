# 帧更新管理框架：完整设计与 API

状态：设计真源；纯 C# 核心初版已实现，Unity 适配与性能基准待验证。版本：设计 v1.0。更新：2026-09-21。

目标：提供可独立复用的集中更新框架，支持动态注册、多循环驱动、可配置频率、分组暂停、有序执行和性能观测。业务通过接口接入，具体策略由使用方扩展。

## 阅读导引

快速评审先看第 1–3 节的职责与架构、第 8 节的五类循环，以及第 15 节的取舍。准备实现时依次阅读 API、注册、时间与生命周期契约；分阶段任务和可执行验收场景见[实施与验收清单](frame-update-implementation-plan.md)。

本方案中的公开类型和方法都是拟定契约。首版交付目标为纯 C# 核心、Unity 默认驱动、通用策略扩展、诊断查询、示例和测试；具体网络 SDK、距离计算、游戏事件与完整物理世界暂停由扩展提供。

核心关系：**Loop 决定谁推进，Phase 决定循环内时机，Group 决定暂停与时间策略，Scope 决定生命周期，Schedule 决定更新频率，Priority 决定局部顺序。**

## 1. 技术定位与边界

当前仓库是 Unity Package，最低 Unity 版本为 `2022.3`，使用 C#；现有程序集名为 `GFramework`，现有代码命名空间为 `WFrameWork.Core.*`。

新模块采用 `WFrameWork.Core.FrameUpdate` 命名空间，分为纯 C# 核心、Unity 适配和诊断三层。核心可手动推进，可创建多个独立实例。Unity 适配负责接入引擎生命周期。

现有 `ActionManager.Update(float)` 是外部驱动入口；`CharacterAnim` 和资源加载组件有自己的 `Update`。它们可以后续通过适配器选择接入，本轮核心实现不迁移这些既有模块。

| 框架负责 | 使用方或扩展负责 |
|---|---|
| 注册、注销、重复检测、批量接入 | 更新对象内部的业务逻辑 |
| 阶段、优先级、频率、时间累计 | 业务之间的数据依赖及事务 |
| 通用组、生命周期作用域、暂停句柄 | 组的业务含义及何时暂停 |
| 调度预算、分帧、耗时统计 | 距离、可见性、重要性等评估来源 |
| Unity 生命周期适配 | 网络权威、网络时间同步、游戏事件处理 |

**非目标**：首版不接管 Unity 物理模拟、Animator、第三方组件、协程或全局 `Time.timeScale`；不更改 `Application.targetFrameRate`、VSync 或 `Time.fixedDeltaTime`；不提供多线程调度、任务依赖图、确定性锁步或完整游戏暂停服务。

这里的 FPS 配置表示对象的目标平均更新频率，实际到期落在宿主提供的更新机会中。游戏实际渲染帧率仍由宿主决定。

## 2. 总体结构

```text
Unity 驱动 / 网络驱动 / 自定义宿主
             │ 提供循环、阶段与时间样本
             ▼
     FrameUpdateManager
       ├── UpdateLoop              循环标识及各自的驱动状态
       │     ├── InputLoop         输入采样、快照
       │     ├── SimulationLoop    普通逻辑推进
       │     ├── PhysicsLoop       固定步相关逻辑
       │     ├── PresentationLoop  表现准备、插值、相机
       │     └── NetworkLoop       网络节拍相关业务
       ├── RegistrationStore       注册记录、句柄及生命周期
       ├── PhaseScheduler          各循环内部的阶段、频率及顺序
       ├── UpdateGroup             分组、暂停及时间缩放
       ├── UpdateScope             批量管理生命周期
       └── FrameUpdateDiagnostics  计数、采样及查询
             │ 接口调用
             ▼
        IFrameUpdate
       ├── 单个更新对象
       └── 批量系统 → 自己管理内部对象

使用方策略 → SetSchedule / SetPriority / SetEnabled
          或 IUpdateSchedulePolicy 适配器
```

`FrameUpdateManager` 是公开门面，内部按注册、调度和诊断分工。它持有通用元数据，调用 `IFrameUpdate`；业务可选择单对象注册，也可把批量系统注册一次。

`UpdateLoop` 表示独立推进的循环。上图的循环是组装层提供的常用命名，核心通过 CreateLoop 动态创建循环；使用方可增加自己的循环，并绑定自定义驱动。循环、阶段、分组与生命周期分别建模，具体含义见第 8 节。

框架采用显式注册。Unity 生命周期辅助组件提供便捷接入，运行时对象发现和业务装配由使用方负责。

## 3. 核心类型

| 类型 | 职责 |
|---|---|
| `FrameUpdateManager` | 管理独立的调度实例，提供注册、控制及 Tick 入口 |
| `UpdateLoop` | 标识独立驱动的循环，拥有自己的阶段序号与调度状态 |
| `LoopDriverHandle` | 持有某个循环的独占驱动绑定，释放后停止接收该驱动的 Tick |
| `IFrameUpdate` | 接受一次更新回调 |
| `FrameUpdateContext` | 提供本次回调的阶段、时间、句柄与序号 |
| `UpdateHandle` | 标识一次注册，包含调度器身份、槽位和代次 |
| `UpdateGroup` | 表达暂停与时间策略，支持父子继承 |
| `UpdateScope` | 表达生命周期归属，支持递归释放 |
| `PauseHandle` | 表达一个暂停原因，释放自己的暂停请求 |
| `UpdatePhase` | 表达所属循环内部的 EarlyUpdate、NormalUpdate、LateUpdate 阶段 |
| `UpdatePriority` | 提供通用优先级常量，实际排序使用整数 |
| `UpdateSchedule` | 描述每步、指定 FPS、每 N 步或固定间隔 |
| `FrameUpdateOptions` | 描述一次注册的组、作用域、阶段、优先级及频率 |
| `FrameUpdateConfig` | 描述容量、预算、时间上限和诊断配置 |

**组与作用域分别处理策略和生命周期。** 例如，某对象属于一个更新组，同时归属一个场景作用域；暂停组改变调度许可，释放场景作用域注销对象。同一组可以包含不同场景的对象。

每个注册项恰好归属一个循环、一个循环内阶段、一个组和一个作用域。组可以横跨多个循环，便于一次暂停关联的更新项。多个场景、模块和对象层次通过组树与作用域树组合表达；诊断标签可作为扩展元数据。

## 4. 公开 API 契约

以下是公开契约摘要，省略实现和普通值类型构造器；运行时实现位于 `Core/FrameUpdate/Runtime`，Unity 适配位于 `Core/FrameUpdate/Unity`。

```csharp
public enum UpdatePhase { EarlyUpdate, NormalUpdate, LateUpdate }
[Flags]
public enum UpdatePhaseMask
{
    Early = 1, Normal = 2, Late = 4, All = Early | Normal | Late
}
public enum UpdateTimeSource { Scaled, Unscaled }
public enum UpdateWorkClass { Required, Deferrable }

public interface IFrameUpdate
{
    void OnFrameUpdate(in FrameUpdateContext context);
}

public interface IUpdateLifetime
{
    bool IsAlive { get; }
}

public static class UpdatePriority
{
    public const int Early = -100;
    public const int Normal = 0;
    public const int Late = 100;
}

public readonly struct FrameUpdateContext
{
    public UpdateHandle Handle { get; }
    public UpdateLoop Loop { get; }
    public UpdateGroup Group { get; }
    public UpdatePhase Phase { get; }
    public long Sequence { get; }
    public double DeltaTime { get; }
    public double RawDeltaTime { get; }
    public double ElapsedTime { get; }
    public double DiscardedDeltaTime { get; }
}

public readonly struct UpdateSchedule
{
    public static UpdateSchedule EveryStep();
    public static UpdateSchedule AtFps(double fps, double initialOffsetSeconds = 0);
    public static UpdateSchedule EveryNFrames(int count, int offset = 0);
    public static UpdateSchedule AtInterval(double seconds, double initialOffsetSeconds = 0);
}

public sealed class FrameUpdateManager : IDisposable
{
    public FrameUpdateManager(FrameUpdateConfig config);

    public UpdateLoop DefaultLoop { get; }
    public UpdateGroup DefaultGroup { get; }
    public UpdateScope DefaultScope { get; }
    public UpdateLoop CreateLoop(string name);
    public bool RemoveLoop(UpdateLoop loop);
    public LoopDriverHandle BindDriver(UpdateLoop loop, string name,
        UpdatePhaseMask phases = UpdatePhaseMask.All);

    public UpdateGroup CreateGroup(string name, in UpdateGroupOptions options,
        UpdateGroup parent = default);
    public UpdateScope CreateScope(string name, UpdateScope parent = default);

    public UpdateHandle Register(IFrameUpdate target, in FrameUpdateOptions options,
        IUpdateLifetime lifetime = null);
    public void RegisterBatch(IReadOnlyList<FrameUpdateRequest> requests,
        List<UpdateHandle> results);
    public bool Unregister(UpdateHandle handle);
    public int ReleaseScope(UpdateScope scope);
    public bool RemoveGroup(UpdateGroup group);

    public bool SetEnabled(UpdateHandle handle, bool enabled);
    public bool SetSchedule(UpdateHandle handle, UpdateSchedule schedule);
    public bool SetPriority(UpdateHandle handle, int priority);
    public bool SetGroup(UpdateHandle handle, UpdateGroup group);
    public bool SetScope(UpdateHandle handle, UpdateScope scope);
    public bool SetGroupTimeScale(UpdateGroup group, double timeScale);
    public bool SetPhaseBudget(UpdateLoop loop, UpdatePhase phase, double milliseconds);

    public PauseHandle PauseAll(UpdateScope owner = default);
    public PauseHandle PauseGroup(UpdateGroup group, UpdateScope owner = default);

    public void Tick(LoopDriverHandle driver, UpdatePhase phase, in FrameTimeSample time);
    public bool TryGetRegistration(UpdateHandle handle, out FrameRegistrationInfo info);
    public void RecordHostFrame(in HostFrameSample sample);
    public FrameUpdateStats GetStats();
    public void CopyLoopStats(List<UpdateLoopStats> results);
    public void CopyGroupStats(List<UpdateGroupStats> results);
    public void CopyEntryStats(List<UpdateEntryStats> results);
    public void CopyErrors(List<FrameUpdateError> results);
    public void Dispose();
}
```

补充约定：

- `FrameUpdateOptions` 包含 `Loop`、`Phase`、`Group`、`Scope`、`Priority`、`Schedule`、`WorkClass`、`Enabled`；未指定字段继承配置，默认解析为 DefaultLoop、NormalUpdate、默认组、默认作用域、Normal、EveryStep、Required、true。可选字段须保留“是否显式设置”，区分未设置与显式 EarlyUpdate、false 或零值；注册时解析为完整配置快照。
- 循环是带管理器身份及代次的句柄。CreateLoop 每次创建独立循环，名称只供诊断；组装层缓存常用循环句柄。RemoveLoop 只允许移除没有活动项、待注册项、驱动绑定且未在 Tick 中执行的自定义循环；DefaultLoop 随管理器 Dispose 释放。更换注册项的循环或阶段通过注销后重新注册完成，并开始新的计时基线。
- BindDriver 为循环取得独占绑定。重复绑定直接报告冲突；核心 Tick 必须使用当前绑定句柄。旧绑定释放后，即使提交更大序号也不能继续驱动。句柄本身不启动线程或计时器。
- 驱动声明支持的阶段，所有已注册项必须能被该声明覆盖；绑定后的新注册项也进行同样校验。未绑定循环允许先注册，诊断展示“等待驱动”。常用驱动使用全部阶段，手动驱动可只绑定 Normal。
- `FrameUpdateRequest` 由 `Target`、`Options` 和可选 `Lifetime` 组成。批量注册先完整校验，再统一加入待注册队列；输入错误时整批及输出缓冲不变，重复的相同请求按幂等规则返回对应句柄。输出列表由调用方持有并复用。
- `FrameTimeSample` 包含 `Sequence`、可选的 `RenderFrameId`、`ScaledDeltaTime`、`UnscaledDeltaTime`。每个驱动绑定从 Sequence=1 开始，完成其声明的阶段后递增一次；阶段按 Early、Normal、Late 中被声明的顺序调用。同一循环一次完整推进中的各阶段共享序号及相同时间样本。重复、倒退、漏阶段、跨周期或样本不一致均在执行回调前拒绝。不同循环可在各自阶段调用之间交错推进。
- 所有时间输入必须有限且非负。驱动因异常或卸载不能完成当前周期时释放绑定；新绑定开始新的序号代次。普通绑定替换保留注册项已累计的有效时间，重建宿主采样基线，不补交解绑期间的墙钟时间。
- `UpdateGroupOptions` 指定根组的 `TimeSource` 和本组 `TimeScale`。未指定倍率继承配置默认值，显式指定零值表示停止；倍率必须有限且非负。子组继承根组时间源，时间倍率沿父链相乘；切换时间源通过转移到另一根组完成。
- CreateGroup 不指定 parent 时创建独立根组，因此可独立选择 Scaled 或 Unscaled；显式提供 parent 时创建其子组。CreateScope 不指定 parent 时归属 DefaultScope。组树与作用域树的根节点语义分别处理。
- 默认句柄表示无效。过期、跨调度器或代次不匹配的控制请求返回 false/0；创建和注册中的非法参数抛出明确参数异常。
- `PauseHandle` 为可释放的值类型；复制和重复 `Dispose` 只释放同一个暂停请求。暂停请求可绑定生命周期作用域，owner 未指定时属于管理器默认作用域。管理器销毁后再释放旧句柄为无操作。
- `RemoveGroup` 仅移除无子组、无注册项、无活动暂停请求的组；`ReleaseScope` 递归注销该作用域及后代并释放其持有的暂停请求，返回注销的注册项数量。默认组和默认作用域由管理器持有至 Dispose，普通移除请求被拒绝。

### 配置值与查询值

| 数据类型 | 字段及语义 |
|---|---|
| FrameUpdateOptions | 可选 Loop、Phase、Group、Scope、Priority、Schedule、WorkClass、Enabled、MaxCallbackDeltaSeconds；未设置字段从管理器配置快照解析 |
| UpdateGroupOptions | 可选 TimeSource、TimeScale；子组继承根组时间源，显式给出冲突时间源时拒绝创建 |
| FrameTimeSample | Sequence、RenderFrameId、ScaledDeltaTime、UnscaledDeltaTime；提供构造器完整传入 |
| HostFrameSample | FrameId、UnscaledDeltaTime；仅统计宿主渲染帧，不推进任何注册项 |
| FrameRegistrationInfo | 解析后的配置、生命周期状态、累计时间、剩余等待、执行及延期计数；查询返回值快照 |
| UpdatePolicyContext | 当前句柄、解析后的 Schedule/Priority、最近一份只读统计；由策略评估适配器构造 |
| FrameUpdateError | 错误序号、句柄、Loop、Phase、Sequence、异常与发生时间；有界保留 |

配置可选字段使用可空值类型表达“未设置”，并提供 Default 工厂；不额外公开通用 Optional 类型。FrameUpdateConfig 是独立的可变构建对象，Default 每次返回新对象；管理器构造时校验并深拷贝为运行时快照。注册选项中 Group、Scope、Loop 的 default 值解析为管理器默认对象；CreateGroup 的默认 parent 表示独立根，CreateScope 的默认 parent 和 Pause 的默认 owner 表示 DefaultScope。普通控制 API 传入无效句柄不自动回落。

Set 方法修改注册项的有效配置，与初始化配置独立。每项 MaxCallbackDeltaSeconds 可覆盖管理器默认值，便于对物理固定步等注册项显式保持零上限。查询与 Copy 方法不暴露可修改的内部集合；Copy 方法先清空传入列表再写入当前快照，容量不足时允许扩容。

SetPhaseBudget 在循环创建后配置其阶段软预算，milliseconds=0 表示不限；未覆盖的阶段使用 FrameUpdateConfig 的默认预算。Tick 内修改下次该阶段生效，输入须有限且非负。

### 返回值与错误

| 情况 | 处理 |
|---|---|
| Register 相同身份及相同有效配置 | 返回同一有效句柄 |
| Register 相同身份、不同有效配置 | 抛出 InvalidOperationException，原注册不变 |
| 空对象、非法枚举、非法数值或创建时使用失效 parent | 抛出参数异常，无状态修改 |
| Set/Unregister 使用过期句柄或已释放目标组/作用域 | 返回 false，无状态修改 |
| ReleaseScope 使用失效作用域 | 返回 0 |
| RemoveLoop/RemoveGroup 仍被占用 | 返回 false |
| Tick 使用失效驱动、错误线程、重入或非法时序 | 抛出 InvalidOperationException，本次不执行回调 |
| Dispose 重复调用、已失效暂停/驱动句柄重复释放 | 无操作 |
| 管理器已 Dispose | 创建、注册、查询、Tick 抛出 ObjectDisposedException；注销与控制返回 false/0，句柄释放无操作 |

数值校验涵盖 fps/interval 为有限正值、count 为正整数、offset 范围正确、倍率与时间上限为有限非负值、容量和统计窗口为正整数。优先级允许整个 int 范围，比较使用比较器，避免相减溢出。槽位代次溢出时退休该槽位并另分配，避免旧句柄再次命中。

## 5. 动态注册与集合安全

### 注册身份

注册唯一键为“对象引用身份 + 循环 + 阶段”。对象的自定义 `Equals` 不参与身份判断。同一对象可以在不同循环或阶段分别注册，回调通过 Context.Loop 和 Context.Phase 区分职责；各注册项独立累计时间。

同一唯一键、相同有效配置及同一 Lifetime 引用重复注册，返回现有句柄。若配置或生命周期来源不同，注册报告冲突，调用方通过 `Set*` 显式修改配置。组或作用域不属于唯一键，换组不会创建第二次相同阶段回调。Target 使用引用类型，值类型注册在入口拒绝，避免装箱产生不稳定身份。

可选 IUpdateLifetime 是纯 C# 存活检查，返回 false 时注销该项；省略时依赖显式注销/作用域清理。检查发生在阶段资格快照与调用前，低频项同样会清理；检查不应用于暂时暂停。检查抛异常时按注册项错误策略处理。

重复检测同时考虑本 Tick 已排队的有效配置。同一对象注销后立刻重新注册会得到新代次，旧条目先墓碑化，新条目下一安全边界激活。批量请求在同一批内部也执行去重和配置冲突校验。

一个句柄对应一个生命周期所有者。重复注册具有幂等性，不代表获得独立所有权；多个独立控制者需要独立适配对象。

### 生效时机

| 操作 | 生效规则 |
|---|---|
| Tick 外注册或修改配置 | 下一次对应阶段 Tick 使用新配置 |
| Tick 内注册 | 当前遍历不加入；该调用栈退出后可进入下一次对应阶段 Tick |
| 注销、禁用、释放作用域 | 立即标记不可调度，尚未执行的回调立即被跳过；结构整理在安全边界进行 |
| Tick 内修改频率、优先级、组或作用域 | 排队到当前 Tick 结束，下一次对应阶段 Tick 生效 |
| 暂停请求 | 立即关闭对应调度许可；当前正在执行的回调正常返回 |
| 释放暂停、重新启用 | 下一次对应阶段 Tick 恢复，清除暂停期间的时间债务 |

待注册表也参与重复检测。待注册项被注销、或其作用域在激活前释放时，该项不会被激活。

热路径使用按“循环 + 阶段”缓存的列表、槽位和待变更缓冲。排序仅在注册、注销或顺序变化后重建。每次调用前检查存活代次、启用状态和暂停门。

所有管理 API 和 Tick 在同一所属线程调用。框架拒绝重入 Tick；异步或后台任务通过使用方的主线程消息队列提交变更。

### 一个阶段 Tick 的处理顺序

1. 校验管理器、驱动所有权、线程、阶段序号与样本，提交上次安全边界已允许的结构变更。
2. 取得当前阶段的稳定执行顺序，快照注册项的资格、组倍率与生效配置。
3. 为快照中合格的注册项累计本阶段有效时间和步数，再判断到期；时间累计在回调前集中完成。
4. 按顺序执行到期项；每次调用前再次检查注销、禁用、暂停、作用域及驱动有效性。预算只延期 Deferrable。
5. 调用开始前消费本次累计时间，更新 ElapsedTime 与下次截止；回调抛出异常也不退还这次时间，防止重新启用后重复交付。
6. 在 finally 中提交允许的变更、整理墓碑、记录诊断并释放重入标记。

阶段快照前已经合格的项都获得这一阶段的时间信用。某个回调申请暂停时，后续项停止执行，但其已获得的时间保留到恢复后交付；该规则避免时间累计结果依赖遍历位置。相反，本阶段开始时被暂停的项，即使暂停在中途释放，也要等下一阶段调用获得资格。

注销、禁用、解绑或 Dispose 能阻止尚未开始的回调；当前调用栈允许完整返回。注册项创建在某个阶段调用之后时，从下次所属阶段的完整时间样本开始累计，不做子帧墙钟推算。

## 6. 调度与时间语义

### 频率

| 模式 | 语义 |
|---|---|
| EveryStep | 每次所属阶段推进时调用 |
| AtFps | 周期为 `1 / fps`，实际回调受宿主阶段频率限制 |
| EveryNFrames | 每累计 N 次有运行资格的所属阶段推进调用一次 |
| AtInterval | 累计指定秒数后获得一次调用机会 |

`EveryNFrames` 在 PhysicsLoop 中计算物理步数，在独立 NetworkLoop 中计算网络步数。首版 API 保留易理解的名称，文档明确“帧”指所属循环、所属阶段的推进次数。

每个注册项、每次阶段 Tick 最多回调一次。AtFps/AtInterval 保持原截止时间网格：执行后把截止时间推进到当前有效时间之后的下一个周期，避免每次简单重置周期导致持续漂移。宿主变慢时合并调用，`DeltaTime` 覆盖累计有效时间。

每步模式在下一次有资格的 Tick 首次执行。周期模式在首个完整周期加初始偏移后首次执行。每 N 步模式在累计 `count + offset` 步后首次执行，此后按 count 推进；offset 范围为 `[0, count)`。偏移支持把大量注册项分散到不同帧，默认偏移为零。

### 回调时间

1. 选择根组的 Scaled 或 Unscaled 时间输入，乘以组链上的倍率。
2. 未暂停、未禁用的注册项累计有效时间；未达到频率和被预算延期期间均保留累计量。
3. `RawDeltaTime` 是本次回调之前累计的有效时间；`DeltaTime` 是经过可选上限处理后实际交给对象的时间。
4. `ElapsedTime` 是该注册项历次实际交付 `DeltaTime` 的累计，首个回调从零基线开始；它是该项自己的执行时间轴。
5. 暂停或禁用期间不累计时间、不累计 N 帧计数。恢复后继续剩余等待时间，暂停前已经累计的有效时间保留。

不同“循环 + 阶段”各自维护调度状态。一份宿主时间样本用于多个循环或 Early/Normal/Late 阶段时，各项只更新自己的计时器；没有被重复累加的全局模拟时钟。不同循环可以具有不同的推进次数和 delta。

### 配置变更

改变周期时，以安全边界上的有效时间为新周期起点，保留上次回调以来累计的有效时间，新周期到期后一起交付。转组时保留已累计时间与旧周期剩余时长，后续累计使用新组的时间策略。改变优先级和作用域不重置时间。

SetSchedule 收到与当前有效计划相同的值时为成功的无操作，不重置截止时间。每 N 步模式在预算延期后按原整数步网格推进到下一个未来到期点；多个错过点合并为一次回调。暂停保留剩余步数，改计划则按新 count/offset 重设剩余步数。

### 时间上限与平滑

`MaxCallbackDeltaSeconds` 默认值为 `0`，表示不截断。设置正值后，超过上限的部分计入 `DiscardedDeltaTime` 并丢弃，`ElapsedTime` 只增加实际交付量。这是一项显式的时间损失策略。

到期判断使用原始有效时间累计，ElapsedTime 使用实际交付时间；启用截断后两者可以不同。ElapsedTime 因而不应用于跨注册项截止时间比较。需要共享模拟时钟的项目由外部时间服务提供，并对同组采用一致策略。

组倍率为零是明确停表；输入时间样本为零仅代表本步没有经过该来源的时间。EveryStep 与 EveryNFrames 仍可在零 delta 下执行或计步，AtFps/AtInterval 的时间截止不推进。依赖“完全不执行”的逻辑应使用 PauseGroup/SetEnabled，而不是依据 delta 值推断暂停。

平滑帧时间仅用于 FPS、平均耗时及可选降频评估。业务回调默认使用实际累计时间，避免显示层平滑改变计时语义。

固定间隔是“定时获得一次调用机会”。需要逐步补算的数值模拟，可在独立适配器中实现有上限的固定步累积器；其步长、最大补算次数和余量策略由该扩展负责。

## 7. 分组暂停与生命周期

暂停采用句柄集合：管理器暂停或任一祖先组暂停时，对应注册项停止获得更新。释放一个句柄只移除对应原因，剩余请求继续生效。

场景或对象申请暂停时可把自己的作用域传为 owner。作用域释放会同时撤销其暂停请求，避免申请者已经离开但全局仍保留暂停原因。

暂停不改注册关系。暂停中的新注册项继承当前组状态。组时间倍率为零时同样停止获得更新和累计时间；恢复倍率后接续剩余进度。

全局暂停的范围是“该 FrameUpdateManager 管理的全部回调”。使用方需要保留某些更新时，可以只暂停目标组，或者使用独立管理器。

作用域释放先标记整棵作用域树失效，再批量注销项、撤销其暂停请求并释放引用。暂停中的组和暂停中的管理器仍允许执行注销、驱动解绑、作用域释放及诊断查询。

| 生命周期事件 | 核心行为 | Unity 适配行为 |
|---|---|---|
| 普通对象退出使用 | 显式注销或释放作用域 | 辅助绑定在 OnDisable/OnDestroy 清理 |
| 场景卸载 | 释放对应作用域 | 监听 sceneUnloaded，释放已绑定场景作用域 |
| 对象被销毁但遗漏注销 | 由显式生命周期契约处理 | 保存独立的 Unity owner 引用，调用前检查 Unity 销毁语义并回收 |
| 对象池复用 | 旧句柄失效，新注册产生新代次 | 每次启用重新注册或显式重新启用，由使用方选定一种模式 |
| 移入常驻场景 | 可通过 SetScope 改变归属 | 提供显式重新绑定接口，在原场景卸载前完成 |
| 应用退出 | Dispose 注销全部项并释放引用 | 驱动注销引擎事件与静态默认入口 |

纯 C# 核心不会推断任意普通对象是否“已销毁”。接口引用中的 Unity 对象也必须通过适配层保存的 Unity owner 判断存活。

场景作用域绑定是显式的，不依赖每帧遍历场景。所有权从场景转为常驻时需要更新绑定；普通对象可以只使用业务作用域。

前后台策略在 Unity 适配层配置：Continue、PauseAll 或 PauseSelectedGroups。默认 Continue。失焦与应用挂起分别持有自己的暂停句柄，只有相应原因解除后才释放。

恢复应用时默认重新建立宿主采样基线，避免把操作系统挂起期间的墙钟间隔当作一帧输入。真实离线时长由外部时钟服务处理。

软失焦（如窗口失去焦点）只应用配置策略；若应用在后台仍持续收到 Update，Continue 继续交付实际样本。Unity OnApplicationPause 等实际挂起信号走采样重置，恢复后的首个循环周期使用零 delta。进程被系统静默挂起而宿主没有收到信号时，核心无法辨认该间隔，宿主可通过显式重置或时间上限策略处理。

## 8. 输入、逻辑、物理、表现与网络循环

### 各层含义

| 概念 | 回答的问题 | 示例 |
|---|---|---|
| UpdateLoop | 由哪个循环、哪种节拍推进 | Input、Simulation、Physics、Presentation、Network |
| UpdatePhase | 在该循环内部哪个阶段执行 | EarlyUpdate、NormalUpdate、LateUpdate |
| UpdateGroup | 与哪些项共享暂停和时间策略 | 任意使用方创建的组，可横跨循环 |
| UpdateScope | 随哪个生命周期清理 | 场景、模块实例、对象 |
| UpdateSchedule | 每多少次循环机会或有效时间执行 | 每步、每 N 步、指定 FPS、间隔 |

物理帧、表现帧、网络帧和输入帧分别放在 FrameUpdateManager 下的 UpdateLoop 中。UpdatePhase 为循环内部的顺序描述。Unity FixedUpdate 是 PhysicsLoop 的默认驱动来源。

### 常用循环与驱动

| 循环 | 注册内容 | 驱动与时间 |
|---|---|---|
| InputLoop | 读取已采集输入，形成输入状态或命令快照 | 默认在 Unity 动态输入系统更新完成后的 Update，通常使用 Unscaled |
| SimulationLoop | 普通逻辑与状态推进 | 默认逐 Update；使用方也可绑定独立模拟步驱动 |
| PhysicsLoop | 每个物理步的施力、运动准备及相关逻辑 | 默认 Unity FixedUpdate，使用实际固定步的时间样本 |
| PresentationLoop | 动画参数准备、视觉插值、相机和表现刷新 | 准备阶段可放 Update，最终表现阶段放 LateUpdate；时间策略由组决定 |
| NetworkLoop | 网络节拍中的命令打包、状态快照及同步业务 | 由网络库 Tick 适配或自定义网络节拍推进，通常使用 Unscaled 或宿主提供的网络步时间 |

核心可支持其他循环。常用循环由组装层显式创建和绑定；每个循环只绑定一个有效驱动。默认 Unity 组装把 DefaultLoop 用作 SimulationLoop，并创建输入、物理和表现循环。网络循环需在接入网络适配时绑定，未绑定时保持未驱动状态并在诊断中可见。

### 默认 Unity 驱动时序

```text
Unity FixedUpdate（每个渲染帧可能零次或多次）
  PhysicsLoop.EarlyUpdate → PhysicsLoop.NormalUpdate
  → PhysicsLoop.LateUpdate（仍在本次 FixedUpdate 回调内）
随后 Unity 执行自动物理模拟

Unity 动态输入采集完成后的 Update
  InputLoop：EarlyUpdate → NormalUpdate → LateUpdate
  SimulationLoop：EarlyUpdate → NormalUpdate → LateUpdate
  PresentationLoop.EarlyUpdate：表现参数准备

Unity 动画评估及其他引擎阶段

Unity LateUpdate
  PresentationLoop.NormalUpdate → PresentationLoop.LateUpdate

网络库 Tick / 自定义网络驱动
  NetworkLoop：EarlyUpdate → NormalUpdate → LateUpdate
```

以上是宿主回调映射，网络节拍不表示固定排在 LateUpdate 之后。与网络库收包、应用状态、发包的相对顺序由网络适配器明确配置；跨线程回调先移交管理器所属线程。适配器定义排队、背压和网络步交付策略。

EarlyUpdate、NormalUpdate、LateUpdate 都是循环内部阶段名。PhysicsLoop.LateUpdate 在默认驱动中仍发生于自动物理模拟之前；需要严格的物理后处理时，替换适配器并在 Unity 真实的物理后置位置派发对应阶段。单纯修改阶段名或优先级不能形成引擎物理前后钩子。

### 输入、网络与暂停边界

输入采样与游戏动作执行分别注册。暂停世界时通常保留 InputLoop 中用于 UI、恢复和退出的采样，业务动作是否消费由使用方策略决定。新 Input System 的 Dynamic/Fixed/Manual 更新模式由输入适配器声明，框架默认不手动调用 InputSystem.Update。

默认 Unity FixedUpdate 可能先于该渲染帧的动态输入采样。物理步消费最近可用的输入快照；按下、松开等边沿事件通过命令序号或消费游标实现一次消费。需要每物理步采集输入时，配置 Fixed 输入模式及对应驱动，不依赖动态 Update 的先后顺序推断。

网络库的协议、连接保活及自身 PlayerLoop 由网络库维护。NetworkLoop 调度接入的网络帧业务；适配器选择接收现有 SDK 节拍或显式接管其手动驱动模式，避免重复推进 SDK。AtFps 可以限制快照发送等业务频率，其本身不创建真实网络时钟。

组可以横跨 SimulationLoop、PhysicsLoop 和 PresentationLoop 的部分注册项，同时让 InputLoop、NetworkLoop 中的其他组继续执行。管理器 PauseAll 仍表示暂停全部注册回调。

暂停 PhysicsLoop 只停止框架注册的物理步逻辑；Unity 自动物理仍由引擎驱动。需要暂停物理世界时，由单独的物理适配或应用暂停服务控制引擎。暂停 PresentationLoop 同样不会自动停止 Animator、粒子、音频或引擎渲染。

循环节拍优先于组时间源：Unity 不调用 FixedUpdate 时，PhysicsLoop 中即使注册到 Unscaled 组也不会自行产生回调。需要持续运行的任务应绑定仍在运行的驱动。

### 执行顺序保证

每个“循环 + 阶段”内按 `(Priority 升序, 注册序号升序)` 确定执行顺序。组负责策略，不增加隐式排序层；同循环同阶段内跨组优先级仍可比较。重新注册得到新的注册序号。

跨循环顺序由驱动编排保证，不能通过优先级跨越循环或引擎阶段。需要每步先后依赖的对象应注册到同一循环、同一阶段，使用 EveryStep 和 Required，再配置优先级。

与未接入框架的 MonoBehaviour 的相对顺序由 Unity Script Execution Order 或替换驱动决定。表现插值所需的模拟快照和插值系数由模拟或表现适配提供，核心的 ElapsedTime 不充当跨循环同步时钟。

### 驱动绑定与 Unity 接入 API

纯核心的自定义驱动使用 BindDriver 获取令牌，每次推进传入同一周期的样本；Dispose 令牌立即撤销驱动权。时钟、触发事件和调度器属于不同对象，驱动的职责是把宿主节拍翻译为调用。

Unity 层提供以下目标契约，类型放在 `WFrameWork.Core.FrameUpdate.Unity`：

```csharp
public sealed class UnityFrameUpdateHost : IDisposable
{
    public static UnityFrameUpdateHost Install(FrameUpdateManager manager,
        UnityFrameUpdateSettings settings);
    public FrameUpdateManager Manager { get; }
    public UnityFrameUpdateLoops Loops { get; }
    public void ResetTimeBaseline();
    public void Dispose();
}

public static class UnityFrameRegistration
{
    public static IDisposable Bind(FrameUpdateManager manager,
        IFrameUpdate target, UnityEngine.Object owner,
        in FrameUpdateOptions options);
}
```

Install 在 Unity 主线程创建一个常驻驱动对象，绑定输入、默认模拟、物理与表现循环。UnityFrameUpdateLoops 提供 Input、Simulation、Physics、Presentation 四个句柄；Network 由可选网络适配创建并单独持有，不在默认 Unity 安装时伪造驱动。默认驱动对四个循环均声明 All 阶段。

UnityFrameUpdateSettings 保存后台策略与需要后台暂停的组，可选择复用已有的四个循环句柄。安装先校验所有绑定，再应用；中途失败撤销本次创建的驱动与空循环。重复安装到同一管理器明确报告冲突。

宿主 Update 为输入、模拟和表现循环使用相同的 delta 与 RenderFrameId，按各循环独立的 Sequence 构造样本。Presentation 样本从 Update 缓存到 LateUpdate。FixedUpdate 每次使用新的物理 Sequence，ScaledDeltaTime 取固定步的缩放时间，UnscaledDeltaTime 取固定步的不缩放时间。HostFrameSample 每个渲染 Update 上报一次。

ResetTimeBaseline 在周期边界生效，下个周期交付零 delta 并继续递增序号。它不重置注册项已经累计的有效时间。Host Dispose 撤销本宿主拥有的绑定、事件订阅和暂停请求，立即禁止继续派发；是否销毁管理器由创建者决定。仍有外部注册项的循环留在未驱动状态，重新安装可显式复用其句柄。

UnityFrameRegistration.Bind 为 owner 缓存实现 IUpdateLifetime 的存活检查对象，并把原始 target 注册到核心，保持原始引用身份；同一注册键配置或 owner 冲突时拒绝绑定。相同绑定的多个返回值共享一次所有权，释放任意一个即注销，不作为引用计数租约使用。

自动生命周期辅助组件在 OnEnable 获取一次绑定，在 OnDisable/OnDestroy 释放；管理器注入须早于启用。场景辅助 registry 将 Scene.handle 对应为作用域并在卸载时释放。只有当前组件 owner 的启停受该辅助组件管理，纯逻辑对象需要显式释放其句柄或作用域。

### 网络和固定步的扩展模板

独立网络 SDK 适配按以下顺序接入：CreateLoop → BindDriver → 订阅 SDK tick → 在所属线程派发声明阶段 → 退出时取消订阅并 Dispose 驱动。SDK 回调来自后台线程时，适配负责有界排队；队列溢出时按协议选择合并快照、拒绝输入或中止连接，不由核心静默丢帧。

自定义固定模拟驱动负责自己的固定步长和补步上限，每个真实模拟步递增 Sequence 后调用核心。它应在收到应用暂停通知时清理或冻结自己的累积器；核心暂停只约束已提交的回调，不能推断驱动内部尚未提交的时间债务。默认 Unity 物理驱动沿用 Unity 的固定步调度，不额外再套累积器。

## 9. 降频与预算扩展

基础调度器提供确定的频率机制。使用方可以在状态变化或低频评估时调用 `SetSchedule`；需要复用决策方式时使用策略接口：

```csharp
public interface IUpdateSchedulePolicy
{
    UpdateSchedule Evaluate(in UpdatePolicyContext context);
}
```

`UpdatePolicyContext` 提供注册句柄、当前频率、优先级和最近统计值。策略可通过构造注入读取自定义数据源。策略评估适配器自身通过 IFrameUpdate 注册，评估频率独立配置，输出通过 SetSchedule 生效。

距离、可见性、负载或重要性可以各自实现策略，也可组合后输出一个最终频率。策略的防抖、阈值、缓存与迟滞由扩展实现。调度核心只接受最终调度计划。

`UpdateWorkClass.Required` 为默认值：达到频率后执行。`Deferrable` 显式允许受预算延期。每个“循环 + 阶段”可配置预算，默认 `0` 表示不限；到达预算后跳过后续 Deferrable，继续执行 Required。

预算是软上限：已经开始的回调会完整返回。框架记录实际超出时间。预算延期保留时间累计，后续执行时按时间上限规则交付；严格优先级下低优先级项可能持续延期，诊断记录连续延期次数和最长等待时间。需要公平轮转或强实时预算的场景使用独立扩展。

框架通过初始偏移、每 N 步、间隔和可延期项提供分帧基础。单次大任务须由使用方拆成可迭代的小工作单元，调度器无法抢占一个长时间运行的方法。

## 10. 默认配置

| 参数 | 默认值 | 说明 |
|---|---:|---|
| InitialCapacity | 256 | 注册槽位与缓冲的初始容量，可增长 |
| DefaultLoop | 管理器默认循环 | Unity 组装将其作为 SimulationLoop |
| DefaultPhase | NormalUpdate | 默认阶段 |
| DefaultPriority | 0 | 使用 UpdatePriority.Normal |
| DefaultSchedule | EveryStep | 每次阶段推进更新 |
| DefaultTimeSource | Scaled | 默认组使用缩放时间 |
| DefaultGroupTimeScale | 1 | 组自身倍率 |
| DefaultWorkClass | Required | 默认不因预算延期 |
| LoopPhaseBudgetMilliseconds | 0 | 各循环各阶段默认不限预算 |
| MaxCallbackDeltaSeconds | 0 | 默认不截断累计时间 |
| DiagnosticsLevel | Basic | 帧率、循环及阶段总耗时和计数 |
| DetailedSampleEveryNSteps | 30 | Detailed 模式每 N 次所属循环推进采样 |
| StatisticsWindowSamples | 120 | 各时序分别保留最近 N 个统计样本 |
| BackgroundPolicy | Continue | Unity 适配默认不追加暂停 |
| ExceptionPolicy | DisableAndReport | 默认禁用异常注册项并记录错误 |
| ErrorBufferCapacity | 64 | 有界错误环形缓冲容量，满时替换最旧记录 |

配置是普通 C# 数据。Unity 可提供可选的 `FrameUpdateSettings : ScriptableObject` 转换器，用于 Inspector 编辑；核心只接收配置快照。

调度异常包含句柄、组、阶段和异常对象；错误在该 Tick 的安全边界写入有界缓冲，诊断适配通过 CopyErrors 查询。默认禁用异常项以保护其他回调，已执行的业务副作用保留。调试模式可选择 Propagate，并在 finally 中完成待变更整理和重入标记恢复。

Propagate 模式下，已接受的阶段记为结束，剩余回调本次不执行并记录 Aborted；其未交付时间保留。宿主可以继续下一声明阶段，也可以释放驱动绑定后重建。核心始终不回滚已开始的回调。

## 11. 性能与诊断

性能目标是预热后稳定 Tick 不产生框架自身托管分配；这不包含用户回调、容量扩容、首次注册、异常和显式导出报告。该目标需要实现后用 Profiler 验证。

实现采用：按“循环 + 阶段”的连续列表、对象引用身份字典、带代次的槽位复用、变更缓冲、配置变化时排序、可复用诊断输出列表。注册及生命周期事件允许按需分配，热路径不使用反射发现、LINQ 或临时闭包。

| 诊断级别 | 数据 |
|---|---|
| Off | 保留内部正确性需要的状态，关闭性能采样 |
| Basic | 宿主 FPS、各循环 Tick 频率、帧间隔、循环及阶段耗时、注册数、执行数及跳过原因 |
| Detailed | 采样单项耗时、按组聚合耗时、最大值、延期时长、异常次数 |

FPS 从 RecordHostFrame 每个渲染帧上报一次的 unscaled 时间计算，不按阶段调用数计算。相同 FrameId 重复上报拒绝，零 delta 样本不参与除法计算；未配置渲染宿主时 FPS 为“无数据”。帧间隔与回调 CPU 耗时分开展示；后者通过单调计时器测量，不能代表整帧 GPU 耗时。

物理和网络循环独立统计 Tick 频率。同一循环的多个阶段使用同一 Sequence 时只计算一次循环推进；各阶段耗时分别记录。跨渲染帧的网络样本按独立窗口统计，不能与渲染 FPS 混为一个数。驱动绑定和未驱动告警由适配诊断补充，核心只报告调用与注册状态。

跳过原因分别统计：暂停、禁用、频率未到、预算延期、生命周期失效、异常中止。组统计包含直接项与含子组汇总两种口径；全局总量只计算每个项一次。Detailed 的耗时注明采样数量和采样窗口。

每项每阶段最多归入一种结果，判定顺序为失效、禁用、暂停、未到期、预算延期、执行完成/失败或异常中止。Executed 表示已开始的回调次数，Faulted 是其中抛出异常的子集。未驱动循环的注册项显示等待状态，不凭空累加每帧跳过次数。

循环的实际 TickHz 使用同一单调实时时钟记录调用间隔，模拟 delta 用于另一个“交付时间”指标。多个固定步在同一渲染帧批量到达时，统计窗口展示实际步吞吐率，不把每步固定 delta 直接倒数冒充实测频率。计时器作为内部可替换测试依赖，用于预算与采样的可重复测试。

预算测量覆盖本阶段的调度和回调 CPU 耗时，Required 仍允许越过软预算。Detailed 统计里的组耗时是该组被采样回调耗时之和，循环总耗时额外包含调度开销；两者不要求相等。查询和可视化展示的开销独立测量。

诊断面板、日志输出和 Profiler 标记是独立适配。统计查询按需复制到调用方缓冲，UI 刷新频率独立配置。

## 12. 使用示例

以下是目标 API 的使用方式；省略应用自己的引导与销毁接线。

```csharp
sealed class ExampleUpdater : IFrameUpdate
{
    public double WorkedTime { get; private set; }

    public void OnFrameUpdate(in FrameUpdateContext context)
    {
        WorkedTime += context.DeltaTime;
    }
}

var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
var group = manager.CreateGroup("BackgroundWork", UpdateGroupOptions.Default);
var scope = manager.CreateScope("ExampleScope");
var target = new ExampleUpdater();

var options = new FrameUpdateOptions
{
    Loop = manager.DefaultLoop,
    Phase = UpdatePhase.NormalUpdate,
    Group = group,
    Scope = scope,
    Priority = UpdatePriority.Normal,
    Schedule = UpdateSchedule.AtFps(10),
    WorkClass = UpdateWorkClass.Required,
    Enabled = true
};

UpdateHandle handle = manager.Register(target, options);
manager.SetSchedule(handle, UpdateSchedule.EveryNFrames(3));

PauseHandle pause = manager.PauseGroup(group, scope);
pause.Dispose();                  // 只释放本次暂停原因

manager.SetEnabled(handle, false);
manager.SetEnabled(handle, true);
manager.ReleaseScope(scope);      // 同时注销该作用域内全部对象
manager.Dispose();
```

各类更新通过同一注册 API 指定循环，示意如下。循环句柄由组装层创建，驱动按第 8 节绑定；此片段只展示归属：

```csharp
manager.Register(inputUpdater, new FrameUpdateOptions
{
    Loop = loops.Input, Phase = UpdatePhase.NormalUpdate, Group = inputGroup
});
manager.Register(physicsUpdater, new FrameUpdateOptions
{
    Loop = loops.Physics, Phase = UpdatePhase.NormalUpdate, Group = worldGroup
});
manager.Register(viewUpdater, new FrameUpdateOptions
{
    Loop = loops.Presentation, Phase = UpdatePhase.LateUpdate, Group = visualGroup
});
manager.Register(networkUpdater, new FrameUpdateOptions
{
    Loop = loops.Network, Phase = UpdatePhase.NormalUpdate, Group = networkGroup
});
```

`loops` 是使用方组装层缓存的循环句柄集合，包含 Unity 宿主提供的句柄以及网络适配返回的 Network 句柄；这些名称不进入核心枚举。组的 Scaled/Unscaled 时间源和暂停归属由使用方独立创建与选择。

### 手动推进与暂停示例

以下是目标 API 的完整方法级示例，可用于核心测试和接入参考。它不依赖 Unity；参数 `target` 是任意 IFrameUpdate 实现。

```csharp
static void DemonstrateScheduling(IFrameUpdate target)
{
    using var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
    using var driver = manager.BindDriver(manager.DefaultLoop, "Manual", UpdatePhaseMask.Normal);
    var scope = manager.CreateScope("DemoLifetime");
    var group = manager.CreateGroup("DemoTime", new UpdateGroupOptions
    {
        TimeSource = UpdateTimeSource.Unscaled,
        TimeScale = 1
    });
    manager.Register(target, new FrameUpdateOptions
    {
        Loop = manager.DefaultLoop,
        Phase = UpdatePhase.NormalUpdate,
        Group = group,
        Scope = scope,
        Schedule = UpdateSchedule.AtInterval(0.1)
    });

    manager.Tick(driver, UpdatePhase.NormalUpdate,
        new FrameTimeSample(1, scaledDeltaTime: 0.04, unscaledDeltaTime: 0.04));

    using (manager.PauseGroup(group, scope))
    {
        manager.Tick(driver, UpdatePhase.NormalUpdate,
            new FrameTimeSample(2, scaledDeltaTime: 5, unscaledDeltaTime: 5));
    }

    manager.Tick(driver, UpdatePhase.NormalUpdate,
        new FrameTimeSample(3, scaledDeltaTime: 0.06, unscaledDeltaTime: 0.06));
    // 到此只执行一次回调，DeltaTime 为 0.1，暂停中的 5 秒未累计。
    manager.ReleaseScope(scope);
}
```

FrameTimeSample 构造器签名为 `(long sequence, double scaledDeltaTime, double unscaledDeltaTime, long? renderFrameId = null)`。浮点到期比较使用实现统一定义的容差，仅修正数值舍入，不允许把明显未到期的项提前执行；验证使用绝对与相对误差界限。

Unity 便捷层提供两种接入：已有组件组合持有注册句柄；可选 `FrameUpdateBehaviour` 自动绑定 OnEnable/OnDisable。可选基类使用受保护的生命周期钩子供子类扩展，业务无需继承框架基类也能注册。

驱动实例通过 UnityFrameUpdateHost.Install 显式初始化创建。停止应用或禁用 Domain Reload 后重新进入 Play 时，适配层重置静态宿主 registry、驱动标记和场景事件订阅，避免重复驱动。首版采用显式注入管理器，不自动创建隐藏的全局默认单例。

## 13. 拟定目录与程序集

```text
Core/FrameUpdate/
  Runtime/              纯 C# 核心、动态循环与公共契约
  Unity/                输入/模拟/物理/表现循环驱动、生命周期绑定、配置转换
  Diagnostics/          可选诊断与展示适配
Tests~/FrameUpdate/     隔离测试与基准工程，按测试接入方案导入
Samples~/FrameUpdate/   基础注册、分组暂停、自定义策略示例
Documentation~/        API、时序语义与调优说明
```

新增程序集按依赖方向分离：`WFrameWork.FrameUpdate` 为纯核心，设置 `noEngineReferences`；`WFrameWork.FrameUpdate.Unity` 引用核心与 Unity；Unity 诊断展示归入适配程序集或独立可选程序集。新模块使用自身 asmdef 与现有 GFramework 隔离，首版不修改现有模块依赖关系。

具体网络库适配作为独立可选包提供，引用核心和对应 SDK。网络库类型不进入核心公开契约。

测试接入确定为两条路径：Tests~/FrameUpdate/Core 下的独立 .NET 测试工程链接纯核心源文件；Tests~/FrameUpdate/Unity 下的测试程序集由验证工程显式复制导入，运行 EditMode/PlayMode 检查。带 `~` 的目录由 Unity 忽略，独立测试工程和导入步骤必须随测试说明交付。具体用例见实施与验收清单。

## 14. 验收与实施拆分

| 独立实施单元 | 验收重点 |
|---|---|
| 注册与句柄 | 同对象重复、跨阶段注册、批量校验、旧代次注销、新项在 Tick 中出现 |
| 循环、阶段与顺序 | 动态创建循环、独立序号、同对象跨循环注册、同循环跨组顺序、重入与重复样本拒绝 |
| 调度与计时 | FPS/间隔漂移、每 N 步、初始错峰、可变帧间隔、累计 delta、时间截断 |
| 暂停与作用域 | 嵌套暂停、暂停时新增、禁用后恢复、递归清理、清理期间注销其他项 |
| Unity 适配 | 固定步多次/零次、动态输入与物理步快照关系、表现准备与 LateUpdate、销毁 owner、场景卸载、常驻迁移、前后台与关闭 Domain Reload |
| 外部驱动适配 | 网络节拍独立于渲染帧、跨线程移交、禁止双驱动 SDK、未绑定循环诊断、暂停普通逻辑时网络可继续 |
| 策略与诊断 | 动态调频、预算延期、错误隔离、采样口径、统计列表复用 |
| 文档与示例 | 按 API 独立完成注册、调频、暂停和生命周期清理 |

核心使用可注入时间样本进行确定性测试；频率断言允许最多一个宿主阶段步的到期量化误差，不把低宿主帧率下的目标 FPS 当作保证。

性能基准分别使用 `EntryCount` 为 100、1,000、10,000 的空回调与轻量回调，测量预热后稳定开销、注册/注销峰值、诊断开关差异及 GC.Alloc。结果同时记录设备、Unity 版本、后端与开发构建状态，不在实现前承诺具体耗时收益。

## 15. 设计选择依据

| 方案 | 适用性 | 本次选择 |
|---|---|---|
| 单一 MonoBehaviour 管理全部状态 | 入门简单，核心与引擎绑定较紧 | 采用薄驱动，状态留在纯核心 |
| 纯 C# 核心 + Unity 适配 | 便于复用、手动测试和更换驱动 | 作为正式结构 |
| 首版直接替换 Unity PlayerLoop | 能精确挂接原生阶段，需要管理安装、卸载及第三方协作 | 作为后续可替换驱动 |

首版用循环表达驱动边界，用阶段及整数优先级表达局部顺序，用组表达暂停策略，用作用域表达生命周期。各维度独立，便于替换驱动、扩展循环和跨循环组合暂停。

每项最多调用一次的合并策略适合通用帧任务。数值模拟所需的补步、网络权威和世界事件队列在各自扩展中决定，保持调度框架的复用边界。

## 16. 本轮交付状态

- 已核对当前包的语言、Unity 版本、程序集、命名空间与现有更新入口。
- 已定稿框架职责、动态注册契约、时间语义、默认配置及扩展边界。
- 已补充 UpdateLoop 层，明确输入、物理、表现和网络的注册位置、驱动时序与适配边界。
- 已给出 API、使用示例、实施单元和验收标准。
- 已补齐独占驱动绑定、配置与错误语义、阶段快照、异常计时、手动推进示例及 Unity 安装契约。
- 纯 C# Runtime、核心独立测试、手动驱动示例和策略 runner 已写入；核心测试工程已编译并通过 55 项测试。
- Unity Host/Driver、owner 绑定和场景作用域保留为适配初版，尚未在 Unity 2022.3 EditMode/PlayMode 工程中编译运行。
- 详细采样、Unity 配置资产、性能报告、具体网络 SDK 与既有模块迁移仍未完成；本文件不把规划的 59 个验收场景当作全部通过。
- 当前仓库是包目录而非完整 Unity 工程；命令行探测受到 Licensing Client access token unavailable 影响，未将 Unity 适配声明为已验证。

首版设计已具备明确实施边界。PlayerLoop 精确钩子、自定义固定模拟驱动、具体网络 SDK、公平预算调度、世界物理暂停和跨循环时钟属于有明确接入位置的后续扩展；它们不影响按本方案先实现通用核心。

本文为设计真源。离线 HTML 由本文及实施清单生成供阅读，后续更新应先修改 Markdown，再重新生成阅读版。

来源：当前仓库 `package.json`、`Core/Core.asmdef`、`Core/Actions/ActionManager.cs`、`Core/Anim/CharacterAnim.cs`、`Core/ResLoad/ResourcesManager.cs`；2026-09-20 对话中的通用框架需求与本轮设计选择。
