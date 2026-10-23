using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using WFrameWork.Scheduling;
using WFrameWork.UI;
using WFrameWork.UI.Unity;
using Object = UnityEngine.Object;

namespace GFramework.Common.Tests
{
    public sealed class UiComponentTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private EventSystem _events;
        private GameObject Make(string name, params Type[] components)
        { var value = new GameObject(name, components); _objects.Add(value); return value; }
        private RectTransform Rect(string name, Transform parent = null)
        { var rect = (RectTransform)Make(name, typeof(RectTransform)).transform; rect.SetParent(parent, false); return rect; }
        [TearDown] public void Cleanup()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            _events = null;
        }
        private UiVirtualList List(out ScrollRect scroll, out RectTransform template)
        {
            var root = Rect("list"); root.sizeDelta = new Vector2(300, 200);
            scroll = root.gameObject.AddComponent<ScrollRect>();
            var viewport = Rect("viewport", root); viewport.sizeDelta = new Vector2(300, 200);
            var content = Rect("content", viewport);
            template = Rect("row", content);
            scroll.viewport = viewport; scroll.content = content;
            var list = root.gameObject.AddComponent<UiVirtualList>(); list.Initialize(scroll, template, 40, 0, 1);
            return list;
        }
        [Test] public void VirtualListReusesRowsAcrossTenThousandItems()
        {
            var list = List(out var scroll, out var template); int binds = 0, unbinds = 0;
            var active = new HashSet<int>();
            list.SetItems(10000, (_, index) => { binds++; Assert.IsTrue(active.Add(index)); }, (_, index) => { unbinds++; Assert.IsTrue(active.Remove(index)); });
            Assert.LessOrEqual(list.CreatedRowCount, 8); int created = list.CreatedRowCount;
            list.ScrollTo(5000); Assert.AreEqual(created, list.CreatedRowCount); Assert.IsTrue(active.Contains(5000));
            Assert.Greater(unbinds, 0); Assert.LessOrEqual(active.Count, 8);
            list.SetItems(0, (_, __) => { }); Assert.AreEqual(0, active.Count); Assert.AreEqual(binds, unbinds);
        }
        [Test] public void ScrollingOneRowRebindsOnlyNewVisibleRow()
        {
            var list = List(out var scroll, out var template); int binds = 0;
            list.SetItems(1000, (_, __) => binds++); list.ScrollTo(100); binds = 0;
            list.ScrollTo(101); Assert.AreEqual(1, binds);
        }
        [Test] public void DataShrinkClampsScrollAndEmptyListReleasesRows()
        {
            var list = List(out var scroll, out var template); var active = new HashSet<int>();
            Action<RectTransform, int> bind = (_, i) => active.Add(i);
            Action<RectTransform, int> unbind = (_, i) => active.Remove(i);
            list.SetItems(1000, bind, unbind); list.ScrollTo(999);
            list.SetItems(2, bind, unbind);
            Assert.AreEqual(0, scroll.content.anchoredPosition.y); CollectionAssert.AreEquivalent(new[] { 0, 1 }, active);
            list.SetItems(0, bind, unbind); Assert.AreEqual(0, active.Count);
        }
        [Test] public void ViewportResizeExpandsPoolAndKeepsIndicesValid()
        {
            var list = List(out var scroll, out var template); var active = new HashSet<int>();
            list.SetItems(1000, (_, i) => Assert.IsTrue(active.Add(i)), (_, i) => Assert.IsTrue(active.Remove(i)));
            list.ScrollTo(100);
            int initial = list.CreatedRowCount; scroll.viewport.sizeDelta = new Vector2(300, 600);
            scroll.onValueChanged.Invoke(Vector2.zero); // Same path as resize/scroll, without clearing bindings first.
            Assert.Greater(list.CreatedRowCount, initial); Assert.LessOrEqual(list.CreatedRowCount, 18);
        }
        [Test] public void ListRejectsConflictingLayoutAndInvalidHeight()
        {
            var list = List(out var scroll, out var template);
            var second = Make("second").AddComponent<UiVirtualList>();
            Assert.Throws<ArgumentOutOfRangeException>(() => second.Initialize(scroll, template, 0));
            scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
            Assert.Throws<InvalidOperationException>(() => second.Initialize(scroll, template, 40));
        }
        private PointerEventData Pointer(int id = -1)
        {
            if (_events == null) _events = Make("events", typeof(EventSystem)).GetComponent<EventSystem>();
            return new PointerEventData(_events) { pointerId = id, button = PointerEventData.InputButton.Left, position = new Vector2(100, 100) };
        }
        [Test] public void LongPressRepeatsAndCancelsOnExitOrDisable()
        {
            using (var scheduler = new TimerScheduler())
            {
                var press = Make("press").AddComponent<UiLongPress>(); press.Initialize(scheduler, 0.5f, 0.25f);
                int calls = 0; press.Pressed.AddListener(() => calls++); var pointer = Pointer();
                press.OnPointerDown(pointer); scheduler.Tick(0.5); Assert.AreEqual(1, calls);
                scheduler.Tick(0.25); Assert.AreEqual(2, calls);
                press.OnPointerExit(pointer); scheduler.Tick(1); Assert.AreEqual(2, calls);
                press.OnPointerDown(pointer); press.enabled = false; scheduler.Tick(1); Assert.AreEqual(2, calls);
            }
        }
        [Test] public void ShortPressAndDifferentPointerCannotFireOrReleaseHold()
        {
            using (var scheduler = new TimerScheduler())
            {
                var press = Make("press").AddComponent<UiLongPress>(); press.Initialize(scheduler);
                int calls = 0; press.Pressed.AddListener(() => calls++); var pointer = Pointer(1);
                press.OnPointerDown(pointer); scheduler.Tick(0.1); press.OnPointerUp(pointer); scheduler.Tick(1); Assert.AreEqual(0, calls);
                press.OnPointerDown(pointer); pointer.pointerId = 2; press.OnPointerUp(pointer); scheduler.Tick(0.5); Assert.AreEqual(1, calls);
            }
        }
        [Test] public void LongPressDisabledByItsOwnCallbackDoesNotStartRepeating()
        {
            using (var scheduler = new TimerScheduler())
            {
                var press = Make("press").AddComponent<UiLongPress>(); press.Initialize(scheduler, 0, 0.1f);
                int calls = 0; press.Pressed.AddListener(() => { calls++; press.enabled = false; });
                press.OnPointerDown(Pointer()); scheduler.Tick(0); scheduler.Tick(1);
                Assert.AreEqual(1, calls); Assert.AreEqual(0, scheduler.ActiveCount);
            }
        }
        [Test] public void DragPreservesPointerOffsetClampsAndRestoresRaycasts()
        {
            var parent = Rect("parent"); parent.sizeDelta = new Vector2(300, 200);
            var target = Rect("target", parent); target.sizeDelta = new Vector2(50, 50);
            target.localPosition = new Vector3(10, 0, 0);
            var group = target.gameObject.AddComponent<CanvasGroup>();
            var drag = target.gameObject.AddComponent<UiDragHandle>(); drag.Configure(target, group);
            var pointer = Pointer(); drag.OnBeginDrag(pointer); Assert.IsFalse(group.blocksRaycasts);
            pointer.position += new Vector2(20, 10); drag.OnDrag(pointer);
            Assert.AreEqual(30, target.localPosition.x, 0.01); Assert.AreEqual(10, target.localPosition.y, 0.01);
            pointer.position += new Vector2(2000, 2000); drag.OnDrag(pointer);
            Assert.LessOrEqual(target.localPosition.x, 125.01f); Assert.LessOrEqual(target.localPosition.y, 75.01f);
            drag.CancelDrag(); Assert.IsTrue(group.blocksRaycasts); Assert.IsFalse(drag.IsDragging);
        }
        [Test] public void TooltipOwnershipPreventsOldTriggerHidingNewTooltip()
        {
            var parent = Rect("overlay"); parent.sizeDelta = new Vector2(400, 300);
            var tooltip = Rect("tooltip", parent); tooltip.sizeDelta = new Vector2(100, 50);
            var label = Rect("label", tooltip).gameObject.AddComponent<TextMeshProUGUI>();
            var view = tooltip.gameObject.AddComponent<UiTooltipView>(); view.Initialize(label);
            var a = new object(); var b = new object();
            view.Show(a, "first", new Vector2(100, 100)); view.Show(b, "second", new Vector2(10000, 10000)); view.Hide(a);
            Assert.IsTrue(view.IsVisible); Assert.AreEqual("second", label.text);
            Assert.IsFalse(tooltip.GetComponent<CanvasGroup>().blocksRaycasts);
            Assert.LessOrEqual(tooltip.localPosition.x, 150.01f); Assert.LessOrEqual(tooltip.localPosition.y, 125.01f);
            view.Hide(b); Assert.IsFalse(view.IsVisible);
        }
        [UnityTest] public IEnumerator ExistingAsyncCommandCoalescesDuplicateButtonSubmissions()
        {
            var completion = new TaskCompletionSource<bool>(); int calls = 0;
            using (var command = new AsyncUiCommand(_ => { calls++; return completion.Task; }))
            {
                var button = Make("button", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
                using (UiControlBindings.Button(button, command))
                {
                    button.onClick.Invoke(); button.onClick.Invoke();
                    Assert.AreEqual(1, calls); Assert.IsFalse(button.interactable);
                    var first = command.ExecuteAsync(); var second = command.ExecuteAsync(); Assert.AreSame(first, second);
                    completion.SetResult(true);
                    for (int i = 0; i < 300 && !first.IsCompleted; i++) yield return null;
                    Assert.IsTrue(first.IsCompleted); Assert.IsNull(command.Error); Assert.IsTrue(button.interactable);
                }
            }
        }
        [UnityTest] public IEnumerator ComponentDisableAndDestroyReleaseBindingsAndPointerState()
        {
            yield return new EnterPlayMode();
            using (var scheduler = new TimerScheduler())
            {
                var list = List(out var scroll, out var template); var active = new HashSet<int>();
                list.SetItems(1000, (_, index) => active.Add(index), (_, index) => active.Remove(index));
                Assert.Greater(active.Count, 0);
                list.enabled = false; Assert.AreEqual(0, active.Count);
                list.enabled = true; Assert.Greater(active.Count, 0);
                var press = Make("press").AddComponent<UiLongPress>(); press.Initialize(scheduler, 1);
                press.OnPointerDown(Pointer()); Assert.AreEqual(1, scheduler.ActiveCount);
                press.enabled = false; Assert.AreEqual(0, scheduler.ActiveCount);
                var target = Rect("drag", scroll.viewport); target.sizeDelta = new Vector2(50, 50);
                var group = target.gameObject.AddComponent<CanvasGroup>();
                var drag = target.gameObject.AddComponent<UiDragHandle>(); drag.Configure(target, group);
                drag.OnBeginDrag(Pointer()); Assert.IsFalse(group.blocksRaycasts);
                drag.enabled = false; Assert.IsTrue(group.blocksRaycasts); Assert.IsFalse(drag.IsDragging);
                Object.Destroy(list); yield return null;
                Assert.AreEqual(0, active.Count);
                yield return null; // Deferred row destruction must leave only the original template.
                Assert.AreEqual(1, scroll.content.childCount);
            }
            yield return new ExitPlayMode();
        }
    }
}
