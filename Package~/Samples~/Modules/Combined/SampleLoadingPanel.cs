using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleLoadingPanel : UiPanelViewBehaviour<LoadingViewModel>
    {
        [SerializeField] private TMP_Text stage, error;
        [SerializeField] private Slider progress;
        [SerializeField] private Button cancel, retry, back;
        protected override LoadingViewModel CreateViewModel(object argument) => argument as LoadingViewModel ?? throw new System.ArgumentException("Loading operation required.");
        protected override void Bind(LoadingViewModel vm, UiBindingSet bindings)
        {
            if (stage != null) bindings.Add(UiControlBindings.Text(vm, nameof(vm.Stage), () => vm.Stage == "Complete" ? "加载完成" : vm.Stage == "Canceled" ? "已取消" : vm.Stage == "Failed" ? "加载失败" : vm.Stage, stage));
            if (error != null) bindings.Add(UiControlBindings.Text(vm, nameof(vm.Error), () => vm.Error, error));
            if (progress != null)
            {
                bindings.Add(UiBinding.OneWay(vm, nameof(vm.Progress), () => vm.Progress, progress.SetValueWithoutNotify));
                bindings.Add(UiControlBindings.Visible(vm, nameof(vm.HasProgress), () => vm.HasProgress, progress.gameObject));
            }
            if (cancel != null) bindings.Add(UiControlBindings.Button(cancel, vm.CancelCommand));
            if (retry != null)
            {
                bindings.Add(UiControlBindings.Button(retry, vm.RetryCommand));
                bindings.Add(UiControlBindings.Visible(vm, nameof(vm.CanRetry), () => vm.CanRetry, retry.gameObject));
            }
            if (back != null) bindings.Add(UiControlBindings.Button(back, vm.BackCommand));
        }
    }
}
