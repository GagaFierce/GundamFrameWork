using TMPro;
using UnityEngine;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleAboutPanel : UiPanelViewBehaviour<AboutViewModel>
    {
        [SerializeField] private TMP_Text productName;
        [SerializeField] private TMP_Text version;
        [SerializeField] private TMP_Text description;
        [SerializeField] private UnityEngine.UI.Button close;

        protected override AboutViewModel CreateViewModel(object argument)
        { return new AboutViewModel("GundamFrameWork", UnityEngine.Application.version, "通用界面与游戏流程示例。", argument as IUiNavigationService); }

        protected override void Bind(AboutViewModel viewModel, UiBindingSet bindings)
        {
            if (productName != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.ProductName), () => viewModel.ProductName, productName));
            if (version != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Version), () => viewModel.Version, version));
            if (description != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Description), () => viewModel.Description, description));
            if (close != null) bindings.Add(UiControlBindings.Button(close, viewModel.BackCommand));
        }
    }
}
