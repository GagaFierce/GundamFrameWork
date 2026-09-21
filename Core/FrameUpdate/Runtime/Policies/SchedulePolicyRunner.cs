using System;

namespace WFrameWork.Core.FrameUpdate
{
    /// <summary>
    /// Optional adapter for policies that periodically choose a target's schedule.
    /// The policy itself owns the source of truth for distance, visibility or load;
    /// the core only applies the returned schedule.
    /// </summary>
    public sealed class SchedulePolicyRunner : IFrameUpdate, IDisposable
    {
        private readonly FrameUpdateManager _manager;
        private readonly UpdateHandle _target;
        private readonly IUpdateSchedulePolicy _policy;
        private readonly UpdateHandle _runner;
        private bool _disposed;

        public UpdateHandle TargetHandle { get { return _target; } }
        public UpdateHandle RunnerHandle { get { return _runner; } }

        public SchedulePolicyRunner(FrameUpdateManager manager, UpdateHandle target,
            IUpdateSchedulePolicy policy, in FrameUpdateOptions runnerOptions)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (!target.IsValid) throw new ArgumentException("The target handle must be active.", nameof(target));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _target = target;
            _runner = manager.Register(this, runnerOptions);
        }

        public void OnFrameUpdate(in FrameUpdateContext context)
        {
            if (_disposed || !_target.IsValid) return;
            if (!_manager.TryGetRegistration(_target, out var info)) return;
            var stats = new UpdateEntryStats(_target, info.Loop, info.Phase, info.Group,
                info.Priority, info.Enabled, info.ExecutionCount, info.DeferredCount,
                info.FaultedCount, 0, 0, info.ElapsedTime, info.AccumulatedTime);
            var schedule = _policy.Evaluate(new UpdatePolicyContext(_target,
                info.Schedule, info.Priority, stats));
            _manager.SetSchedule(_target, schedule);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manager.Unregister(_runner);
        }
    }
}
