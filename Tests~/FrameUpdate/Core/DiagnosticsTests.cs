using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class DiagnosticsTests
    {
        internal static void Add(List<TestCase> tests)
        {
            tests.Add(new TestCase("O01 errors are isolated and reported", () =>
            {
                using var r = new Rig();
                var failing = new Probe { Action = _ => throw new InvalidOperationException("failure") };
                var healthy = new Probe();
                r.Register(failing, priority: -1);
                r.Register(healthy);
                r.Step();
                r.Step();
                Assert.Equal(1, failing.Calls);
                Assert.Equal(2, healthy.Calls);
                var errors = new List<FrameUpdateError>();
                r.Manager.CopyErrors(errors);
                Assert.Equal(1, errors.Count);
                Assert.True(errors[0].Exception is InvalidOperationException);
            }));
            tests.Add(new TestCase("O03 host FPS is not multiplied by callback phases", () =>
            {
                using var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
                using var driver = manager.BindDriver(manager.DefaultLoop, "All phases");
                var probe = new Probe();
                manager.Register(probe, new FrameUpdateOptions { Phase = UpdatePhase.EarlyUpdate });
                manager.Register(new Probe(), new FrameUpdateOptions { Phase = UpdatePhase.LateUpdate });
                manager.RecordHostFrame(new HostFrameSample(1, .1));
                var sample = new FrameTimeSample(1, .1, .1, 1);
                manager.Tick(driver, UpdatePhase.EarlyUpdate, sample);
                manager.Tick(driver, UpdatePhase.NormalUpdate, sample);
                manager.Tick(driver, UpdatePhase.LateUpdate, sample);
                manager.RecordHostFrame(new HostFrameSample(2, .1));
                var stats = manager.GetStats();
                Assert.Near(10, stats.HostFps);
                Assert.Equal(1, stats.TotalTicks);
                Assert.Equal(1, probe.Calls);
            }));
            tests.Add(new TestCase("O05 error buffer is bounded", () =>
            {
                var config = FrameUpdateConfig.Default;
                config.ErrorBufferCapacity = 2;
                using var r = new Rig(config);
                var a = new Probe { Action = _ => throw new Exception("a") };
                var b = new Probe { Action = _ => throw new Exception("b") };
                var c = new Probe { Action = _ => throw new Exception("c") };
                r.Register(a); r.Register(b); r.Register(c);
                r.Step();
                var errors = new List<FrameUpdateError>();
                r.Manager.CopyErrors(errors);
                Assert.Equal(2, errors.Count);
                Assert.Equal("b", errors[0].Exception.Message);
                Assert.Equal("c", errors[1].Exception.Message);
            }));
            tests.Add(new TestCase("O06 query buffers are reused", () =>
            {
                using var r = new Rig();
                r.Register(new Probe());
                var entries = new List<UpdateEntryStats> { new UpdateEntryStats() };
                r.Manager.CopyEntryStats(entries);
                Assert.Equal(1, entries.Count);
                var loops = new List<UpdateLoopStats> { new UpdateLoopStats() };
                r.Manager.CopyLoopStats(loops);
                Assert.Equal(1, loops.Count);
            }));
        }
    }
}
