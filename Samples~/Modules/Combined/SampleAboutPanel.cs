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

        protected override AboutViewModel CreateViewModel(object argument)
        { return new AboutViewModel("GundamFrameWork Combined Sample", Application.version, "UI MVVM、Addressables 和应用生命周期示例。"); }

        protected override void Bind(AboutViewModel viewModel, UiBindingSet bindings)
        {
            if (productName != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.ProductName), () => viewModel.ProductName, value => productName.text = value));
            if (version != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Version), () => viewModel.Version, value => version.text = value));
            if (description != null) bindings.Add(UiControlBindings.Text(viewModel, nameof(viewModel.Description), () => viewModel.Description, value => description.text = value));
        }
    }
}
