using System.Text;
using TMPro;
using UnityEngine;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleHelpPanel : UiPanelViewBehaviour<HelpViewModel>
    {
        [SerializeField] private TMP_Text content;
        [SerializeField] private UnityEngine.UI.Button close;

        protected override HelpViewModel CreateViewModel(object argument)
        {
            var services = argument as CombinedSampleRuntimeServices;
            if (services == null) throw new System.InvalidOperationException("CombinedSampleRuntimeServices is required by help.");
            return new HelpViewModel(services.GetHelpItems(), services);
        }

        protected override void Bind(HelpViewModel viewModel, UiBindingSet bindings)
        {
            if (close != null) bindings.Add(UiControlBindings.Button(close, viewModel.BackCommand));
            if (content == null) return;
            bindings.Add(UiBinding.Collection(viewModel.Items, () =>
            {
                var text = new StringBuilder();
                for (int i = 0; i < viewModel.Items.Count; i++)
                {
                    if (i > 0) text.Append('\n');
                    text.Append(viewModel.Items[i].Action).Append(": ").Append(viewModel.Items[i].Description);
                }
                content.text = text.ToString();
            }));
        }
    }
}
