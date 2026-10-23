using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.Scheduling
{
    /// <summary>Reusable definition; Start takes a snapshot. Actions run synchronously on the scheduler thread.</summary>
    public sealed class TaskSequence
    {
        internal readonly struct Step
        {
            internal readonly double Delay;
            internal readonly Action Action;
            internal readonly Func<bool> Condition;
            internal Step(double delay, Action action, Func<bool> condition) { Delay = delay; Action = action; Condition = condition; }
        }
        private readonly List<Step> _steps = new List<Step>();
        public TaskSequence Wait(double seconds)
        { TimerScheduler.ValidateSeconds(seconds, nameof(seconds)); _steps.Add(new Step(seconds, null, null)); return this; }
        public TaskSequence Do(Action action)
        { _steps.Add(new Step(0, action ?? throw new ArgumentNullException(nameof(action)), null)); return this; }
        public TaskSequence WaitUntil(Func<bool> condition)
        { _steps.Add(new Step(0, null, condition ?? throw new ArgumentNullException(nameof(condition)))); return this; }
        internal Step[] Snapshot() { return _steps.ToArray(); }
    }

    public sealed class SequenceHandle : ScheduledHandle
    {
        private TaskSequence.Step[] _steps;
        private int _index;
        private double _elapsed;
        internal SequenceHandle(TaskSequence.Step[] steps, IUpdateLifetime owner) : base(owner) { _steps = steps; }
        public int CompletedStepCount => _index;
        protected override void Tick(double delta)
        {
            // Bound instant work so a large sequence cannot monopolize a frame.
            for (int work = 0; work < 64 && !IsDone && !IsPaused; work++)
            {
                if (_index == _steps.Length) { Complete(); return; }
                var step = _steps[_index];
                if (step.Condition != null)
                {
                    bool ready = step.Condition();
                    if (IsDone || IsPaused || !ready) return;
                    // The condition became true sometime in this frame; don't backdate subsequent waits.
                    delta = 0;
                }
                else if (step.Action != null)
                {
                    _index++;
                    step.Action();
                    continue;
                }
                else
                {
                    double needed = step.Delay - _elapsed;
                    if (delta < needed) { _elapsed += delta; return; }
                    delta -= needed;
                    _elapsed = 0;
                }
                _index++;
            }
            if (!IsDone && _index == _steps.Length) Complete();
        }
        protected override void Release() { _steps = null; }
    }
}
