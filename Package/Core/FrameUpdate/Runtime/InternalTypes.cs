using System;
using System.Collections.Generic;

namespace WFrameWork.Core.FrameUpdate
{
    internal sealed class LoopState
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly string Name;
        internal readonly int Id;
        internal bool IsActive = true;
        internal bool IsDefault;
        internal bool IsTicking;
        internal DriverState Driver;
        internal readonly List<Entry>[] Entries = { new List<Entry>(), new List<Entry>(), new List<Entry>() };
        internal readonly bool[] Dirty = { true, true, true };
        internal readonly double[] BudgetMilliseconds = new double[3];
        internal readonly bool[] BudgetOverrides = new bool[3];
        internal long CompletedTicks;
        internal long ExecutedCount;
        internal long DeferredCount;
        internal long FaultedCount;
        internal double TotalCpuMilliseconds;
        internal long LastCompletedTimestamp;
        internal double LastTickHz;

        internal LoopState(FrameUpdateManager manager, int id, string name)
        { Manager = manager; Id = id; Name = name; }
    }

    internal sealed class DriverState
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly LoopState LoopState;
        internal readonly UpdateLoop Loop;
        internal readonly string Name;
        internal readonly UpdatePhaseMask Phases;
        internal readonly UpdatePhase[] OrderedPhases;
        internal bool IsActive = true;
        internal int ExpectedPhaseIndex;
        internal long ExpectedSequence = 1;
        internal bool HasSample;
        internal FrameTimeSample Sample;

        internal DriverState(FrameUpdateManager manager, LoopState loopState, UpdateLoop loop,
            string name, UpdatePhaseMask phases, UpdatePhase[] orderedPhases)
        {
            Manager = manager; LoopState = loopState; Loop = loop; Name = name;
            Phases = phases; OrderedPhases = orderedPhases;
        }
    }

    internal sealed class GroupState
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly int Id;
        internal readonly string Name;
        internal readonly GroupState Parent;
        internal readonly List<GroupState> Children = new List<GroupState>();
        internal readonly UpdateTimeSource RootTimeSource;
        internal double TimeScale;
        internal int PauseCount;
        internal bool IsActive = true;
        internal bool IsDefault;

        internal GroupState(FrameUpdateManager manager, int id, string name,
            GroupState parent, UpdateTimeSource rootTimeSource, double timeScale)
        {
            Manager = manager; Id = id; Name = name; Parent = parent;
            RootTimeSource = rootTimeSource; TimeScale = timeScale;
        }
    }

    internal sealed class ScopeState
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly int Id;
        internal readonly string Name;
        internal readonly ScopeState Parent;
        internal readonly List<ScopeState> Children = new List<ScopeState>();
        internal bool IsActive = true;
        internal bool IsDefault;

        internal ScopeState(FrameUpdateManager manager, int id, string name, ScopeState parent)
        { Manager = manager; Id = id; Name = name; Parent = parent; }
    }

    internal sealed class PauseState
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly GroupState Group;
        internal readonly ScopeState Owner;
        internal bool IsGlobal;
        internal bool IsActive = true;

        internal PauseState(FrameUpdateManager manager, GroupState group, ScopeState owner, bool isGlobal)
        { Manager = manager; Group = group; Owner = owner; IsGlobal = isGlobal; }
    }

    internal sealed class EntrySlot
    {
        internal readonly int Id;
        internal int Generation = 1;
        internal Entry Entry;
        internal bool Retired;
        internal EntrySlot(int id) { Id = id; }
    }

    internal sealed class Entry
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly int Slot;
        internal readonly int Generation;
        internal readonly long RegistrationOrder;
        internal readonly IFrameUpdate Target;
        internal readonly IUpdateLifetime Lifetime;
        internal readonly LoopState LoopState;
        internal readonly UpdateLoop Loop;
        internal readonly UpdatePhase Phase;
        internal GroupState GroupState;
        internal ScopeState ScopeState;
        internal int Priority;
        internal UpdateSchedule Schedule;
        internal UpdateWorkClass WorkClass;
        internal bool Enabled;
        internal double MaxCallbackDeltaSeconds;
        internal bool IsActive = true;
        internal long ScheduleVersion;
        internal double ScheduleClock;
        internal double AccumulatedTime;
        internal double NextDeadline;
        internal long FrameCounter;
        internal long NextFrame;
        internal double ElapsedTime;
        internal long ExecutionCount;
        internal long DeferredCount;
        internal long FaultedCount;
        internal long ConsecutiveDeferredCount;
        internal long LifetimeSkipCount;
        internal long DisabledSkipCount;
        internal long PausedSkipCount;
        internal long FrequencySkipCount;
        internal long LastExecutionTimestamp;
        internal double LastDeltaTime;

        internal Entry(FrameUpdateManager manager, EntrySlot slot, long registrationOrder,
            IFrameUpdate target, IUpdateLifetime lifetime, LoopState loopState, UpdateLoop loop,
            UpdatePhase phase, GroupState groupState, ScopeState scopeState, int priority,
            UpdateSchedule schedule, UpdateWorkClass workClass, bool enabled,
            double maxCallbackDeltaSeconds)
        {
            Manager = manager; Slot = slot.Id; Generation = slot.Generation; RegistrationOrder = registrationOrder;
            Target = target; Lifetime = lifetime; LoopState = loopState; Loop = loop; Phase = phase;
            GroupState = groupState; ScopeState = scopeState; Priority = priority; Schedule = schedule;
            WorkClass = workClass; Enabled = enabled; MaxCallbackDeltaSeconds = maxCallbackDeltaSeconds;
            ScheduleVersion = 1;
            switch (schedule.Kind)
            {
                case UpdateScheduleKind.AtFps:
                case UpdateScheduleKind.AtInterval:
                    NextDeadline = schedule.PeriodSeconds + schedule.InitialOffsetSeconds;
                    break;
                case UpdateScheduleKind.EveryNFrames:
                    NextFrame = schedule.FrameCount + schedule.Offset;
                    break;
            }
        }
    }

    internal readonly struct RegistrationKey : IEquatable<RegistrationKey>
    {
        internal readonly IFrameUpdate Target;
        internal readonly LoopState Loop;
        internal readonly UpdatePhase Phase;

        internal RegistrationKey(IFrameUpdate target, LoopState loop, UpdatePhase phase)
        { Target = target; Loop = loop; Phase = phase; }

        public bool Equals(RegistrationKey other)
        { return ReferenceEquals(Target, other.Target) && ReferenceEquals(Loop, other.Loop) && Phase == other.Phase; }
        public override bool Equals(object obj) { return obj is RegistrationKey && Equals((RegistrationKey)obj); }
        public override int GetHashCode()
        { unchecked { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Target) * 397 ^ (Loop == null ? 0 : Loop.Id) * 31 ^ (int)Phase; } }
    }

    internal readonly struct EntryCandidate
    {
        internal readonly Entry Entry;
        internal readonly GroupState Group;
        internal readonly UpdateSchedule Schedule;
        internal readonly long ScheduleVersion;
        internal readonly double RawDelta;
        internal readonly bool Due;

        internal EntryCandidate(Entry entry, GroupState group, UpdateSchedule schedule,
            long scheduleVersion, double rawDelta, bool due)
        { Entry = entry; Group = group; Schedule = schedule; ScheduleVersion = scheduleVersion; RawDelta = rawDelta; Due = due; }
    }
}
