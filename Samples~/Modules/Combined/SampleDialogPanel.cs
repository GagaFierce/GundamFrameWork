using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleDialogPanel : UiPanelViewBehaviour<DialogViewModel>
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirm;
        [SerializeField] private Button cancel;
        [SerializeField] private Button alternative;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private TMP_Text cancelLabel;
        [SerializeField] private TMP_Text alternativeLabel;

        protected override DialogViewModel CreateViewModel(object argument)
        { return argument as DialogViewModel ?? new DialogViewModel(new UiDialogRequest("确认", "请确认操作。")); }

        protected override void Bind(DialogViewModel viewModel, UiBindingSet bindings)
        {
            if (titleText != null) titleText.text = viewModel.Request.Title;
            if (messageText != null) messageText.text = viewModel.Request.Message;
            if (confirmLabel != null) confirmLabel.text = viewModel.Request.ConfirmLabel;
            if (cancelLabel != null) cancelLabel.text = viewModel.Request.CancelLabel;
            if (alternativeLabel != null) alternativeLabel.text = viewModel.Request.AlternativeLabel ?? string.Empty;
            if (confirm != null) bindings.Add(UiControlBindings.Button(confirm, viewModel.ConfirmCommand));
            if (cancel != null) bindings.Add(UiControlBindings.Button(cancel, viewModel.CancelCommand));
            if (alternative != null) bindings.Add(UiControlBindings.Button(alternative, viewModel.AlternativeCommand));
            if (alternative != null) bindings.Add(UiControlBindings.Visible(viewModel, nameof(viewModel.Request), () => !string.IsNullOrEmpty(viewModel.Request.AlternativeLabel), alternative.gameObject));
        }
    }
}
