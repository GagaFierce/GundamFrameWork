# Core 回归修复记录（2026-10-09）

本轮将历史审查中的并发和生命周期复现迁入 `Tests~/Modules/Core/RegressionTests.cs`，测试使用受控 `TaskCompletionSource`、释放屏障和独立临时目录，不依赖随机延时或真实用户存档。

## 修复状态

| 类别 | 状态 | 关键保证 |
| --- | --- | --- |
| Save 临时文件取消竞态 | 已修复并验证 | 每次写入独立临时文件；仅创建者清理；未获锁的取消不会删除其他写入 |
| Save 路径越界 | 已修复并验证 | 规范化后检查相对路径、非法段和根目录边界；拒绝请求无文件副作用 |
| 应用关闭顺序 | 已修复并验证 | 先关闭业务作用域归还租约，再关闭资源服务，最后处理 FrameUpdate/主线程入口 |
| 实例加载取消后的迟到释放 | 已修复并验证 | 从请求接收至释放完成持续计入关闭生命周期；后端最后关闭 |
| UI 关闭与重开 | 已修复并验证 | Entry/Handle 共享真实关闭任务；Closing 使用新 generation；CloseAll 使用稳定快照并等待销毁 |
| 编辑器注释工具 | 已修复并验证 | 删除固定行截断；纯文本转换幂等；保留源码、BOM/换行，并在批量写入前确认、写入前备份 |
| 作用域和任务留存 | 已修复并验证 | 子作用域关闭后从父集合移除；完成任务从活动集合移除；快照使用稳定副本 |
| 取消回调异常 | 已修复并验证 | 先建立共享关闭任务；取消回调异常聚合后继续必要清理 |
| BGM 旧请求淘汰新请求 | 已修复并验证 | 资源准备与播放提交分离；序号检查和 BGM 限制提交在同一边界 |
| Actions 回调修改集合 | 已修复并验证 | 回调后按对象身份移除；清空、移除自身和新增动作不会使用失效下标 |
| 对象池所有权 | 已修复并验证 | `_owned`/`_leased` 使用引用身份；拒绝外来、重复和工厂重复实例 |
| 关闭对象池预热 | 已修复并验证 | 关闭后所有创建入口拒绝；销毁异常聚合并继续清理剩余对象 |
| Input 无关上下文变化 | 已修复并验证 | 仅作废可见性实际变化的动作事件；非阻挡上下文保留其他动作的固定步事件 |

## 实际验证

- `dotnet run --project Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj`：55/55。
- `dotnet run --project Tests~/Modules/Core/GFramework.Modules.Tests.csproj`：本轮基线 45/45；最终 53/53。
- 历史复现程序：10/10；补充资源/对象池/输入复现：4/4。
- `Validation~/validate-package.ps1`：包含上述两组 .NET 测试、asmdef 名称/引用/环检查；未传 Unity 参数时会明确跳过 Unity 阶段，传入 Unity 参数时会检查真实进程退出码和日志成功标记。

本机实际启动了 Unity 2022.3.62f3 临时宿主并完成许可证握手，但 UPM 因正式包依赖 TextMeshPro 3.0.9 无法解析而失败；真实 UPM 导入、Addressables 内容、PlayMode、Domain Reload 和 Player 构建仍需在依赖可解析的宿主中执行，不能由纯 .NET 结果替代。

## 本轮复查修复（7 项）

本轮根据 `review-round2.md` 的 7 个可复现边界问题完成了最小实现修复，并新增 `Tests~/Modules/Core/FollowupRegressionTests.cs` 正式断言测试。新增测试使用受控 `TaskCompletionSource`、释放屏障和 2 秒等待上限；异常路径在 `finally` 中释放屏障，不依赖随机 sleep。

| 问题 | 修改与保证 | 正式结果 |
| --- | --- | --- |
| 实例加载失败后无法关闭 | `ResourceService.AwaitInstanceLease` 将首次等待纳入生命周期收尾；失败、取消、预取消和迟到成功都等待实际释放；实例释放也进入关闭错误聚合 | 通过：同步 throw、故障 Task、延迟失败、底层取消、调用方取消、迟到成功、成功租约与混合关闭 |
| 初始化失败回滚死锁 | `GameRuntime.InitializeCoreAsync` 先关闭 `ApplicationScope`，再回滚已启动 Owned parts；共享 Scope/Shutdown 任务并保留初始化与清理异常 | 通过：应用/子作用域租约、外部并发 Shutdown、清理异常与重复关闭 |
| 预取消 Single 场景产生副作用 | `SceneFlowService` 在预留、卸载旧场景和提交后端加载前检查取消；提交后的迟到结果仍由清理任务释放 | 通过：Single/Additive 预取消、进行中取消、迟到成功释放、取消后新请求 |
| Single 面板连续重开突破限制 | 按 generation 状态选择最新 Loading/Visible/Hidden 实例，跳过 Closing 历史 generation；CloseAll 仍等待稳定快照中的全部 generation | 通过：A/B/C 重开合并、Loading 合并、旧关闭先后、失败重试、CloseAll |
| 资源释放失败未传递到关闭 | 资源释放同时记录 pending 状态和最终结果，关闭期间收集有界错误并继续独立清理，后端关闭错误也保留 | 通过：关闭前失败、关闭中异步失败、多资源部分失败 |
| 注释工具破坏 UTF-16 | `TransformUtf8` 改为严格 UTF-8；新增 `TransformSource` 识别并保持 UTF-8/UTF-8 BOM/UTF-16 LE/BE BOM；菜单预览和写入共用转换结果 | 通过：四种编码、非法 UTF-8、中文、CRLF、幂等和失败不写入 |
| 对象池 reset 重入关闭 | `_returning` 保持归还中对象的所有权；回调结束重新检查关闭状态；关闭、reset/clear/return/destroy 重入均避免重复销毁 | 通过：reset Dispose/Clear/Return、关闭后抛错、销毁抛错、工厂重入和计数一致 |

### 本轮实际验证

- FrameUpdate：`55/55`。
- Modules：`53/53`（原有 `45/45` 加本轮 8 个正式回归入口）。
- `Validation~/validate-package.ps1`：通过纯 C# 测试、asmdef 名称检查、Runtime 配置检查和依赖环检查；未传宿主参数时明确跳过 Unity 阶段。
- 工作树原有未提交修改均保留，未执行回退、清理、提交或推送。
- 本机检测到 Unity `2022.3.62f3` 和 `2022.3.62f3c1` 可执行文件，但当前仓库只有 UPM 包，没有完整 Unity 宿主（`Assets/ProjectSettings/Packages`）。因此本轮未宣称 Unity 编译、Addressables 内容、PlayMode 或 Player 验收通过；此前临时宿主仍受 `TextMeshPro 3.0.9` 依赖解析问题阻塞。

## 第三轮异步生命周期修复（直接实施）

- 资产加载增加独立的结果发布和完整生命周期。调用方只能在结果已登记后取得租约；最后一个等待者取消后，仍持续跟踪底层加载、结果观察和实际释放。持有中的空闲租约不计入待执行操作统计，但关闭会等待其归还。
- 初始化收到关闭请求后正常返回的模块也纳入已启动模块清单，统一先关闭业务作用域，再回滚 Owned 模块；Borrowed 模块不被关闭。
- 音频跨线程释放使用最外层 `finally` 归还操作计数。清理失败会被记入有界错误集合，关闭继续处理其他播放和后端，最终返回聚合错误，而不是卡住或伪报成功。
- SceneFlow 在操作开始前检查调用方取消；开始后独立取消调用方等待，后端使用不被调用方截断的令牌，继续由 SceneFlow 等待原生加载及迟到卸载。AddressablesSceneBackend 直接收到取消时也会等待自身清理后再返回。

新增 `AsyncLifecycleRegressionTests.cs`，包含 6 个正式回归入口：资产续执行被延迟、共享资产发布、取消后释放失败、迟到初始化成功（Owned/Borrowed）、音频释放失败后的继续清理、场景原生加载/卸载协议。资产调度窗口在独立子进程内确定性复现，不修改主测试进程的 ThreadPool 设置。

新增 Unity PlayMode 用例 `FrameworkCanceledSceneLoadDrainsBeforeShutdown`，检查取消场景加载后关闭完成时原生场景数量恢复基线；需要生成并构建本地 Addressables 内容。

本轮 Modules 为 **59/59**，保留原有全部回归。真实尝试启动 Unity 2022.3.62f3 临时宿主，退出码为 1：UPM 在包解析阶段报告 `The "path" argument must be of type string. Received undefined. No packages loaded.`，未进入包编译。该结果是环境阻塞，不能据此声明 Unity 编译或新增 PlayMode 用例已通过。
