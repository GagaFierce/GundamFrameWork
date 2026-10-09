using TMPro;
using UnityEngine;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Samples.Combined
{
    public sealed class SampleToastPanel : UiPanelViewBehaviour<ToastViewModel>
    {
        [SerializeField] private TMP_Text message;
        private CanvasGroup _canvasGroup;
        public override bool RequiresContinuousUpdate => ViewModel?.Current != null;
        protected override ToastViewModel CreateViewModel(object argument) => argument as ToastViewModel ?? new ToastViewModel();
        protected override void Bind(ToastViewModel vm, UiBindingSet bindings)
        {
            if (message != null)
            {
                bindings.Add(UiControlBindings.Text(vm, nameof(vm.Current), () => vm.Current?.Message ?? string.Empty, message));
                bindings.Add(UiControlBindings.Visible(vm, nameof(vm.Current), () => vm.Current != null, message.gameObject));
            }
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup != null)
            {
                _canvasGroup.blocksRaycasts = false;
                bindings.Add(UiBinding.OneWay(vm, nameof(vm.Current), () => vm.Current != null,
                    visible => _canvasGroup.alpha = visible ? 1 : 0));
            }
        }
        public override void OnPanelUpdate(in UiPanelUpdateContext context) => ViewModel?.Tick((float)context.UnscaledDeltaTime);
        public override void OnPanelShown() { }
    }
}
