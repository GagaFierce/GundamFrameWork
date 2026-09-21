namespace WFrameWork.Core.FrameUpdate.Samples
{
    /// <summary>Demonstrates independent render and simulated network drivers.</summary>
    public static class ManualDriverSample
    {
        public static string Run()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            using (var frameDriver = manager.BindDriver(
                manager.DefaultLoop, "Sample manual frame driver", UpdatePhaseMask.Normal))
            {
                var networkLoop = manager.CreateLoop("Sample network loop");
                var networkGroup = manager.CreateGroup("Sample network time", new UpdateGroupOptions
                {
                    TimeSource = UpdateTimeSource.Unscaled
                });

                using (var networkDriver = manager.BindDriver(
                    networkLoop, "Sample simulated network driver", UpdatePhaseMask.Normal))
                {
                    var frameCounter = new SampleCounter();
                    var networkCounter = new SampleCounter();
                    manager.Register(frameCounter, new FrameUpdateOptions
                    {
                        Loop = manager.DefaultLoop,
                        Phase = UpdatePhase.NormalUpdate,
                        Schedule = UpdateSchedule.EveryStep()
                    });
                    manager.Register(networkCounter, new FrameUpdateOptions
                    {
                        Loop = networkLoop,
                        Phase = UpdatePhase.NormalUpdate,
                        Group = networkGroup,
                        Schedule = UpdateSchedule.EveryStep()
                    });

                    long networkSequence = 0;
                    for (long frame = 1; frame <= 60; frame++)
                    {
                        manager.RecordHostFrame(new HostFrameSample(frame, 1.0 / 60.0));
                        manager.Tick(frameDriver, UpdatePhase.NormalUpdate,
                            new FrameTimeSample(frame, 1.0 / 60.0, 1.0 / 60.0, frame));

                        // This is only a deterministic demonstration of a separate cadence.
                        // A real SDK owns its own tick; forward that tick on this manager's thread.
                        if (frame % 3 == 0)
                        {
                            networkSequence++;
                            manager.Tick(networkDriver, UpdatePhase.NormalUpdate,
                                new FrameTimeSample(networkSequence, 0.05, 0.05, frame));
                        }
                    }

                    return $"Manual drivers: simulation={frameCounter.CallCount}, " +
                           $"network={networkCounter.CallCount}, " +
                           $"simulation time={frameCounter.WorkedTime:F3}s, " +
                           $"network time={networkCounter.WorkedTime:F3}s.";
                }
            }
        }
    }
}
