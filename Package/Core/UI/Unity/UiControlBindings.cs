using System;
using System.Collections.Generic;
using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WFrameWork.UI;

namespace WFrameWork.UI.Unity
{
    /// <summary>Explicit adapters for supported uGUI/TMP controls. No property-path reflection is used.</summary>
    public static class UiControlBindings
    {
        public static IDisposable Text(INotifyPropertyChanged source, string propertyName, Func<string> read, TMP_Text target)
        { return UiBinding.OneWay(source, propertyName, read, value => target.text = value ?? string.Empty); }
        public static IDisposable Text(INotifyPropertyChanged source, string propertyName, Func<string> read, Text target)
        { return UiBinding.OneWay(source, propertyName, read, value => target.text = value ?? string.Empty); }

        public static IDisposable Button(Button target, IUiCommand command, GameObject busyIndicator = null)
        { return UiBinding.Command(command, new ButtonCommandAdapter(target, busyIndicator)); }

        public static IDisposable Slider(INotifyPropertyChanged source, string propertyName, Func<float> read,
            Action<float> write, Slider target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new SliderAdapter(target)); }

        public static IDisposable Toggle(INotifyPropertyChanged source, string propertyName, Func<bool> read,
            Action<bool> write, Toggle target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new ToggleAdapter(target)); }

        public static IDisposable Dropdown(INotifyPropertyChanged source, string propertyName, Func<int> read,
            Action<int> write, Dropdown target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new DropdownAdapter(target)); }

        public static IDisposable Dropdown(INotifyPropertyChanged source, string propertyName, Func<int> read,
            Action<int> write, TMP_Dropdown target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new TmpDropdownAdapter(target)); }

        public static IDisposable InputField(INotifyPropertyChanged source, string propertyName, Func<string> read,
            Action<string> write, InputField target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new InputFieldAdapter(target)); }

        public static IDisposable InputField(INotifyPropertyChanged source, string propertyName, Func<string> read,
            Action<string> write, TMP_InputField target)
        { return UiBinding.TwoWay(source, propertyName, read, write, new TmpInputFieldAdapter(target)); }

        public static IDisposable Visible(INotifyPropertyChanged source, string propertyName, Func<bool> read, GameObject target)
        { return UiBinding.OneWay(source, propertyName, read, value => { if (target != null) target.SetActive(value); }); }

        public static IDisposable Interactable(INotifyPropertyChanged source, string propertyName, Func<bool> read, Selectable target)
        { return UiBinding.OneWay(source, propertyName, read, value => { if (target != null) target.interactable = value; }); }

        public static IDisposable Options(UiObservableList<string> source, Dropdown target)
        {
            return UiBinding.Collection(source, () => { target.ClearOptions(); target.AddOptions(new List<string>(source)); });
        }

        public static IDisposable Options(UiObservableList<string> source, TMP_Dropdown target)
        {
            return UiBinding.Collection(source, () => { target.ClearOptions(); target.AddOptions(new List<string>(source)); });
        }

        private abstract class ValueAdapter<T> : IUiValueAdapter<T>
        {
            public abstract T Value { get; }
            public event Action<T> ValueChanged;
            protected void Raise(T value) { ValueChanged?.Invoke(value); }
            public abstract void SetValueWithoutNotify(T value);
        }

        private sealed class SliderAdapter : ValueAdapter<float>, IDisposable
        {
            private readonly Slider _target;
            public SliderAdapter(Slider target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override float Value => _target.value;
            public override void SetValueWithoutNotify(float value) => _target.SetValueWithoutNotify(value);
            private void OnChanged(float value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class ToggleAdapter : ValueAdapter<bool>, IDisposable
        {
            private readonly Toggle _target;
            public ToggleAdapter(Toggle target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override bool Value => _target.isOn;
            public override void SetValueWithoutNotify(bool value) => _target.SetIsOnWithoutNotify(value);
            private void OnChanged(bool value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class DropdownAdapter : ValueAdapter<int>, IDisposable
        {
            private readonly Dropdown _target;
            public DropdownAdapter(Dropdown target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override int Value => _target.value;
            public override void SetValueWithoutNotify(int value) => _target.SetValueWithoutNotify(value);
            private void OnChanged(int value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class TmpDropdownAdapter : ValueAdapter<int>, IDisposable
        {
            private readonly TMP_Dropdown _target;
            public TmpDropdownAdapter(TMP_Dropdown target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override int Value => _target.value;
            public override void SetValueWithoutNotify(int value) => _target.SetValueWithoutNotify(value);
            private void OnChanged(int value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class InputFieldAdapter : ValueAdapter<string>, IDisposable
        {
            private readonly InputField _target;
            public InputFieldAdapter(InputField target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override string Value => _target.text;
            public override void SetValueWithoutNotify(string value) => _target.SetTextWithoutNotify(value ?? string.Empty);
            private void OnChanged(string value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class TmpInputFieldAdapter : ValueAdapter<string>, IDisposable
        {
            private readonly TMP_InputField _target;
            public TmpInputFieldAdapter(TMP_InputField target) { _target = target ?? throw new ArgumentNullException(nameof(target)); _target.onValueChanged.AddListener(OnChanged); }
            public override string Value => _target.text;
            public override void SetValueWithoutNotify(string value) => _target.SetTextWithoutNotify(value ?? string.Empty);
            private void OnChanged(string value) { Raise(value); }
            public void Dispose() { if (_target != null) _target.onValueChanged.RemoveListener(OnChanged); }
        }

        private sealed class ButtonCommandAdapter : IUiCommandAdapter
        {
            private readonly Button _target;
            private readonly GameObject _busyIndicator;
            public ButtonCommandAdapter(Button target, GameObject busyIndicator)
            {
                _target = target ?? throw new ArgumentNullException(nameof(target)); _busyIndicator = busyIndicator;
                _target.onClick.AddListener(OnClicked);
            }
            public event Action Clicked;
            public void SetInteractable(bool interactable) { if (_target != null) _target.interactable = interactable; }
            public void SetBusy(bool busy) { if (_busyIndicator != null) _busyIndicator.SetActive(busy); }
            private void OnClicked() { Clicked?.Invoke(); }
            public void Dispose() { if (_target != null) _target.onClick.RemoveListener(OnClicked); Clicked = null; }
        }
    }
}
