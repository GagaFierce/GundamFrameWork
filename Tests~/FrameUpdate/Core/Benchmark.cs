using System;
using System.Diagnostics;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal static class Benchmark
    {
        internal static int Run(string[] args)
        {
            int count = 1000;
            if (args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed > 0) count = parsed;
            using var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
            using var driver = manager.BindDriver(manager.DefaultLoop, "Benchmark", UpdatePhaseMask.Normal);
            for (int i = 0; i < count; i++) manager.Register(new EmptyUpdate(), default(FrameUpdateOptions));
            for (int i = 0; i < 100; i++) manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(i + 1, .016, .016));
            var timer = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(i + 101, .016, .016));
            timer.Stop();
            Console.WriteLine($"BENCH entries={count} ticks=1000 elapsedMs={timer.Elapsed.TotalMilliseconds:F3}");
            return 0;
        }

        private sealed class EmptyUpdate : IFrameUpdate
        {
            public void OnFrameUpdate(in FrameUpdateContext context) { }
        }
    }
}
