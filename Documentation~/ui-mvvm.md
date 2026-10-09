# UI MVVM 绑定层

本模块采用四层职责：

- Model/服务：设置、GameFlow、音频、存档和导航适配器。
- ViewModel：`ViewModelBase`、命令、校验、草稿和异步状态；不引用 UnityEngine 或控件。
- View：`UiPanelViewBehaviour<TViewModel>` 与 uGUI/TMP 引用，只负责创建 VM 和注册绑定。
- Binding：`UiBindingSet` 与 `UiControlBindings` 管理属性同步、控件事件和解绑。

## 接入步骤

1. 让面板脚本继承 `UiPanelViewBehaviour<SomeViewModel>`。
2. 在 `CreateViewModel` 中注入窄接口，不在 VM 内查找场景对象或全局服务。
3. 在 `Bind` 中使用 `Bindings.Add(UiControlBindings...)`，属性名使用 `nameof(ViewModel.Property)`，getter/setter 显式传入。
4. 面板关闭时基类自动移除 Button/Slider/Toggle/Dropdown/InputField 监听、属性订阅和集合订阅，并取消 VM 的 `LifetimeToken`。

示例：

```csharp
protected override void Bind(SettingsViewModel vm, UiBindingSet bindings)
{
    bindings.Add(UiControlBindings.Slider(vm, nameof(vm.MasterVolume),
        () => vm.MasterVolume, value => vm.MasterVolume = value, masterSlider));
    bindings.Add(UiControlBindings.Button(applyButton, vm.ApplyCommand));
}
```

双向绑定使用 `SetValueWithoutNotify`/等价控件 API，避免 View → VM → View 的循环触发。动态列表使用 `UiObservableList<T>` 的变化事件，不能在 `Update` 中遍历整个 ViewModel。

## 生命周期规则

每一次 `UiPanelManager.OpenAsync` 的新 generation 都创建新的 ViewModel。`AsyncUiCommand` 同一实例只允许一个执行中的任务，重复点击复用当前任务；关闭面板会取消 VM 任务，`ViewModelBase` 在 disposed 后不再发出属性通知。GameFlow/场景加载等应用级服务仍由 `CombinedSampleRuntimeServices` 持有，不随按钮对象销毁。

## 示例流程

执行以下菜单：

1. `GFramework/Samples/Generate Combined Sample Assets`
2. `GFramework/Samples/Validate And Build Combined Addressables`
3. 打开 `Assets/GFrameworkSamples/Combined/Scenes/CombinedSample.unity` 并运行。

大厅和设置是实际绑定入口；设置草稿在修改时预览，应用保存失败会保留草稿和错误文本，取消会回滚预览。Dialog 用一次性结果协调退出确认。

## 验证

纯 C#：

```powershell
dotnet run --project "Tests~/FrameUpdate/Core/FrameUpdate.Core.Tests.csproj"
dotnet run --project "Tests~/Modules/Core/GFramework.Modules.Tests.csproj"
```

Unity UI/Addressables 测试必须在宿主 Unity 2022.3 项目中执行；包仓库本身不包含宿主的 `ProjectSettings`。至少验证 prefab 引用、TMP 中文显示、CanvasScaler/安全区、模态焦点、反复打开关闭以及关闭后的监听和 Addressables 租约。
