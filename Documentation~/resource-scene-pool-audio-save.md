# GFramework 资源与运行时服务

## 版本与依赖

包声明 `com.unity.addressables: 1.22.3`，目标 Unity 版本为 2022.3。Addressables 1.22.3 的 `Addressables.InitializeAsync(false)`、`LoadAssetAsync<T>`、`InstantiateAsync`、`LoadSceneAsync` 和 `ReleaseInstance` API 是本包适配的契约；包不声明或使用未经验证的 2.x API。实际兼容性仍需在宿主 Unity 2022.3 编辑器中验证。

Addressables Settings、Groups、Catalog 和内容构建由宿主 Unity 工程管理。UPM 包内的地址字符串不会自动成为宿主工程的 Addressables 内容。

## 程序集依赖

```text
Diagnostics
  ├─ Input ── Input.Unity
  ├─ ResLoad ── ResLoad.Unity (Unity.Addressables)
  ├─ Pool ── Pool.Unity ── ResLoad
  ├─ Scene ── Scene.Unity (Unity.Addressables)
  ├─ Audio ── Audio.Unity ── ResLoad
  └─ Config ── Config.Unity ── ResLoad

FrameUpdate ── FrameUpdate.Unity
UI ── UI.Unity ── ResLoad / ResLoad.Unity
Input + UI ── Input.UIBridge
Physics + Input ── Physics.InputBridge
```

`FrameUpdate`、`Input`、`Physics`、`UI`、`ResLoad`、`Pool`、`Scene`、`Audio`、`Config` 的 Runtime 程序集均不引用 UnityEngine；Unity API 只在对应 Unity 程序集中出现。FrameUpdate 仍只有一个显式 `UnityFrameUpdateHost`。

## 初始化与释放顺序

1. 综合装配使用 `UnityGameRuntime.Create()` 创建唯一的 FrameUpdate、Host 和主线程边界。
2. `GameRuntime` 先初始化 `AddressablesResourceService`，再按依赖顺序初始化 Scene、Pool、Audio、Save、Input、Physics 和 UI。
3. 运行时关闭先等待 UI/Physics/Input/Save/Audio/Pool/Scene 的使用者退出，最后等待并关闭 `ResourceService`。
4. 独立模块示例可以自建局部运行时，但必须明确其所有权；不能由 UI、音频各自隐式创建应用级资源服务。

## Addressables 所有权表

| 创建方式 | 持有者 | 释放方式 |
| --- | --- | --- |
| `ResourceService.LoadAssetAsync<T>` | 每个调用方一个 `ResourceLease<T>` | 调用方 `Dispose`；最后一个租约释放 Addressables handle |
| `ResourceService.InstantiateAsync` | 一个 `ResourceInstanceLease` | 租约 `Dispose`，Unity 适配使用 `Addressables.ReleaseInstance(handle)` |
| `AddressableGameObjectPool` prefab | 池的 prefab `ResourceLease<GameObject>` | `CloseAsync` 清理可用及租用对象后释放 prefab 租约 |
| Addressables 场景 | `SceneLease` | `SceneLease.Dispose`，Unity 适配使用 `UnloadSceneAsync(handle, true)` |
| UI prefab 实例 | `UGuiPanelInstance` | `Destroy(instance)`；UI 资源租约单独 Dispose |
| AudioClip | AudioService 的共享 clip entry | 最后一个播放生命周期结束后释放 clip 租约 |

不要对手动 `Object.Instantiate` 的对象调用 `ReleaseInstance`，也不要对 `InstantiateAsync` 的实例手动 `Destroy` 后再 ReleaseInstance。Unity `Destroy` 为延迟销毁，池关闭使用 `CloseAsync` 等待一个调度机会后才释放 prefab 依赖。

## Resources 迁移

`ResourcesManagerImpl` 是历史 public API，保留用于迁移期兼容；它不再被 UI、场景、池或音频新入口调用。旧的 `Resources` 路径不是 Addressables address，必须在宿主工程显式建立映射：

```text
Resources/UI/Settings.prefab  ->  GFramework.Samples.Settings
Resources/Scenes/Game.unity   ->  GFramework.Samples.Game
Resources/Audio/click.wav     ->  GFramework.Samples.Audio.Click
```

迁移步骤：把资产加入宿主 Addressables Group，设置稳定 address；将旧调用替换为 `LoadAssetAsync<T>`/`InstantiateAsync`；把旧的 `UnloadResources` 替换为对应租约或实例释放。同步旧方法不能伪装成异步，也不再自动调用 `WaitForCompletion`。

## 场景、池、音频和存档

- `SceneFlowService` 支持 Single/Additive、加载状态、进度、失败状态和显式 `SceneLease`；并发切换请求采用拒绝策略，调用方应等待当前操作完成；启动场景不应伪造为 Addressables 场景。
- `ObjectPool<T>` 拒绝跨池和重复归还，关闭后归还的租用对象直接销毁；`AddressableGameObjectPool` 的 prefab 必须先 Warmup。
- `AudioService` 按 BGM/Sfx/UI 保存音量、静音和并发限制；`AudioServiceBehaviour` 统一 Tick 复用 AudioSource，不创建每个音效一个 MonoBehaviour。BGM 使用请求序号，旧异步结果不会覆盖最新请求。
- `SaveService<T>` 只写 `Application.persistentDataPath` 下的相对文件名，使用临时文件和 backup；读主文件失败会尝试 `.bak`，再回退默认值。版本迁移通过 `IUserDataSerializer<T>.Migrate` 注入，写入按存档 key 串行化。

## 综合样例

导入 `Combined modules` Sample 后，在宿主工程执行：

1. `GFramework/Samples/Generate Combined Sample Assets`。
2. 确认宿主已有 Addressables Settings；生成器只创建 `Assets/GFrameworkSamples/Combined` 及名为 `GFramework Samples` 的组，并注册 Player、Settings、Menu、Config 和 Gameplay scene 地址。
3. `GFramework/Samples/Build Combined Addressables Content`。
4. 打开生成的 `Assets/GFrameworkSamples/Combined/Scenes/CombinedSample.unity` 并运行。

样例入口 `CombinedSampleBootstrap` 展示输入、固定步跳跃、UI 模态、返回和 Gameplay 恢复；`CombinedSampleRuntimeServices` 展示 Addressables 初始化、场景服务、池、音频和 persistentDataPath 设置存档。Escape 在 UI context 下关闭设置，Space 只有 `Pressed` 固定步事件会跳跃。

## 验证命令

```powershell
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj"
dotnet run --project "D:/wangjian/Project/GundamFrameWork/Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
```

纯逻辑测试使用可控 TaskCompletionSource 和可记录 release 次数的 backend；当前结果为 Modules `28/28`、FrameUpdate `55/55`。Unity EditMode/PlayMode 和构建后的本地 Addressables 内容必须在宿主工程执行；当前仓库不是完整 Unity 工程，不能仅靠 .NET 测试宣称 Unity Addressables 已运行验收。
