using System;
using System.Collections.Generic;
using NUnit.Framework;
using WFrameWork.Events;
using WFrameWork.StateMachine;

namespace GFramework.Common.Tests
{
    public sealed class StateAndEventTests
    {
        [Test] public void StateTransitionsHaveOrderedExitEnterAndUpdate()
        {
            var calls = new List<string>();
            using (var machine = new StateMachine<int, List<string>>(calls))
            {
                machine.AddState(1, new DelegateState<List<string>>(c => c.Add("enter1"), (c, _) => c.Add("tick1"), c => c.Add("exit1")));
                machine.AddState(2, new DelegateState<List<string>>(c => c.Add("enter2"), (c, _) => c.Add("tick2"), c => c.Add("exit2")));
                machine.AddTransition(1, 2, _ => true); machine.ChangeState(1); machine.Tick(0.1); machine.Stop();
                CollectionAssert.AreEqual(new[] { "enter1", "exit1", "enter2", "tick2", "exit2" }, calls);
                Assert.IsFalse(machine.HasCurrent);
            }
        }
        [Test] public void TransitionConditionsUseRegistrationOrder()
        {
            using (var machine = new StateMachine<int, object>(null))
            {
                for (int i = 0; i < 3; i++) machine.AddState(i, new DelegateState<object>());
                machine.AddTransition(0, 1, _ => true); machine.AddTransition(0, 2, _ => true);
                machine.ChangeState(0); machine.Tick(0); Assert.AreEqual(1, machine.Current);
            }
        }
        [Test] public void ReentrantChangeWaitsForEnterToReturn()
        {
            var calls = new List<int>();
            using (var machine = new StateMachine<int, object>(null))
            {
                machine.AddState(1, new DelegateState<object>(_ => { machine.ChangeState(2); calls.Add(1); }));
                machine.AddState(2, new DelegateState<object>(_ => calls.Add(2)));
                machine.ChangeState(1); CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
            }
        }
        [Test] public void DisposalDuringEnterExitsOnce()
        {
            var machine = new StateMachine<int, object>(null); int exits = 0;
            machine.AddState(1, new DelegateState<object>(_ => machine.Dispose(), exit: _ => exits++));
            machine.ChangeState(1); machine.Dispose(); Assert.AreEqual(1, exits); Assert.IsFalse(machine.HasCurrent);
        }
        [Test] public void UpdateFailureExitsAndStoresError()
        {
            using (var machine = new StateMachine<int, object>(null))
            {
                int exits = 0;
                machine.AddState(1, new DelegateState<object>(tick: (_, __) => throw new InvalidOperationException(), exit: _ => exits++));
                machine.ChangeState(1); Assert.Throws<InvalidOperationException>(() => machine.Tick(0));
                Assert.AreEqual(1, exits); Assert.IsFalse(machine.HasCurrent); Assert.IsNotNull(machine.Error);
            }
        }
        [Test] public void TransitionCyclesFailInsteadOfHanging()
        {
            using (var machine = new StateMachine<int, object>(null))
            {
                machine.AddState(1, new DelegateState<object>(_ => machine.ChangeState(2)));
                machine.AddState(2, new DelegateState<object>(_ => machine.ChangeState(1)));
                Assert.Throws<InvalidOperationException>(() => machine.ChangeState(1)); Assert.IsFalse(machine.HasCurrent);
            }
        }
        [Test] public void UnknownStatesAndLiveDefinitionChangesAreRejected()
        {
            using (var machine = new StateMachine<int, object>(null))
            {
                Assert.Throws<KeyNotFoundException>(() => machine.ChangeState(99));
                machine.AddState(1, new DelegateState<object>()); machine.ChangeState(1);
                Assert.Throws<InvalidOperationException>(() => machine.AddState(2, new DelegateState<object>()));
            }
        }
        [Test] public void EventsKeepPayloadTypesSeparate()
        {
            using (var bus = new EventBus())
            {
                int ints = 0, strings = 0;
                bus.Subscribe<int>(v => ints += v); bus.Subscribe<string>(_ => strings++);
                bus.Publish(3); Assert.AreEqual(3, ints); Assert.AreEqual(0, strings);
            }
        }
        [Test] public void UnsubscribeDuringDispatchSuppressesLaterCallback()
        {
            using (var bus = new EventBus())
            {
                int calls = 0; IDisposable second = null;
                bus.Subscribe<int>(_ => second.Dispose()); second = bus.Subscribe<int>(_ => calls++);
                bus.Publish(1); Assert.AreEqual(0, calls); second.Dispose();
            }
        }
        [Test] public void NewListenerStartsOnNextPublishAndNestedPublishIsSafe()
        {
            using (var bus = new EventBus())
            {
                var calls = new List<int>(); bool first = true;
                bus.Subscribe<int>(value =>
                {
                    calls.Add(value);
                    if (first) { first = false; bus.Subscribe<int>(v => calls.Add(v * 10)); bus.Publish(2); }
                });
                bus.Publish(1); CollectionAssert.AreEqual(new[] { 1, 2, 20 }, calls);
                calls.Clear(); bus.Publish(3); CollectionAssert.AreEqual(new[] { 3, 30 }, calls);
            }
        }
        [Test] public void DuplicateSubscriptionsHaveIndependentOwnership()
        {
            using (var bus = new EventBus())
            {
                int calls = 0; Action<int> callback = _ => calls++;
                var a = bus.Subscribe(callback); bus.Subscribe(callback); a.Dispose(); bus.Publish(1);
                Assert.AreEqual(1, calls);
            }
        }
        [Test] public void SubscriberFailureDoesNotBlockOtherSubscribers()
        {
            using (var bus = new EventBus())
            {
                int calls = 0;
                bus.Subscribe<int>(_ => throw new InvalidOperationException()); bus.Subscribe<int>(_ => calls++);
                bus.Publish(1); Assert.AreEqual(1, calls);
            }
        }
        [Test] public void DisposeDuringPublishClearsRemainingCallbacks()
        {
            var bus = new EventBus(); int calls = 0;
            bus.Subscribe<int>(_ => bus.Dispose()); var lease = bus.Subscribe<int>(_ => calls++);
            bus.Publish(1); lease.Dispose(); Assert.AreEqual(0, calls);
            Assert.Throws<ObjectDisposedException>(() => bus.Publish(2));
        }
    }
}
