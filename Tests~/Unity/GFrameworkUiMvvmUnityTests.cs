using System;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WFrameWork.UI;
using WFrameWork.UI.Unity;

namespace WFrameWork.Tests.Unity
{
    public sealed class GFrameworkUiMvvmUnityTests
    {
        [Test]
        public void SliderAndToggleBindingsSynchronizeWithoutDuplicateEvents()
        {
            var root = new GameObject("UI-MVVM-Test");
            var slider = root.AddComponent<Slider>(); var toggle = root.AddComponent<Toggle>(); var model = new ProbeViewModel();
            using (UiControlBindings.Slider(model, nameof(ProbeViewModel.Volume), () => model.Volume, value => model.Volume = value, slider))
            using (UiControlBindings.Toggle(model, nameof(ProbeViewModel.Muted), () => model.Muted, value => model.Muted = value, toggle))
            {
                model.Volume = .75f; model.Muted = true; Assert.That(slider.value, Is.EqualTo(.75f).Within(.001f)); Assert.That(toggle.isOn, Is.True);
                slider.value = .25f; toggle.isOn = false; Assert.That(model.Volume, Is.EqualTo(.25f).Within(.001f)); Assert.That(model.Muted, Is.False);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void ButtonBindingRemovesListenerWhenDisposed()
        {
            var root = new GameObject("UI-MVVM-Button-Test"); var button = root.AddComponent<Button>(); int calls = 0;
            var command = new UiCommand(() => calls++); IDisposable binding = UiControlBindings.Button(button, command);
            button.onClick.Invoke(); Assert.That(calls, Is.EqualTo(1)); binding.Dispose(); button.onClick.Invoke(); Assert.That(calls, Is.EqualTo(1));
            command.Dispose(); UnityEngine.Object.DestroyImmediate(root);
        }

        [Test]
        public void TmpTextBindingSynchronizesAndDisposes()
        {
            var root = new GameObject("UI-MVVM-TMP-Test"); var text = root.AddComponent<TextMeshProUGUI>(); var model = new ProbeViewModel();
            using (UiControlBindings.Text(model, nameof(ProbeViewModel.Text), () => model.Text, value => text.text = value, text))
            { model.Text = "中文绑定"; Assert.That(text.text, Is.EqualTo("中文绑定")); }
            model.Text = "late"; Assert.That(text.text, Is.EqualTo("中文绑定")); UnityEngine.Object.DestroyImmediate(root);
        }

        private sealed class ProbeViewModel : ViewModelBase
        {
            private float _volume; private bool _muted; private string _text = string.Empty;
            public float Volume { get => _volume; set => SetProperty(ref _volume, value); }
            public bool Muted { get => _muted; set => SetProperty(ref _muted, value); }
            public string Text { get => _text; set => SetProperty(ref _text, value); }
        }
    }
}
