using System;
using System.Collections.Generic;
using NUnit.Framework;
using WFrameWork.Application;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Diagnostics;
using WFrameWork.Scheduling;

namespace GFramework.Common.Tests
{
    public sealed class SchedulingTests
    {
        [Test] public void DelayCanPauseResumeAndCompleteOnce()
        {
            using (var scheduler = new TimerScheduler())
            {
                int calls = 0; var timer = scheduler.Delay(1, () => calls++);
                scheduler.Tick(0.25); timer.Pause(); scheduler.Tick(10);
                Assert.AreEqual(0, calls); timer.Resume(); scheduler.Tick(0.75); scheduler.Tick(10);
                Assert.AreEqual(1, calls); Assert.AreEqual(ScheduledStatus.Completed, timer.Status);
                Assert.AreEqual(0, scheduler.ActiveCount);
            }
        }
        [Test] public void RepeatSkipsMissedIntervalsAndHonorsCount()
        {
            using (var scheduler = new TimerScheduler())
            {
                int calls = 0; var timer = scheduler.Repeat(1, () => calls++, 2);
                scheduler.Tick(5.5); Assert.AreEqual(1, calls);
                Assert.AreEqual(0.5, timer.RemainingSeconds, 0.00001);
                scheduler.Tick(0.5); scheduler.Tick(20);
                Assert.AreEqual(2, calls); Assert.IsTrue(timer.IsDone);
            }
        }
        [Test] public void CallbackCanCancelAnotherAndScheduleForNextTick()
        {
            using (var scheduler = new TimerScheduler())
            {
                var calls = new List<int>(); TimerHandle second = null;
                scheduler.Delay(0, () => { calls.Add(1); second.Cancel(); scheduler.Delay(0, () => calls.Add(3)); });
                second = scheduler.Delay(0, () => calls.Add(2));
                scheduler.Tick(0); CollectionAssert.AreEqual(new[] { 1 }, calls);
                scheduler.Tick(0); CollectionAssert.AreEqual(new[] { 1, 3 }, calls);
            }
        }
        [Test] public void CancelAllInsideCallbackDoesNotCorruptIteration()
        {
            using (var scheduler = new TimerScheduler())
            {
                int calls = 0;
                scheduler.Delay(0, () => { scheduler.CancelAll(); scheduler.Delay(0, () => calls++); });
                scheduler.Delay(0, () => calls += 100);
                scheduler.Tick(0); Assert.AreEqual(0, calls);
                scheduler.Tick(0); Assert.AreEqual(1, calls);
            }
        }
        [Test] public void DisposalInsideCallbackCancelsRemainingWork()
        {
            var scheduler = new TimerScheduler(); int calls = 0;
            scheduler.Delay(0, scheduler.Dispose); var pending = scheduler.Delay(0, () => calls++);
            scheduler.Tick(0); Assert.AreEqual(0, calls); Assert.AreEqual(ScheduledStatus.Cancelled, pending.Status);
            Assert.Throws<ObjectDisposedException>(() => scheduler.Delay(0, () => { }));
        }
        [Test] public void CallbackFailureDoesNotStopOtherTimers()
        {
            var sink = new CollectingDiagnosticSink();
            using (var scheduler = new TimerScheduler(sink))
            {
                int calls = 0; var bad = scheduler.Delay(0, () => throw new InvalidOperationException("expected"));
                scheduler.Delay(0, () => calls++); scheduler.Tick(0);
                Assert.AreEqual(ScheduledStatus.Faulted, bad.Status); Assert.IsNotNull(bad.Error); Assert.AreEqual(1, calls);
            }
        }
        [Test] public void InvalidDurationsAndRepeatCountsAreRejected()
        {
            using (var scheduler = new TimerScheduler())
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Delay(double.NaN, () => { }));
                Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Repeat(0, () => { }));
                Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Repeat(1, () => { }, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Tick(-1));
            }
        }
        private sealed class Lifetime : IUpdateLifetime { public bool IsAlive { get; set; } = true; }
        [Test] public void DeadOwnerCancelsEvenPausedTimer()
        {
            using (var scheduler = new TimerScheduler())
            {
                var owner = new Lifetime(); var timer = scheduler.Delay(0, () => Assert.Fail(), owner);
                timer.Pause(); owner.IsAlive = false; scheduler.Tick(0);
                Assert.AreEqual(ScheduledStatus.Cancelled, timer.Status);
            }
        }
        [Test] public void SequenceCarriesExcessTimeAcrossWaitsAndSnapshotsDefinition()
        {
            using (var scheduler = new TimerScheduler())
            {
                var calls = new List<int>();
                var definition = new TaskSequence().Wait(0.5).Do(() => calls.Add(1)).Wait(0.25).Do(() => calls.Add(2));
                var sequence = scheduler.Start(definition); definition.Do(() => calls.Add(3));
                scheduler.Tick(1); CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
                Assert.AreEqual(ScheduledStatus.Completed, sequence.Status);
            }
        }
        [Test] public void SequenceWaitUntilDoesNotBackdateNextWait()
        {
            using (var scheduler = new TimerScheduler())
            {
                bool ready = false; int calls = 0;
                scheduler.Start(new TaskSequence().WaitUntil(() => ready).Wait(1).Do(() => calls++));
                scheduler.Tick(100); ready = true; scheduler.Tick(100); Assert.AreEqual(0, calls);
                scheduler.Tick(1); Assert.AreEqual(1, calls);
            }
        }
        [Test] public void SequenceCanCancelItselfAndInstantWorkIsBounded()
        {
            using (var scheduler = new TimerScheduler())
            {
                SequenceHandle sequence = null;
                sequence = scheduler.Start(new TaskSequence().Do(() => sequence.Cancel()).Do(() => Assert.Fail()));
                scheduler.Tick(0); Assert.AreEqual(ScheduledStatus.Cancelled, sequence.Status);
                int calls = 0; var definition = new TaskSequence();
                for (int i = 0; i < 130; i++) definition.Do(() => calls++);
                var bounded = scheduler.Start(definition);
                scheduler.Tick(0); Assert.AreEqual(64, calls);
                scheduler.Tick(0); scheduler.Tick(0); Assert.AreEqual(130, calls); Assert.IsTrue(bounded.IsDone);
            }
        }
        [Test] public void RuntimeScopeOwnsTimerAndEventLeases()
        {
            using (var scheduler = new TimerScheduler())
            using (var bus = new WFrameWork.Events.EventBus())
            {
                var scope = new RuntimeScope("test"); int calls = 0;
                var timer = scheduler.Delay(0, () => calls++);
                scope.Own(timer); scope.Own(bus.Subscribe<int>(_ => calls++));
                scope.CloseAsync().GetAwaiter().GetResult();
                scheduler.Tick(0); bus.Publish(1);
                Assert.AreEqual(0, calls); Assert.AreEqual(ScheduledStatus.Cancelled, timer.Status);
            }
        }
        [Test] public void FrameGroupProvidesUnscaledTimeAndPause()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            using (var scheduler = new TimerScheduler())
            using (var driver = manager.BindDriver(manager.DefaultLoop, "test", UpdatePhaseMask.Normal))
            {
                var group = manager.CreateGroup("UI", new UpdateGroupOptions { TimeSource = UpdateTimeSource.Unscaled });
                manager.Register(scheduler, new FrameUpdateOptions { Group = group }, scheduler);
                int calls = 0; scheduler.Delay(0.2, () => calls++);
                using (manager.PauseGroup(group)) manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, 0, 0.1));
                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(2, 0, 0.1));
                Assert.AreEqual(0, calls);
                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(3, 0, 0.1));
                Assert.AreEqual(1, calls);
            }
        }
    }
}
