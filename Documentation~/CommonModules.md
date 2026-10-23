# 通用模块：计时、状态、事件与 UI 组件

适用 Unity 2022.3。新增模块保留旧 `ActionManager`、`DelayAction`、`EventManager` 和 `GameFlowService` 接口。所有新增运行时服务及句柄均在主线程调用，不创建线程或全局单例。

## 1. 计时器与任务序列

程序集：`WFrameWork.Scheduling`；命名空间：`WFrameWork.Scheduling`。

### 注册与释放

复用应用已有的 `FrameUpdateManager` 和 `UnityFrameUpdateHost`，每个时间组注册一个调度器。UI 可使用不受 `Time.timeScale` 影响的时间组；玩法使用现有玩法组。不要同时手动 `Tick` 和注册帧更新。

```csharp
using WFrameWork.Application;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.Scheduling;

// manager、host 为现有应用对象；this 为持有该服务的 MonoBehaviour。
var lifetime = new RuntimeScope("Screen");
var uiGroup = manager.CreateGroup("UiTimers",
    new UpdateGroupOptions { TimeSource = UpdateTimeSource.Unscaled });
var timers = new TimerScheduler(); // 可传入项目的 IDiagnosticSink
lifetime.Own(timers);
lifetime.Own(UnityFrameRegistration.Bind(manager, timers, this,
    new FrameUpdateOptions { Loop = host.Loops.Presentation, Group = uiGroup }));

var timer = timers.Delay(2, () => toast.SetActive(false));
timer.Pause();
timer.Resume();
// timer.Cancel() 或 timer.Dispose() 都会取消并立即释放回调引用。

var pulse = timers.Repeat(1, RefreshCountdown, count: 10);
// count = -1 为无限循环；首次回调在一个间隔之后。

var sequence = timers.Start(new TaskSequence()
    .Do(ShowReady)
    .Wait(1)
    .Do(ShowGo)
    .WaitUntil(() => player.IsReady)
    .Do(StartPlaying));

// 若 timers 是全局服务，可只让页面持有自己的任务。
lifetime.Own(sequence);

// 页面销毁/关闭时：先解绑帧更新，再取消此作用域持有的任务。
await lifetime.CloseAsync();
manager.RemoveGroup(uiGroup);
```

行为约定：

- `Delay(0)` 在下一次调度更新执行，回调中新建任务也从下一次更新开始。
- 循环每次更新至多执行一次，长帧跳过已错过的次数并保持下一次触发相位，避免补执行造成卡顿。
- 句柄可暂停、恢复、取消；`Status` 区分完成、取消与异常，`Error` 保存异常。单个任务异常不会阻断其他任务。
- 可传 `IUpdateLifetime`，调度检查发现对象已失效时取消任务。需要立即清理时，使用 `RuntimeScope.Own(handle)`。
- 任务序列开始时复制定义，可以复用同一定义。连续时间等待会传递本帧剩余时间；条件等待通过后不把之前的时间计入后续等待。
- 每个序列每次更新最多执行 64 个步骤，超过后下次继续，超出该预算的本帧剩余时间不累积。
- `Do` 是同步动作。异步业务可启动已有异步流程后通过 `WaitUntil` 等待业务状态；不要把 `async void` 传入 `Do`。
- 单独移除帧注册只会停止驱动；释放调度器/句柄才会取消任务和释放捕获引用。组暂停期间不累计补执行时间。

## 2. 通用状态机

程序集和命名空间：`WFrameWork.StateMachine`。

```csharp
var machine = new StateMachine<ActorState, ActorContext>(context);
machine.AddState(ActorState.Idle, new DelegateState<ActorContext>(
    enter: c => c.PlayIdle(),
    tick: (c, dt) => c.LookForTarget(),
    exit: c => c.StopIdle()));
machine.AddState(ActorState.Move, new MoveState()); // 实现 IState<ActorContext>
machine.AddTransition(ActorState.Idle, ActorState.Move, c => c.HasMoveInput);
machine.ChangeState(ActorState.Idle);

// 同样可以通过 UnityFrameRegistration.Bind 注册到现有玩法时间组。
lifetime.Own(machine);
lifetime.Own(UnityFrameRegistration.Bind(manager, machine, this,
    new FrameUpdateOptions { Loop = host.Loops.Simulation, Group = gameplayGroup }));
```

- 状态提供 `Enter / Tick / Exit`，共享显式上下文；也可以使用枚举、字符串等作为状态键。
- 每次更新先按注册顺序检查当前状态的条件，首个满足的条件生效，再更新切换后的状态。
- 当前状态重复切入不重启。回调中的切换请求延后处理，同一回调多次请求以最后一个为准。
- `Stop()` 退出当前状态，可重新启动；`Dispose()` 退出并永久释放状态定义。
- 回调异常会尝试退出当前状态一次，保存 `Error` 并向调用者传播；进入/退出反复切换超过 32 次会停止并报错。
- 状态与条件在停止时配置。状态机不拥有上下文及其外部资源；状态需要释放的资源应在 `Exit` 中释放。

## 3. 强类型事件

程序集和命名空间：`WFrameWork.Events`。新服务独立于旧整数消息系统，业务可逐步接入。

```csharp
public readonly struct HealthChanged
{
    public readonly int Current;
    public HealthChanged(int current) { Current = current; }
}

var events = new EventBus(diagnosticSink);
lifetime.Own(events); // 应用级总线由应用作用域持有即可
var subscription = events.Subscribe<HealthChanged>(e => healthLabel.text = e.Current.ToString());
pageLifetime.Own(subscription); // 页面关闭时自动退订
events.Publish(new HealthChanged(90));
```

- 同步按订阅顺序通知，按事件的准确泛型类型隔离，不使用整数消息号或 `object` 强转。
- 每次订阅产生独立的 `IDisposable`，同一委托重复订阅会收到多次通知。
- 派发中退订立即生效；新订阅不参与已经开始的那次派发，可以参加之后的嵌套派发。
- 单个监听器异常报告给诊断服务，其余监听器继续执行；同类型递归派发限制为 64 层。
- 总线释放后清除全部订阅，禁止继续发布/订阅。没有全局静态事件残留。

## 4. 通用 UI 组件

命名空间：`WFrameWork.UI.Unity`，沿用 `WFrameWork.UI.Unity` 程序集。

### 固定行高虚拟列表

结构：`ScrollRect > Viewport（RectMask2D）> Content > RowTemplate`。
Content 必须是 Viewport 的直接子节点，关闭 Content 上的 LayoutGroup 和 ContentSizeFitter。行模板可包含普通 uGUI/TMP 元素。

```csharp
list.Initialize(scrollRect, rowTemplate, height: 48, gap: 4, extraRows: 1);
list.SetItems(items.Count,
    (row, index) => row.GetComponent<ShopRow>().Bind(items[index]),
    (row, index) => row.GetComponent<ShopRow>().Unbind());
list.ScrollTo(100);
// 数据原位变化：list.RefreshItems(); 数量变化：重新 SetItems。
```

只创建可见行与少量缓冲行，滚动一行只重绑新出现的行。池保留历史最大视口所需的行数；关闭列表释放所有行绑定，重新开启恢复。销毁列表释放克隆对象。数据缩短会夹紧滚动位置。

`Bind` 必须覆盖全部视觉数据；`Unbind` 清理该行的按钮监听、绑定句柄和资源租约。不可在绑定回调中替换列表数据。首版范围为纵向、固定行高列表；网格、横向和动态行高需要单独扩展。

### 长按

把 `UiLongPress` 挂到可接收射线的 UI 对象上，调用：

```csharp
longPress.Initialize(uiTimers, delay: 0.5f, repeat: 0.1f);
longPress.Pressed.AddListener(AddOne);
```

`repeat: 0` 只触发一次。离开、松开、失焦、后台切换或禁用后取消；多指分别识别。同对象上存在 `Selectable` 时遵守其可交互状态。计时来自注入的调度器，不另建更新循环。长按与 `Button.onClick` 是独立事件；需要互斥时由业务决定点击行为。

### 拖拽

`UiDragHandle` 默认拖动自身，也可以 `Configure(target, canvasGroup, clamp: true)` 作为窗口标题栏。支持父节点平面坐标转换、保留按下偏移、限制在父区域、拖动时关闭指定 CanvasGroup 的射线阻挡并在结束/禁用/失焦后恢复。提供 `DragStarted / DragEnded` 和 `CancelDrag(restorePosition: true)`。

### 提示浮层

在上层 Canvas 创建常驻 `UiTooltipView`，设置 TMP 文本、背景和所需的布局组件。通过 CanvasGroup 显隐，默认不拦截射线。将 `UiTooltipTrigger` 挂到需要提示的控件，调用 `Configure(view, "说明文字")`，支持鼠标悬停与键盘/手柄选择。共享浮层通过拥有者区分隐藏请求，旧控件退出不会误关新控件的提示，位置限制在父区域内。

### 按钮防重复提交（复用现有能力）

```csharp
var command = new AsyncUiCommand(token => SubmitAsync(token));
pageBindings.Add(command);
pageBindings.Add(UiControlBindings.Button(submitButton, command, busyIndicator));
```

同一命令实例执行期间重复调用返回同一任务，按钮自动禁用并显示忙碌状态；释放绑定时移除监听，释放命令时请求取消。避免另挂直接提交业务的 `Button.onClick`，否则该独立监听不受命令保护。

## 验证与回滚

- 自动测试：宿主工程 `Assets/GFrameworkTests/Editor`，程序集 `GFramework.Common.Tests`。
- Unity Test Runner 运行 EditMode 测试；命令行可运行 `Validation~/validate-common.ps1`。
- 本机包管理器不可用时，可加 `-Isolated`：复制源码及宿主已缓存的测试依赖到临时工程，在 Unity 内运行同一套测试。该方式不等同于原宿主的 UPM 集成验证。
- 手动体验时补查不同 CanvasScaler、触控设备、实际列表行绑定和页面关闭行为；自动测试不等同于移动端 Player 验证。
- 旧 API、序列化字段和资源地址没有迁移。要回滚本次扩展，移除新增三个模块、五个 UI 组件及其引用、文档和测试即可；已有程序集不依赖新增状态机或事件服务。
