# 帧更新框架：实施与验收清单

状态：实施中；M1–M4 核心初版已完成，M5 基础诊断已完成，Unity 验证与性能基准待执行。对应[完整设计与 API](frame-update-framework.md)，设计版本 v1.0，更新 2026-09-21。

本文把方案分成可独立编译和验证的实施单元。勾选表示代码、测试与文档均已完成，不能只因文件创建或静态检查通过而勾选。

## 1. 首版交付范围

| 交付层 | 首版内容 | 完成证据 |
|---|---|---|
| 纯 C# 核心 | 多循环、独占驱动、注册、调度、组、作用域、暂停、错误与查询 | 独立核心测试通过，Unity 目标版本编译通过 |
| Unity 适配 | 显式安装、输入/模拟/物理/表现驱动、owner 与场景清理、前后台策略 | EditMode/PlayMode 验证记录 |
| 扩展支持 | 自定义循环及驱动、调频策略评估器、有界网络接入示例 | 无具体网络 SDK 依赖的手动驱动示例 |
| 诊断 | Basic/Detailed、循环与组统计、错误缓冲、简洁的可选调试展示 | 指标口径测试及开启/关闭开销对比 |
| 文档与示例 | 手动推进、Unity 绑定、分组暂停、动态调频、批量系统 | 示例可编译且能独立运行 |

具体游戏逻辑、完整网络协议、Unity 原生物理暂停、Animator 接管、公平调度算法和确定性固定模拟不进入首版。其接入方式由主设计第 8–9 节规定。

## 2. 建议文件职责

这些是实施时的目标文件，不表示现在已经存在。一个文件只承载相应职责，可按实现规模合并小型值类型。

```text
Core/FrameUpdate/Runtime/
  WFrameWork.FrameUpdate.asmdef
  FrameUpdateManager.cs              公开门面与线程/销毁状态
  Contracts/
    IFrameUpdate.cs                  更新接口
    IUpdateLifetime.cs               可选纯 C# 存活检查
    FrameUpdateContext.cs            回调时间与身份
    FrameTimeSample.cs               宿主样本
    FrameUpdateOptions.cs            注册选项与默认解析
    FrameUpdateConfig.cs             配置快照
    FrameUpdateHandles.cs            Loop/Driver/Group/Scope/Update/Pause 句柄
  Scheduling/
    LoopState.cs                     驱动绑定、阶段协议、序号
    RegistrationStore.cs             引用身份键、槽位、代次
    PhaseScheduler.cs                资格快照、稳定顺序、预算
    UpdateSchedule.cs                频率值及到期运算
    MutationBuffer.cs                延迟变更和墓碑整理
  Lifecycle/
    GroupRegistry.cs                 组树、倍率及暂停汇总
    ScopeRegistry.cs                 生命周期树和递归释放
    PauseRegistry.cs                 原因集合和所有者清理
  Diagnostics/
    FrameUpdateDiagnostics.cs        指标与采样
    FrameUpdateStats.cs              查询 DTO
    FrameUpdateError.cs              有界错误记录
  Policies/
    IUpdateSchedulePolicy.cs         频率策略接口
    SchedulePolicyRunner.cs          独立低频评估适配

Core/FrameUpdate/Unity/
  WFrameWork.FrameUpdate.Unity.asmdef
  UnityFrameUpdateHost.cs            显式安装与宿主句柄
  UnityFrameUpdateDriver.cs          引擎回调转发
  UnityFrameUpdateSettings.cs        宿主设置
  UnityFrameRegistration.cs          owner 绑定及适配缓存
  SceneScopeRegistry.cs              场景与作用域映射
  ApplicationPausePolicy.cs          失焦/挂起策略
  FrameUpdateSettings.cs             可选 ScriptableObject 配置转换
  FrameUpdateBehaviour.cs            可选自动绑定基类

Samples~/FrameUpdate/
  ManualDriver/                     纯 C# 手动推进
  UnityLifecycle/                   owner/场景绑定
  GroupPause/                       跨循环暂停组
  DynamicSchedule/                  注入数据源的调频策略
  BatchSystem/                      一个注册项维护多个内部对象

Tests~/FrameUpdate/
  Core/                             独立 .NET 核心测试工程
  Unity/                            可导入的 Unity 测试程序集
  README.md                         运行、导入与指标采集步骤
```

Core 程序集不引用 Unity；Unity 程序集单向引用 Core。诊断纯数据归 Core，窗口或屏幕展示归 Unity/Editor。可选基类和配置资产在核心完成后再交付，业务接入不依赖继承基类。

## 3. 分阶段实施

### M1：公共契约与纯核心编译

- [x] 创建独立核心程序集及对应目录，保持现有 GFramework 程序集内容不变。
- [x] 实现选项、配置、枚举、样本、句柄和查询 DTO；落实可空选项与快照语义。
- [x] 建立独立测试工程，链接核心源文件；使用与 Unity 2022.3 兼容的 C# 和基础库 API。
- [x] 验证默认值、参数边界、句柄所属管理器与代次。

退出条件：核心编译通过；没有 Unity、具体输入库或网络 SDK 类型进入公开契约。

### M2：注册、动态变更与生命周期

- [x] 实现按引用身份、循环和阶段去重的注册表及槽位复用。
- [x] 实现逐项/批量注册、注销、启停、查询和安全边界变更。
- [x] 实现组树、作用域树、暂停原因集合及 Dispose。
- [x] 覆盖注册中注销、作用域清理与对象池旧句柄问题。

退出条件：R/L 类测试通过；重入时不会直接破坏正在遍历的集合。

### M3：多循环驱动与阶段协议

- [x] 实现 CreateLoop/BindDriver/Tick/RemoveLoop。
- [x] 独占驱动、阶段声明、序号、样本一致性、错误线程与重入校验。
- [x] 实现同一阶段稳定排序和阶段开始时的资格快照。
- [x] 支持跨循环交错、表现阶段跨 Update/LateUpdate、驱动解绑后重建。

退出条件：D 类测试通过；真实时钟和独立 Tick 来源保持分离。

### M4：时间与频率调度

- [x] 实现 EveryStep、AtFps、EveryNFrames、AtInterval 与初始错峰。
- [x] 实现有效时间累计、暂停剩余量、无漂移截止时间网格。
- [x] 明确零 delta、组倍率零、同值 SetSchedule、换组和禁用恢复的行为。
- [x] 实现逐项时间上限覆盖及 DiscardedDeltaTime。
- [x] 实现 Required/Deferrable 与软预算，不隐式承诺公平性。

退出条件：T/P 类测试通过；暂停恢复无墙钟补偿，低频更新交付完整累计时间或显式截断量。

### M5：诊断和错误处理

- [x] 实现 Basic 统计、渲染帧上报、循环 TickHz、分组及逐项计数；Detailed 枚举与扩展入口已保留。
- [x] 实现有界错误记录、DisableAndReport 与 Propagate。
- [x] 复用查询列表和采样缓冲；核心热路径使用单调计时器。
- [ ] 验证重复 RenderFrameId、零 delta、未驱动循环和异常中止的全部统计口径。

退出条件：O 类测试通过；数据可追溯到样本数量与计数定义。

### M6：Unity 默认宿主

- [ ] 实现 UnityFrameUpdateHost.Install，使用独立驱动 GameObject。
- [ ] 按主文档映射 Input/Simulation/Physics/Presentation 循环。
- [ ] 实现场景作用域、owner 存活检查及 OnDisable/OnDestroy 对称释放。
- [ ] 实现前后台策略、挂起后采样基线重置和 Domain Reload 关闭时的清理。
- [ ] 增加可选配置转换和生命周期辅助组件；所有生命周期行为显式记录。

退出条件：Unity 2022.3 编译与 U 类 EditMode/PlayMode 验证通过；既有模块无重复驱动。

### M7：扩展示例与性能基准

- [ ] 完成手动网络节拍示例和独立模拟驱动示例，只展示通用接入。
- [ ] 完成低频策略 runner，说明相同计划不重置周期及策略切换清理。
- [ ] 完成多个内部对象由一个注册项批量更新的示例。
- [ ] 运行空回调与轻量回调基准，比较集中调度、频率模式、诊断开关及高变更负载。
- [ ] 更新 API 文档、安装说明、已验证平台范围和剩余限制。

退出条件：示例可运行，性能报告记录环境和原始数据，所有未验证结论明确标注。

## 4. 验收矩阵

### 注册和生命周期

| ID | 输入场景 | 预期结果 |
|---|---|---|
| R01 | 同引用、同循环、同阶段、相同配置注册两次 | 相同句柄，每步回调一次 |
| R02 | 重复身份但修改组/频率/Lifetime | 明确冲突，原项不变 |
| R03 | 同对象注册不同循环或阶段 | 独立句柄与累计时间 |
| R04 | 同批中存在非法项或互相冲突的重复项 | 整批无新增，输出缓冲不包含部分成功 |
| R05 | A 更新中注销尚未执行的 B | B 本步不执行，其槽位在安全边界整理 |
| R06 | A 更新中注册 C | C 不进入当前遍历，下个所属阶段机会开始 |
| R07 | 注销后同对象立即重注册，再释放旧句柄 | 新注册不受旧句柄影响 |
| R08 | 用重写 Equals 的两个不同对象注册 | 仍被识别为两个引用身份 |
| R09 | 值类型 Target 或抛异常的 Lifetime 检查 | 值类型入口拒绝；检查异常按注册项错误策略处理 |
| L01 | 父 scope 下多个子 scope 和暂停请求一起释放 | 递归注销并释放对应暂停原因 |
| L02 | 已排队注册项的 scope 在激活前释放 | 项不会被激活 |
| L03 | 两个原因暂停同组，只释放一个原因 | 仍暂停；第二个释放后下次阶段恢复 |
| L04 | 已暂停组出现新对象 | 对象成功注册但不执行 |
| L05 | Tick 内 Dispose 管理器 | 当前回调返回，后续回调停止，旧句柄失效 |
| L06 | 假设测试槽位代次接近最大值并反复复用 | 溢出槽位退休，旧句柄不重新命中 |

### 驱动与时序

| ID | 输入场景 | 预期结果 |
|---|---|---|
| D01 | 一个循环重复 BindDriver | 第二次绑定失败 |
| D02 | 旧绑定释放后用更大 Sequence 调 Tick | 拒绝旧驱动 |
| D03 | All 驱动依次 Early/Normal/Late，使用同一样本 | 周期完成，只统计一个循环步 |
| D04 | 漏阶段、重复阶段、逆序、样本不一致或跳序号 | 本次在调用对象前拒绝 |
| D05 | 不同循环交错推进，Sequence 不同 | 各自合法，时间互不累加 |
| D06 | 注册项所在阶段不在驱动声明内 | 注册或绑定校验失败 |
| D07 | 同优先级、不同组的同阶段对象 | 按注册序号稳定执行 |
| D08 | 回调内递归 Tick 或后台线程直接 Tick | 拒绝，原状态可正常收尾 |

### 时间与频率

| ID | 输入场景 | 预期结果 |
|---|---|---|
| T01 | interval=0.1，输入 dt=0.04，再 dt=0.06 | 只回调一次，交付 dt≈0.1 |
| T02 | 上例两步间暂停，期间输入 dt=5 | 暂停时间不累计，恢复后仍交付≈0.1 |
| T03 | interval=0.1，首步 dt=0.25 | 一次回调≈0.25，下一时间网格点为≈0.3 |
| T04 | interval=0.1，同值 SetSchedule 每步调用 | 截止时间不被持续重置 |
| T05 | 每 N 步，offset 分散对象，发生预算延期 | 按整数步网格接续，累计时间保留 |
| T06 | 不同父子倍率组合、暂停某祖先 | 有效 delta 正确相乘，暂停项停表 |
| T07 | 源 delta=0 与组倍率=0 分别测试 | 前者 EveryStep 可执行；后者停止执行 |
| T08 | 一组时间源 Scaled，另一组 Unscaled | 同样本下分别使用正确来源 |
| T08a | 无 parent 创建 Unscaled 根，显式 parent 创建子组 | 根可独立选来源，子组来源继承；冲突声明拒绝 |
| T09 | raw=0.5，项上限=0.1 | Delta=0.1，Discarded=0.4，Elapsed 增加0.1 |
| T10 | A 回调申请暂停，使后面的 B 不执行 | B 保留阶段开始时已累计时间，恢复后不丢失 |
| T11 | 禁用、恢复、换组、修改周期组合 | 剩余时间及累计量符合主设计，组外时间不补算 |
| T12 | 可变步长长时间推进 AtFps/AtInterval | 量化误差受宿主机会限制，无逐次 reset 周期漂移 |
| P01 | 预算耗尽，后面包含 Required 与 Deferrable | 后续 Required 执行，Deferrable 延期 |
| P02 | 单个回调时间超过预算 | 完整返回，准确记录超预算，不能抢占 |
| P03 | 低优先级长期延期 | 延期计数和最长等待增长，不标记为已执行 |
| P04 | 动态创建循环后 SetPhaseBudget，Tick 内再次修改 | 独立阶段预算生效，当前 Tick 继续使用快照 |

### 错误和诊断

| ID | 输入场景 | 预期结果 |
|---|---|---|
| O01 | 一个回调抛异常，默认策略 | 消费已交付时间，禁用该项，其他项继续 |
| O02 | Propagate 模式中途抛异常 | 剩余项标记 Aborted，finally 清理完成，阶段记为结束 |
| O03 | 多循环多阶段共用一个渲染帧 | 渲染 FPS 不被回调次数放大 |
| O04 | 重复宿主 FrameId、零 delta、无渲染宿主 | 拒绝重复；无除零；无宿主显示无数据 |
| O05 | 错误条数超过 ErrorBufferCapacity | 缓冲有界，替换最旧记录 |
| O06 | 复用足够容量的输出 List 查询 | 查询不新建容器，内容为完整只读值快照 |
| O07 | 多固定步同一渲染帧到达 | TickHz 使用实际计数和实时时间窗口 |

### Unity 集成

| ID | 输入场景 | 预期结果 |
|---|---|---|
| U01 | 同一 manager 重复 Install | 拒绝，不产生第二个常驻驱动 |
| U02 | 一个渲染帧出现零个/多个 FixedUpdate | PhysicsLoop 分别收到零步/多步 |
| U03 | Presentation Early 在 Update，后续在 LateUpdate | 使用同一 Sequence 和时间样本 |
| U04 | Dynamic 输入模式，多物理步消费同一快照 | 连续状态可复用，边沿事件由示例一次消费 |
| U05 | owner 被 Destroy，未主动注销 | 生命周期辅助在回调前排除失效 owner |
| U06 | 对象池 disable/enable、同帧复用 | 新旧绑定互不误删，不双重回调 |
| U07 | 场景卸载与对象转常驻场景 | scope 清理正确，显式迁移对象保留 |
| U08 | 失焦与挂起两个原因重叠，再依次恢复 | 各自暂停句柄独立释放，恢复无时间跳跃 |
| U09 | 关闭 Domain Reload 后反复进入 Play | 静态 registry、宿主和事件订阅不累积 |
| U10 | 暂停 worldGroup，input/network 属于其他组 | 世界回调暂停，输入和网络仍可更新 |
| U11 | Time.timeScale=0，自动物理不再产生固定步 | Unscaled 注册不会凭空制造 PhysicsLoop Tick |
| U12 | Host Dispose 后 manager 继续存在 | 绑定撤销，未驱动状态可查，外部自定义循环不受误删 |

## 5. 性能测试方法

统一记录 Unity/运行时版本、构建后端、设备、构建模式与诊断级别。核心时间正确性测试用可控样本，预算测试用内部计时器替身，性能测试使用真实单调计时器和 Unity Profiler。

| 参数 | 基准取值 |
|---|---|
| EntryCount | 100、1,000、10,000 |
| Workload | 空回调、固定成本轻量回调、批量系统 |
| ScheduleMix | 全每步、间隔混合、错峰每 N 步 |
| MutationRate | 稳态无变更、低频增删、大批注册/清理 |
| Diagnostics | Off、Basic、Detailed |
| WarmupSteps | 至少覆盖容量初始化、JIT/运行时预热及统计窗口 |
| MeasurementWindow | 相同平台使用相同统计步数，记录平均值与 P95/P99 |

验收分开描述：预热后稳态框架 Tick 的 GC.Alloc 目标为零；注册、扩容、错误记录和显式报告允许分配但须可量化。CPU 指标展示自身调度成本和用户回调成本，不把减少调用数量直接当作单次调度更快。

高频短回调可使用每对象注册；大量极轻对象应比较批量系统注册。只有真实基准证据表明阶段扫描是瓶颈后，才考虑时间轮、堆或更复杂索引，并保持公开时序语义一致。

## 6. 安装与回退路径

首版按模块新增文件和独立 asmdef。Unity 适配通过显式 Install 接入，旧系统迁移时一次只指定一个驱动来源。接入 IFrameUpdate 后应关闭同一逻辑原来的原生 Update，避免逻辑执行两次。

每个迁移单元完成自身运行时回归后再继续。回退顺序为释放新增注册/驱动绑定，恢复原有驱动，确认常驻宿主和订阅已清理；不在两条路径同时启用时比较行为。

已执行：独立核心工程还原/构建通过，运行时测试 55/55 通过，包含注册、驱动、时间、生命周期、暂停、异常和基础诊断场景。尚未执行：Unity 2022.3 导入与 EditMode/PlayMode 验证、Unity 场景运行、真实性能基准和具体模块迁移；当前仓库是包目录，命令行探测还受到 Licensing Client access token unavailable 环境问题影响。

## 7. 完成定义

- [ ] 纯核心测试和 Unity 目标版本编译通过。
- [ ] 注册、时序、暂停、生命周期与错误语义都有可复现测试。
- [ ] 默认 Unity 驱动与手动驱动示例独立可运行。
- [ ] 没有具体玩法或网络 SDK 类型进入核心。
- [ ] 稳态分配与调度开销有可复现记录，诊断数据含清晰口径。
- [ ] API、配置、示例、程序集关系和限制与最终代码一致。
- [ ] 未完成的可选扩展明确留在扩展范围，不以占位代码冒充实现。

维护约定：主设计记录规范，本文记录实施与验收。实现改变规范时先同时更新两份 Markdown，再重新生成离线阅读页。
