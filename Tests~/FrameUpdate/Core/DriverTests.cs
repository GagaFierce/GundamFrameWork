using System;
using System.Collections.Generic;
using System.Threading;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class DriverTests
    {
        internal static void Add(List<TestCase> tests)
        {
            tests.Add(new TestCase("D01 exclusive loop driver", () =>
            {
                using var r = new Rig();
                Assert.Throws<InvalidOperationException>(() => r.Manager.BindDriver(r.Manager.DefaultLoop, "Duplicate"));
            }));
            tests.Add(new TestCase("D02 disposed binding rejected and replacement starts at one", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p, UpdateSchedule.AtInterval(.1));
                r.Step(.04); r.Driver.Dispose();
                Assert.Throws<InvalidOperationException>(() => r.Manager.Tick(r.Driver, UpdatePhase.NormalUpdate, new FrameTimeSample(100, 1, 1)));
                using var replacement = r.Manager.BindDriver(r.Manager.DefaultLoop, "Replacement", UpdatePhaseMask.Normal);
                r.Manager.Tick(replacement, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .06, .06));
                Assert.Equal(1, p.Calls); Assert.Near(.1, p.Last.DeltaTime);
            }));
            tests.Add(new TestCase("R03 D03 independent registrations across phases and loops", () =>
            {
                using var m = new FrameUpdateManager(FrameUpdateConfig.Default);
                using var d = m.BindDriver(m.DefaultLoop, "Main");
                var loop = m.CreateLoop("Other"); using var e = m.BindDriver(loop, "Other", UpdatePhaseMask.Normal);
                var p = new Probe(); var records = new List<FrameUpdateContext>(); p.Action = context => records.Add(context);
                var normal = m.Register(p, default);
                var late = m.Register(p, new FrameUpdateOptions { Phase = UpdatePhase.LateUpdate });
                var other = m.Register(p, new FrameUpdateOptions { Loop = loop });
                Assert.False(normal.Equals(late)); Assert.False(normal.Equals(other));
                var sample = new FrameTimeSample(1, .02, .02);
                m.Tick(d, UpdatePhase.EarlyUpdate, sample); m.Tick(d, UpdatePhase.NormalUpdate, sample); m.Tick(d, UpdatePhase.LateUpdate, sample);
                m.Tick(e, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .1, .1));
                Assert.Equal(3, records.Count); Assert.Near(.02, records[0].ElapsedTime); Assert.Near(.02, records[1].ElapsedTime);
                Assert.Near(.1, records[2].ElapsedTime);
            }));
            tests.Add(new TestCase("D04 invalid driver protocol rejected without advancing", InvalidProtocol));
            tests.Add(new TestCase("D05 loop cycles may interleave", () =>
            {
                using var m = new FrameUpdateManager(FrameUpdateConfig.Default); using var a = m.BindDriver(m.DefaultLoop, "A");
                var l = m.CreateLoop("B"); using var b = m.BindDriver(l, "B", UpdatePhaseMask.Normal);
                var pa = new Probe(); var pb = new Probe(); m.Register(pa, default); m.Register(pb, new FrameUpdateOptions { Loop = l });
                var s = new FrameTimeSample(1, .03, .03);
                m.Tick(a, UpdatePhase.EarlyUpdate, s);
                m.Tick(b, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .1, .1));
                m.Tick(b, UpdatePhase.NormalUpdate, new FrameTimeSample(2, .1, .1));
                m.Tick(a, UpdatePhase.NormalUpdate, s); m.Tick(a, UpdatePhase.LateUpdate, s);
                Assert.Equal(1, pa.Calls); Assert.Equal(2, pb.Calls); Assert.Near(.03, pa.Total); Assert.Near(.2, pb.Total);
            }));
            tests.Add(new TestCase("D06 stage declaration validates binding and registration", () =>
            {
                using var r = new Rig();
                Assert.Throws<ArgumentException>(() => r.Manager.Register(new Probe(), new FrameUpdateOptions { Phase = UpdatePhase.LateUpdate }));
                var loop = r.Manager.CreateLoop("Unbound");
                r.Manager.Register(new Probe(), new FrameUpdateOptions { Loop = loop, Phase = UpdatePhase.LateUpdate });
                Assert.Throws<InvalidOperationException>(() => r.Manager.BindDriver(loop, "Insufficient", UpdatePhaseMask.Normal));
            }));
            tests.Add(new TestCase("D07 priority extremes and stable registration order across groups", () =>
            {
                using var r = new Rig(); var trace = new List<int>();
                var group = r.Manager.CreateGroup("Other", default);
                r.Register(new Probe { Action = _ => trace.Add(2) }, priority: 0);
                r.Register(new Probe { Action = _ => trace.Add(3) }, group: group, priority: 0);
                r.Register(new Probe { Action = _ => trace.Add(4) }, priority: int.MaxValue);
                r.Register(new Probe { Action = _ => trace.Add(1) }, priority: int.MinValue);
                r.Step(); Assert.Equal("1,2,3,4", string.Join(",", trace));
            }));
            tests.Add(new TestCase("D08 reentrant tick rejected and phase recovers", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                a.Action = _ => Assert.Throws<InvalidOperationException>(() => r.Manager.Tick(r.Driver, UpdatePhase.NormalUpdate, new FrameTimeSample(r.Sequence + 1, .02, .02)));
                r.Register(a); r.Register(b); r.Step(); r.Step(); Assert.Equal(2, b.Calls);
            }));
            tests.Add(new TestCase("D08 foreign thread tick rejected", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p); Exception caught = null;
                var thread = new Thread(() =>
                {
                    try { r.Manager.Tick(r.Driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .02, .02)); }
                    catch (Exception e) { caught = e; }
                });
                thread.Start(); thread.Join(); Assert.True(caught is InvalidOperationException);
                r.Step(); Assert.Equal(1, p.Calls);
            }));
            tests.Add(new TestCase("driver disposal during callback stops later callbacks", () =>
            {
                using var r = new Rig(); var a = new Probe { Action = _ => r.Driver.Dispose() }; var b = new Probe();
                r.Register(a); r.Register(b); r.Step(); Assert.Equal(1, a.Calls); Assert.Equal(0, b.Calls);
            }));
            tests.Add(new TestCase("RemoveLoop requires no driver or registrations", () =>
            {
                using var r = new Rig(); var loop = r.Manager.CreateLoop("Removable");
                var d = r.Manager.BindDriver(loop, "Driver", UpdatePhaseMask.Normal); Assert.False(r.Manager.RemoveLoop(loop));
                d.Dispose(); var h = r.Manager.Register(new Probe(), new FrameUpdateOptions { Loop = loop });
                Assert.False(r.Manager.RemoveLoop(loop)); r.Manager.Unregister(h); Assert.True(r.Manager.RemoveLoop(loop));
                Assert.Throws<ArgumentException>(() => r.Manager.Register(new Probe(), new FrameUpdateOptions { Loop = loop }));
            }));
        }

        private static void InvalidProtocol()
        {
            using var m = new FrameUpdateManager(FrameUpdateConfig.Default); using var d = m.BindDriver(m.DefaultLoop, "Full");
            var p = new Probe(); m.Register(p, default); var s = new FrameTimeSample(1, .02, .03);
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.NormalUpdate, s));
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.EarlyUpdate, new FrameTimeSample(2, .02, .03)));
            m.Tick(d, UpdatePhase.EarlyUpdate, s);
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.EarlyUpdate, s));
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.LateUpdate, s));
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.NormalUpdate, new FrameTimeSample(1, .04, .03)));
            Assert.Equal(0, p.Calls);
            m.Tick(d, UpdatePhase.NormalUpdate, s); m.Tick(d, UpdatePhase.LateUpdate, s); Assert.Equal(1, p.Calls);
            Assert.Throws<InvalidOperationException>(() => m.Tick(d, UpdatePhase.EarlyUpdate, s));
            var next = new FrameTimeSample(2, .01, .01);
            m.Tick(d, UpdatePhase.EarlyUpdate, next); m.Tick(d, UpdatePhase.NormalUpdate, next); m.Tick(d, UpdatePhase.LateUpdate, next);
            Assert.Equal(2, p.Calls);
        }
    }
}
