namespace WFrameWork.Core.FrameUpdate.Samples
{
    /// <summary>Independent pause owners do not cancel each other's pause requests.</summary>
    public static class GroupPauseSample
    {
        public static string Run()
        {
            using (var manager = new FrameUpdateManager(FrameUpdateConfig.Default))
            using (var driver = manager.BindDriver(
                manager.DefaultLoop, "Sample pause driver", UpdatePhaseMask.Normal))
            {
                var scope = manager.CreateScope("Sample lifetime");
                var group = manager.CreateGroup("Sample pausable work", UpdateGroupOptions.Default);
                var counter = new SampleCounter();
                var handle = manager.Register(counter, new FrameUpdateOptions
                {
                    Group = group,
                    Scope = scope,
                    Schedule = UpdateSchedule.EveryStep()
                });

                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(1, 0.1, 0.1));
                var firstPause = manager.PauseGroup(group, scope);
                var secondPause = manager.PauseGroup(group, scope);
                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(2, 5, 5));

                firstPause.Dispose();
                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(3, 5, 5));
                int callsWhileAnotherOwnerPaused = counter.CallCount;

                secondPause.Dispose();
                manager.Tick(driver, UpdatePhase.NormalUpdate, new FrameTimeSample(4, 0.1, 0.1));

                // Scope release also revokes any pause requests still owned by this scope.
                int released = manager.ReleaseScope(scope);
                bool oldHandleStillRegistered = manager.TryGetRegistration(handle, out _);
                return $"Group pause: calls before final resume={callsWhileAnotherOwnerPaused}, " +
                       $"total calls={counter.CallCount}, worked time={counter.WorkedTime:F3}s, " +
                       $"released={released}, old handle valid={oldHandleStillRegistered}.";
            }
        }
    }
}
