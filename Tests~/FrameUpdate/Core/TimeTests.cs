using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class TimeTests
    {
        internal static void Add(List<TestCase> tests)
        {
            tests.Add(new TestCase("T01 interval delivers complete accumulated time", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p, UpdateSchedule.AtInterval(.1));
                r.Step(.04); Assert.Equal(0, p.Calls); r.Step(.06); Assert.Equal(1, p.Calls); Assert.Near(.1, p.Last.DeltaTime);
            }));
            tests.Add(new TestCase("T02 pause retains pre-pause accumulation without wall time", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p, UpdateSchedule.AtInterval(.1));
                r.Step(.04); using (r.Manager.PauseAll()) { r.Step(5); }
                r.Step(.06); Assert.Equal(1, p.Calls); Assert.Near(.1, p.Last.DeltaTime);
            }));
            tests.Add(new TestCase("T03 merged overdue interval stays on original deadline grid", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p, UpdateSchedule.AtInterval(.1));
                r.Step(.25); Assert.Equal(1, p.Calls); Assert.Near(.25, p.Last.DeltaTime);
                r.Step(.04); Assert.Equal(1, p.Calls); r.Step(.01); Assert.Equal(2, p.Calls); Assert.Near(.05, p.Last.DeltaTime);
            }));
            tests.Add(new TestCase("T04 same schedule does not reset deadline", () =>
            {
                using var r = new Rig(); var p = new Probe(); var h = r.Register(p, UpdateSchedule.AtFps(10));
                for (int i = 0; i < 100; i++) { Assert.True(r.Manager.SetSchedule(h, UpdateSchedule.AtFps(10))); r.Step(.01); }
                Assert.Equal(10, p.Calls); Assert.Near(1, p.Total);
            }));
            tests.Add(new TestCase("T05 N-step offset and zero-delta step counting", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                r.Register(a, UpdateSchedule.EveryNFrames(3)); r.Register(b, UpdateSchedule.EveryNFrames(3, 1));
                r.Step(0); r.Step(0); Assert.Equal(0, a.Calls); r.Step(0); Assert.Equal(1, a.Calls); Assert.Equal(0, b.Calls);
                r.Step(0); Assert.Equal(1, b.Calls); r.Step(0); r.Step(0); Assert.Equal(2, a.Calls);
            }));
            tests.Add(new TestCase("T06 group chain scales and ancestor pause freezes progress", () =>
            {
                using var r = new Rig(); var parent = r.Manager.CreateGroup("Parent", new UpdateGroupOptions { TimeScale = .5 });
                var child = r.Manager.CreateGroup("Child", new UpdateGroupOptions { TimeScale = 3 }, parent);
                var p = new Probe(); r.Register(p, group: child); r.Step(.2); Assert.Near(.3, p.Last.DeltaTime);
                using (r.Manager.PauseGroup(parent)) { r.Step(10); } r.Step(.1); Assert.Near(.15, p.Last.DeltaTime); Assert.Near(.45, p.Total);
            }));
            tests.Add(new TestCase("T07 zero source delta executes but zero multiplier stops", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p); r.Step(0); Assert.Equal(1, p.Calls);
                r.Manager.SetGroupTimeScale(r.Manager.DefaultGroup, 0); r.Step(1); Assert.Equal(1, p.Calls);
                r.Manager.SetGroupTimeScale(r.Manager.DefaultGroup, 1); r.Step(.01); Assert.Equal(2, p.Calls); Assert.Near(.01, p.Total);
            }));
            tests.Add(new TestCase("T08 T08a root time domains and inherited child source", () =>
            {
                using var r = new Rig(); var real = r.Manager.CreateGroup("Real", new UpdateGroupOptions { TimeSource = UpdateTimeSource.Unscaled });
                var child = r.Manager.CreateGroup("Child", default, real); var a = new Probe(); var b = new Probe();
                r.Register(a); r.Register(b, group: child); r.Step(.02, .1); Assert.Near(.02, a.Total); Assert.Near(.1, b.Total);
                Assert.Throws<ArgumentException>(() => r.Manager.CreateGroup("Conflict", new UpdateGroupOptions { TimeSource = UpdateTimeSource.Scaled }, real));
            }));
            tests.Add(new TestCase("T09 explicit delta cap reports loss and actual elapsed", () =>
            {
                using var r = new Rig(); var p = new Probe();
                r.Manager.Register(p, new FrameUpdateOptions { MaxCallbackDeltaSeconds = .1 }); r.Step(.5);
                Assert.Near(.5, p.Last.RawDeltaTime); Assert.Near(.1, p.Last.DeltaTime); Assert.Near(.4, p.Last.DiscardedDeltaTime); Assert.Near(.1, p.Last.ElapsedTime);
            }));
            tests.Add(new TestCase("T09 entry zero cap overrides manager cap", () =>
            {
                var config = FrameUpdateConfig.Default; config.MaxCallbackDeltaSeconds = .1;
                using var r = new Rig(config); var p = new Probe();
                r.Manager.Register(p, new FrameUpdateOptions { MaxCallbackDeltaSeconds = 0 }); r.Step(.5);
                Assert.Near(.5, p.Last.DeltaTime); Assert.Near(0, p.Last.DiscardedDeltaTime);
            }));
            tests.Add(new TestCase("T10 in-phase pause preserves time accumulated by later entry", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe(); PauseHandle pause = default;
                r.Register(a, priority: -1); r.Register(b);
                a.Action = _ => { pause = r.Manager.PauseAll(); a.Action = null; };
                r.Step(.03); Assert.Equal(0, b.Calls); r.Step(5); pause.Dispose(); r.Step(.02);
                Assert.Equal(1, b.Calls); Assert.Near(.05, b.Last.DeltaTime);
            }));
            tests.Add(new TestCase("T11 disabled entry retains accumulated time and N progress", () =>
            {
                using var r = new Rig(); var p = new Probe(); var h = r.Register(p, UpdateSchedule.EveryNFrames(3));
                r.Step(.04); r.Manager.SetEnabled(h, false); r.Step(5); r.Manager.SetEnabled(h, true);
                r.Step(.03); Assert.Equal(0, p.Calls); r.Step(.03); Assert.Equal(1, p.Calls); Assert.Near(.1, p.Total);
            }));
            tests.Add(new TestCase("T11 moving group retains deadline and accumulated time", () =>
            {
                using var r = new Rig(); var p = new Probe(); var h = r.Register(p, UpdateSchedule.AtInterval(.1));
                var twice = r.Manager.CreateGroup("Twice", new UpdateGroupOptions { TimeScale = 2 });
                r.Step(.04); r.Manager.SetGroup(h, twice); r.Step(.03); Assert.Equal(1, p.Calls); Assert.Near(.1, p.Total);
            }));
            tests.Add(new TestCase("T11 changed interval restarts deadline while retaining raw debt", () =>
            {
                using var r = new Rig(); var p = new Probe(); var h = r.Register(p, UpdateSchedule.AtInterval(.1));
                r.Step(.04); r.Manager.SetSchedule(h, UpdateSchedule.AtInterval(.2));
                r.Step(.19); Assert.Equal(0, p.Calls); r.Step(.01); Assert.Equal(1, p.Calls); Assert.Near(.24, p.Total);
            }));
            tests.Add(new TestCase("T11 callback-time schedule change applies after current phase", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                r.Register(a, priority: -1); var bh = r.Register(b);
                a.Action = _ => { r.Manager.SetSchedule(bh, UpdateSchedule.AtInterval(.1)); a.Action = null; };
                r.Step(.02); Assert.Equal(1, b.Calls); r.Step(.08); Assert.Equal(1, b.Calls); r.Step(.02); Assert.Equal(2, b.Calls);
            }));
            tests.Add(new TestCase("T11 reenable during phase waits for next eligibility snapshot", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                r.Register(a, priority: -1); var bh = r.Manager.Register(b, new FrameUpdateOptions { Enabled = false });
                a.Action = _ => r.Manager.SetEnabled(bh, true);
                r.Step(.02); Assert.Equal(0, b.Calls); r.Step(.02); Assert.Equal(1, b.Calls); Assert.Near(.02, b.Total);
            }));
            tests.Add(new TestCase("T12 variable-step long run has no reset drift", () =>
            {
                using var r = new Rig(); var p = new Probe(); var fps = new Probe();
                r.Register(p, UpdateSchedule.AtInterval(.1)); r.Register(fps, UpdateSchedule.AtFps(10));
                double elapsed = 0;
                for (int i = 0; i < 12000; i++) { double dt = (i % 3 == 0) ? .017 : .013; elapsed += dt; r.Step(dt); }
                int expected = (int)Math.Floor((elapsed + 1e-8) / .1);
                Assert.Equal(expected, p.Calls); Assert.Equal(expected, fps.Calls); Assert.True(elapsed - p.Total < .1 + .017);
            }));
            tests.Add(new TestCase("initial interval offset delays first callback only", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p, UpdateSchedule.AtInterval(.1, .05));
                r.Step(.1); Assert.Equal(0, p.Calls); r.Step(.05); Assert.Equal(1, p.Calls);
                r.Step(.1); Assert.Equal(2, p.Calls);
            }));
            tests.Add(new TestCase("configuration snapshot and explicit zero false fields", () =>
            {
                var config = FrameUpdateConfig.Default; config.DefaultPriority = 42; config.DefaultGroupTimeScale = 2;
                using var r = new Rig(config); config.DefaultGroupTimeScale = 9;
                var p = new Probe(); var h = r.Manager.Register(p, new FrameUpdateOptions { Enabled = false, Priority = 0 });
                r.Step(1); Assert.Equal(0, p.Calls); r.Manager.SetEnabled(h, true); r.Step(.1); Assert.Near(.2, p.Total);
            }));
            tests.Add(new TestCase("numeric parameters reject NaN infinity negative and invalid offsets", InvalidParameters));
        }

        private static void InvalidParameters()
        {
            foreach (double value in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentException>(() => UpdateSchedule.AtFps(value));
                Assert.Throws<ArgumentException>(() => UpdateSchedule.AtInterval(value));
            }
            Assert.Throws<ArgumentException>(() => UpdateSchedule.EveryNFrames(0));
            Assert.Throws<ArgumentException>(() => UpdateSchedule.EveryNFrames(3, 3));
            Assert.Throws<ArgumentException>(() => UpdateSchedule.EveryNFrames(3, -1));
            Assert.Throws<ArgumentException>(() => UpdateSchedule.AtInterval(.1, -.1));
            using var r = new Rig();
            Assert.Throws<ArgumentException>(() => r.Manager.CreateGroup("NaN", new UpdateGroupOptions { TimeScale = double.NaN }));
            Assert.Throws<ArgumentException>(() => r.Manager.Register(new Probe(), new FrameUpdateOptions { MaxCallbackDeltaSeconds = -1 }));
            Assert.Throws<ArgumentException>(() => r.Manager.Register(new Probe(), new FrameUpdateOptions { Phase = (UpdatePhase)99 }));
            Assert.Throws<ArgumentException>(() => r.Manager.Register(null, default));
            Assert.Throws<ArgumentException>(() => r.Manager.Tick(r.Driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, -1, .02)));
            var c = FrameUpdateConfig.Default; c.InitialCapacity = 0;
            Assert.Throws<ArgumentException>(() => new FrameUpdateManager(c));
        }
    }
}
