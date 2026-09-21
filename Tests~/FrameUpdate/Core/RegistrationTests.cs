using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class RegistrationTests
    {
        internal static void Add(List<TestCase> tests)
        {
            tests.Add(new TestCase("R01 duplicate registration is idempotent", () =>
            {
                using var r = new Rig();
                var p = new Probe();
                Assert.Equal(r.Register(p), r.Register(p));
                r.Step(); Assert.Equal(1, p.Calls);
            }));
            tests.Add(new TestCase("R02 conflicting config or lifetime preserves original", () =>
            {
                using var r = new Rig();
                var p = new Probe(); var h = r.Register(p);
                Assert.Throws<InvalidOperationException>(() => r.Register(p, UpdateSchedule.AtFps(5)));
                Assert.Throws<InvalidOperationException>(() => r.Manager.Register(p, default, new Lifetime()));
                var g = r.Manager.CreateGroup("Other", default);
                Assert.Throws<InvalidOperationException>(() => r.Register(p, group: g));
                Assert.True(r.Manager.TryGetRegistration(h, out _));
                r.Step(); Assert.Equal(1, p.Calls);
            }));
            tests.Add(new TestCase("R04 batch error is atomic including output", BatchAtomic));
            tests.Add(new TestCase("R04 batch duplicates resolve same handle", BatchDuplicates));
            tests.Add(new TestCase("R05 immediate removal suppresses later callback", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                r.Register(a, priority: -1); var bh = r.Register(b);
                a.Action = _ => r.Manager.Unregister(bh);
                r.Step(); Assert.Equal(1, a.Calls); Assert.Equal(0, b.Calls);
            }));
            tests.Add(new TestCase("R06 registration in callback waits until next phase opportunity", () =>
            {
                using var r = new Rig(); var a = new Probe(); var c = new Probe();
                r.Register(a); a.Action = _ => r.Register(c);
                r.Step(); Assert.Equal(0, c.Calls);
                r.Step(); Assert.Equal(1, c.Calls); Assert.Near(.02, c.Last.DeltaTime);
            }));
            tests.Add(new TestCase("R07 recycled old handle cannot unregister new registration", () =>
            {
                using var r = new Rig(); var p = new Probe(); var old = r.Register(p);
                Assert.True(r.Manager.Unregister(old)); var next = r.Register(p);
                Assert.False(old.Equals(next)); Assert.False(r.Manager.Unregister(old));
                r.Step(); Assert.Equal(1, p.Calls);
            }));
            tests.Add(new TestCase("R07 unregister and reregister during callback", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe();
                var bh = r.Register(b); var old = bh;
                r.Register(a, priority: -1);
                a.Action = _ => { r.Manager.Unregister(bh); bh = r.Register(b); a.Action = null; };
                r.Step(); Assert.Equal(0, b.Calls); Assert.False(r.Manager.Unregister(old));
                r.Step(); Assert.Equal(1, b.Calls);
            }));
            tests.Add(new TestCase("R08 identity ignores overridden Equals", () =>
            {
                using var r = new Rig(); var a = new EqualProbe(); var b = new EqualProbe();
                Assert.False(r.Register(a).Equals(r.Register(b)));
                r.Step(); Assert.Equal(1, a.Calls); Assert.Equal(1, b.Calls);
            }));
            tests.Add(new TestCase("R09 boxed value target is rejected", () =>
            {
                using var r = new Rig(); Assert.Throws<ArgumentException>(() => r.Manager.Register(new ValueProbe(), default));
            }));
            tests.Add(new TestCase("R09 invalid lifetime removed before frequency due", () =>
            {
                using var r = new Rig(); var p = new Probe(); var life = new Lifetime();
                var h = r.Manager.Register(p, new FrameUpdateOptions { Schedule = UpdateSchedule.AtInterval(100) }, life);
                life.Alive = false; r.Step();
                Assert.False(r.Manager.TryGetRegistration(h, out _)); Assert.Equal(0, p.Calls);
            }));
            tests.Add(new TestCase("R09 throwing lifetime reports and disables", () =>
            {
                using var r = new Rig(); var p = new Probe();
                r.Manager.Register(p, default, new Lifetime { Throw = true });
                r.Step(); Assert.Equal(0, p.Calls);
                var errors = new List<FrameUpdateError>(); r.Manager.CopyErrors(errors); Assert.Equal(1, errors.Count);
                r.Step(); r.Manager.CopyErrors(errors); Assert.Equal(1, errors.Count);
            }));
            tests.Add(new TestCase("L01 recursive scope release unregisters and releases owned pauses", () =>
            {
                using var r = new Rig(); var parent = r.Manager.CreateScope("Parent");
                var child = r.Manager.CreateScope("Child", parent); var a = new Probe(); var b = new Probe(); var survivor = new Probe();
                r.Register(a, scope: parent); r.Register(b, scope: child); r.Register(survivor);
                var pause = r.Manager.PauseAll(child);
                r.Step(); Assert.Equal(0, survivor.Calls);
                Assert.Equal(2, r.Manager.ReleaseScope(parent));
                r.Step(); Assert.Equal(1, survivor.Calls); Assert.Equal(0, a.Calls); Assert.Equal(0, b.Calls);
                Assert.Equal(0, r.Manager.ReleaseScope(child)); pause.Dispose();
            }));
            tests.Add(new TestCase("L02 scope release cancels queued registration", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe(); var scope = r.Manager.CreateScope("Scope");
                r.Register(a); UpdateHandle pending = default;
                a.Action = _ => { pending = r.Register(b, scope: scope); Assert.Equal(1, r.Manager.ReleaseScope(scope)); a.Action = null; };
                r.Step(); r.Step(); Assert.Equal(0, b.Calls); Assert.False(r.Manager.TryGetRegistration(pending, out _));
            }));
            tests.Add(new TestCase("L03 independent pause handles and copied handles", () =>
            {
                using var r = new Rig(); var p = new Probe(); r.Register(p);
                var one = r.Manager.PauseGroup(r.Manager.DefaultGroup); var copy = one;
                var two = r.Manager.PauseGroup(r.Manager.DefaultGroup);
                one.Dispose(); copy.Dispose(); r.Step(); Assert.Equal(0, p.Calls);
                two.Dispose(); r.Step(); Assert.Equal(1, p.Calls); Assert.Near(.02, p.Last.DeltaTime);
            }));
            tests.Add(new TestCase("L04 new registration inherits existing pause", () =>
            {
                using var r = new Rig(); using var pause = r.Manager.PauseAll(); var p = new Probe();
                r.Register(p); r.Step(); Assert.Equal(0, p.Calls);
            }));
            tests.Add(new TestCase("L05 disposal from callback stops remaining entries", () =>
            {
                using var r = new Rig(); var a = new Probe(); var b = new Probe(); var ah = r.Register(a, priority: -1); r.Register(b);
                a.Action = _ => r.Manager.Dispose(); r.Step();
                Assert.Equal(1, a.Calls); Assert.Equal(0, b.Calls); Assert.False(r.Manager.Unregister(ah));
                Assert.Throws<ObjectDisposedException>(() => r.Manager.GetStats());
            }));
            tests.Add(new TestCase("cross-manager handles never control local registrations", () =>
            {
                using var a = new Rig(); using var b = new Rig(); var p = new Probe(); var ah = a.Register(p);
                var bh = b.Register(new Probe()); Assert.False(b.Manager.Unregister(ah));
                Assert.False(a.Manager.SetGroup(ah, b.Manager.DefaultGroup));
                Assert.False(a.Manager.SetScope(ah, b.Manager.DefaultScope));
                Assert.False(a.Manager.Unregister(bh)); a.Step(); Assert.Equal(1, p.Calls);
            }));
            tests.Add(new TestCase("default ownership cannot be removed", () =>
            {
                using var r = new Rig(); Assert.False(r.Manager.RemoveGroup(r.Manager.DefaultGroup));
                Assert.Equal(0, r.Manager.ReleaseScope(r.Manager.DefaultScope));
                Assert.False(r.Manager.RemoveLoop(r.Manager.DefaultLoop));
            }));
            tests.Add(new TestCase("remove group respects child entry and pause ownership", () =>
            {
                using var r = new Rig(); var g = r.Manager.CreateGroup("Parent", default); var c = r.Manager.CreateGroup("Child", default, g);
                Assert.False(r.Manager.RemoveGroup(g)); Assert.True(r.Manager.RemoveGroup(c));
                var p = r.Manager.PauseGroup(g); Assert.False(r.Manager.RemoveGroup(g)); p.Dispose();
                var h = r.Register(new Probe(), group: g); Assert.False(r.Manager.RemoveGroup(g));
                r.Manager.Unregister(h); Assert.True(r.Manager.RemoveGroup(g));
                Assert.False(r.Manager.RemoveGroup(g));
            }));
        }

        private static void BatchAtomic()
        {
            using var r = new Rig(); var p = new Probe(); var existing = r.Register(new Probe());
            var output = new List<UpdateHandle> { existing };
            var invalid = new[] { new FrameUpdateRequest(p, default), new FrameUpdateRequest(null, default) };
            Assert.Throws<ArgumentException>(() => r.Manager.RegisterBatch(invalid, output));
            Assert.Equal(1, output.Count); Assert.Equal(existing, output[0]); r.Step(); Assert.Equal(0, p.Calls);
            var conflict = new[] { new FrameUpdateRequest(p, default), new FrameUpdateRequest(p, new FrameUpdateOptions { Priority = 12 }) };
            Assert.Throws<InvalidOperationException>(() => r.Manager.RegisterBatch(conflict, output));
            Assert.Equal(1, output.Count); r.Step(); Assert.Equal(0, p.Calls);
        }

        private static void BatchDuplicates()
        {
            using var r = new Rig(); var p = new Probe(); var output = new List<UpdateHandle>();
            var requests = new[] { new FrameUpdateRequest(p, default), new FrameUpdateRequest(p, default) };
            r.Manager.RegisterBatch(requests, output);
            Assert.Equal(2, output.Count); Assert.Equal(output[0], output[1]); r.Step(); Assert.Equal(1, p.Calls);
        }
    }
}
