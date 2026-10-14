using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleResultContext
    {
        public readonly CombinedSampleRuntimeServices Services;
        public readonly UiResultData Data;
        public SampleResultContext(CombinedSampleRuntimeServices services, UiResultData data) { Services = services; Data = data; }
    }
    public sealed class SampleResultPanel : UiPanelViewBehaviour<ResultViewModel>
    {
        [SerializeField] private TMP_Text title, description, statistics;
        [SerializeField] private Button restart, lobby, next;
        protected override ResultViewModel CreateViewModel(object argument)
        {
            var context = argument as SampleResultContext ?? throw new System.ArgumentException("Result context required.");
            return new ResultViewModel(context.Data, context.Services, context.Services);
        }
        protected override void Bind(ResultViewModel vm, UiBindingSet bindings)
        {
            if (title != null) bindings.Add(UiControlBindings.Text(vm, nameof(vm.Title), () => vm.Title, title));
            if (description != null) bindings.Add(UiControlBindings.Text(vm, nameof(vm.Description), () => vm.Description, description));
            if (statistics != null)
            {
                var text = new StringBuilder(); foreach (var item in vm.Statistics) text.AppendLine(item.Label + "：" + item.Value);
                statistics.text = text.ToString();
            }
            if (restart != null) bindings.Add(UiControlBindings.Button(restart, vm.RestartCommand));
            if (lobby != null) bindings.Add(UiControlBindings.Button(lobby, vm.ReturnToLobbyCommand));
            if (next != null)
            {
                bindings.Add(UiControlBindings.Button(next, vm.NextCommand));
                next.gameObject.SetActive(vm.CanNext);
            }
        }
    }
}
