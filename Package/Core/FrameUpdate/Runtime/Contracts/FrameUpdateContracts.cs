using System;
using System.Collections.Generic;

namespace WFrameWork.Core.FrameUpdate
{
    public enum UpdatePhase
    {
        EarlyUpdate,
        NormalUpdate,
        LateUpdate
    }

    [Flags]
    public enum UpdatePhaseMask
    {
        Early = 1,
        Normal = 2,
        Late = 4,
        All = Early | Normal | Late
    }

    public enum UpdateTimeSource
    {
        Scaled,
        Unscaled
    }

    public enum UpdateWorkClass
    {
        Required,
        Deferrable
    }

    public enum UpdateScheduleKind
    {
        EveryStep,
        AtFps,
        EveryNFrames,
        AtInterval
    }

    public enum DiagnosticsLevel
    {
        Off,
        Basic,
        Detailed
    }

    public enum ExceptionPolicy
    {
        DisableAndReport,
        Propagate
    }

    public interface IFrameUpdate
    {
        void OnFrameUpdate(in FrameUpdateContext context);
    }

    public interface IUpdateLifetime
    {
        bool IsAlive { get; }
    }

    public interface IUpdateSchedulePolicy
    {
        UpdateSchedule Evaluate(in UpdatePolicyContext context);
    }

    public static class UpdatePriority
    {
        public const int Early = -100;
        public const int Normal = 0;
        public const int Late = 100;
    }

    public readonly struct UpdateSchedule : IEquatable<UpdateSchedule>
    {
        public UpdateScheduleKind Kind { get; }
        public double PeriodSeconds { get; }
        public int FrameCount { get; }
        public int Offset { get; }
        public double InitialOffsetSeconds { get; }

        private UpdateSchedule(UpdateScheduleKind kind, double periodSeconds,
            int frameCount, int offset, double initialOffsetSeconds)
        {
            Kind = kind;
            PeriodSeconds = periodSeconds;
            FrameCount = frameCount;
            Offset = offset;
            InitialOffsetSeconds = initialOffsetSeconds;
        }

        public static UpdateSchedule EveryStep()
        {
            return new UpdateSchedule(UpdateScheduleKind.EveryStep, 0, 0, 0, 0);
        }

        public static UpdateSchedule AtFps(double fps, double initialOffsetSeconds = 0)
        {
            ValidatePositiveFinite(fps, nameof(fps));
            ValidateNonNegativeFinite(initialOffsetSeconds, nameof(initialOffsetSeconds));
            double period = 1.0 / fps;
            ValidatePositiveFinite(period, nameof(fps));
            return new UpdateSchedule(UpdateScheduleKind.AtFps, period, 0, 0, initialOffsetSeconds);
        }

        public static UpdateSchedule EveryNFrames(int count, int offset = 0)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "Frame count must be positive.");
            if (offset < 0 || offset >= count)
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be in [0, count).");
            return new UpdateSchedule(UpdateScheduleKind.EveryNFrames, 0, count, offset, 0);
        }

        public static UpdateSchedule AtInterval(double seconds, double initialOffsetSeconds = 0)
        {
            ValidatePositiveFinite(seconds, nameof(seconds));
            ValidateNonNegativeFinite(initialOffsetSeconds, nameof(initialOffsetSeconds));
            return new UpdateSchedule(UpdateScheduleKind.AtInterval, seconds, 0, 0, initialOffsetSeconds);
        }

        public bool Equals(UpdateSchedule other)
        {
            return Kind == other.Kind && PeriodSeconds == other.PeriodSeconds &&
                FrameCount == other.FrameCount && Offset == other.Offset &&
                InitialOffsetSeconds == other.InitialOffsetSeconds;
        }

        public override bool Equals(object obj) { return obj is UpdateSchedule && Equals((UpdateSchedule)obj); }
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ PeriodSeconds.GetHashCode();
                hash = hash * 397 ^ FrameCount;
                hash = hash * 397 ^ Offset;
                return hash * 397 ^ InitialOffsetSeconds.GetHashCode();
            }
        }

        public static bool operator ==(UpdateSchedule left, UpdateSchedule right) { return left.Equals(right); }
        public static bool operator !=(UpdateSchedule left, UpdateSchedule right) { return !left.Equals(right); }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(UpdateScheduleKind), Kind))
                throw new ArgumentException("Unknown update schedule kind.", nameof(Kind));
            switch (Kind)
            {
                case UpdateScheduleKind.EveryStep:
                    return;
                case UpdateScheduleKind.EveryNFrames:
                    if (FrameCount <= 0 || Offset < 0 || Offset >= FrameCount)
                        throw new ArgumentException("Invalid every-N-frames schedule.", nameof(FrameCount));
                    return;
                case UpdateScheduleKind.AtFps:
                case UpdateScheduleKind.AtInterval:
                    ValidatePositiveFinite(PeriodSeconds, nameof(PeriodSeconds));
                    ValidateNonNegativeFinite(InitialOffsetSeconds, nameof(InitialOffsetSeconds));
                    return;
                default:
                    throw new ArgumentException("Unknown update schedule kind.", nameof(Kind));
            }
        }

        private static void ValidatePositiveFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and positive.");
        }

        private static void ValidateNonNegativeFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and non-negative.");
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case UpdateScheduleKind.EveryStep: return "EveryStep";
                case UpdateScheduleKind.EveryNFrames: return "EveryNFrames(" + FrameCount + ", " + Offset + ")";
                case UpdateScheduleKind.AtFps: return "AtFps(" + (1.0 / PeriodSeconds) + ")";
                default: return "AtInterval(" + PeriodSeconds + ")";
            }
        }
    }

    public readonly struct FrameTimeSample
    {
        public long Sequence { get; }
        public double ScaledDeltaTime { get; }
        public double UnscaledDeltaTime { get; }
        public long? RenderFrameId { get; }

        public FrameTimeSample(long sequence, double scaledDeltaTime,
            double unscaledDeltaTime, long? renderFrameId = null)
        {
            Sequence = sequence;
            ScaledDeltaTime = scaledDeltaTime;
            UnscaledDeltaTime = unscaledDeltaTime;
            RenderFrameId = renderFrameId;
        }
    }

    public readonly struct HostFrameSample
    {
        public long FrameId { get; }
        public double UnscaledDeltaTime { get; }

        public HostFrameSample(long frameId, double unscaledDeltaTime)
        {
            FrameId = frameId;
            UnscaledDeltaTime = unscaledDeltaTime;
        }
    }

    public readonly struct FrameUpdateContext
    {
        public UpdateHandle Handle { get; }
        public UpdateLoop Loop { get; }
        public UpdateGroup Group { get; }
        public UpdatePhase Phase { get; }
        public long Sequence { get; }
        public double DeltaTime { get; }
        public double RawDeltaTime { get; }
        public double ElapsedTime { get; }
        public double DiscardedDeltaTime { get; }

        internal FrameUpdateContext(UpdateHandle handle, UpdateLoop loop, UpdateGroup group,
            UpdatePhase phase, long sequence, double deltaTime, double rawDeltaTime,
            double elapsedTime, double discardedDeltaTime)
        {
            Handle = handle;
            Loop = loop;
            Group = group;
            Phase = phase;
            Sequence = sequence;
            DeltaTime = deltaTime;
            RawDeltaTime = rawDeltaTime;
            ElapsedTime = elapsedTime;
            DiscardedDeltaTime = discardedDeltaTime;
        }
    }

    public struct FrameUpdateOptions
    {
        public UpdateLoop? Loop { get; set; }
        public UpdatePhase? Phase { get; set; }
        public UpdateGroup? Group { get; set; }
        public UpdateScope? Scope { get; set; }
        public int? Priority { get; set; }
        public UpdateSchedule? Schedule { get; set; }
        public UpdateWorkClass? WorkClass { get; set; }
        public bool? Enabled { get; set; }
        public double? MaxCallbackDeltaSeconds { get; set; }

        public static FrameUpdateOptions Default { get { return default(FrameUpdateOptions); } }
    }

    public struct UpdateGroupOptions
    {
        public UpdateTimeSource? TimeSource { get; set; }
        public double? TimeScale { get; set; }

        public static UpdateGroupOptions Default { get { return default(UpdateGroupOptions); } }
    }

    public readonly struct FrameUpdateRequest
    {
        public IFrameUpdate Target { get; }
        public FrameUpdateOptions Options { get; }
        public IUpdateLifetime Lifetime { get; }

        public FrameUpdateRequest(IFrameUpdate target, FrameUpdateOptions options,
            IUpdateLifetime lifetime = null)
        {
            Target = target;
            Options = options;
            Lifetime = lifetime;
        }
    }

    public sealed class FrameUpdateConfig
    {
        public int InitialCapacity { get; set; }
        public UpdatePhase DefaultPhase { get; set; }
        public int DefaultPriority { get; set; }
        public UpdateSchedule DefaultSchedule { get; set; }
        public UpdateTimeSource DefaultTimeSource { get; set; }
        public double DefaultGroupTimeScale { get; set; }
        public UpdateWorkClass DefaultWorkClass { get; set; }
        public double LoopPhaseBudgetMilliseconds { get; set; }
        public double MaxCallbackDeltaSeconds { get; set; }
        public DiagnosticsLevel DiagnosticsLevel { get; set; }
        public int DetailedSampleEveryNSteps { get; set; }
        public int StatisticsWindowSamples { get; set; }
        public ExceptionPolicy ExceptionPolicy { get; set; }
        public int ErrorBufferCapacity { get; set; }

        public static FrameUpdateConfig Default
        {
            get
            {
                return new FrameUpdateConfig
                {
                    InitialCapacity = 256,
                    DefaultPhase = UpdatePhase.NormalUpdate,
                    DefaultPriority = UpdatePriority.Normal,
                    DefaultSchedule = UpdateSchedule.EveryStep(),
                    DefaultTimeSource = UpdateTimeSource.Scaled,
                    DefaultGroupTimeScale = 1,
                    DefaultWorkClass = UpdateWorkClass.Required,
                    LoopPhaseBudgetMilliseconds = 0,
                    MaxCallbackDeltaSeconds = 0,
                    DiagnosticsLevel = DiagnosticsLevel.Basic,
                    DetailedSampleEveryNSteps = 30,
                    StatisticsWindowSamples = 120,
                    ExceptionPolicy = ExceptionPolicy.DisableAndReport,
                    ErrorBufferCapacity = 64
                };
            }
        }

        internal FrameUpdateConfig CloneAndValidate()
        {
            if (InitialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(InitialCapacity));
            if (!Enum.IsDefined(typeof(UpdatePhase), DefaultPhase)) throw new ArgumentOutOfRangeException(nameof(DefaultPhase));
            if (!Enum.IsDefined(typeof(UpdateTimeSource), DefaultTimeSource)) throw new ArgumentOutOfRangeException(nameof(DefaultTimeSource));
            if (!Enum.IsDefined(typeof(UpdateWorkClass), DefaultWorkClass)) throw new ArgumentOutOfRangeException(nameof(DefaultWorkClass));
            if (!Enum.IsDefined(typeof(DiagnosticsLevel), DiagnosticsLevel)) throw new ArgumentOutOfRangeException(nameof(DiagnosticsLevel));
            if (!Enum.IsDefined(typeof(ExceptionPolicy), ExceptionPolicy)) throw new ArgumentOutOfRangeException(nameof(ExceptionPolicy));
            ValidateNonNegativeFinite(DefaultGroupTimeScale, nameof(DefaultGroupTimeScale));
            ValidateNonNegativeFinite(LoopPhaseBudgetMilliseconds, nameof(LoopPhaseBudgetMilliseconds));
            ValidateNonNegativeFinite(MaxCallbackDeltaSeconds, nameof(MaxCallbackDeltaSeconds));
            if (DetailedSampleEveryNSteps <= 0) throw new ArgumentOutOfRangeException(nameof(DetailedSampleEveryNSteps));
            if (StatisticsWindowSamples <= 0) throw new ArgumentOutOfRangeException(nameof(StatisticsWindowSamples));
            if (ErrorBufferCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(ErrorBufferCapacity));
            DefaultSchedule.Validate();
            return new FrameUpdateConfig
            {
                InitialCapacity = InitialCapacity,
                DefaultPhase = DefaultPhase,
                DefaultPriority = DefaultPriority,
                DefaultSchedule = DefaultSchedule,
                DefaultTimeSource = DefaultTimeSource,
                DefaultGroupTimeScale = DefaultGroupTimeScale,
                DefaultWorkClass = DefaultWorkClass,
                LoopPhaseBudgetMilliseconds = LoopPhaseBudgetMilliseconds,
                MaxCallbackDeltaSeconds = MaxCallbackDeltaSeconds,
                DiagnosticsLevel = DiagnosticsLevel,
                DetailedSampleEveryNSteps = DetailedSampleEveryNSteps,
                StatisticsWindowSamples = StatisticsWindowSamples,
                ExceptionPolicy = ExceptionPolicy,
                ErrorBufferCapacity = ErrorBufferCapacity
            };
        }

        private static void ValidateNonNegativeFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name, "Value must be finite and non-negative.");
        }
    }

    public readonly struct UpdatePolicyContext
    {
        public UpdateHandle Handle { get; }
        public UpdateSchedule Schedule { get; }
        public int Priority { get; }
        public UpdateEntryStats Stats { get; }

        public UpdatePolicyContext(UpdateHandle handle, UpdateSchedule schedule,
            int priority, UpdateEntryStats stats)
        {
            Handle = handle;
            Schedule = schedule;
            Priority = priority;
            Stats = stats;
        }
    }

    public readonly struct FrameRegistrationInfo
    {
        public UpdateHandle Handle { get; }
        public IFrameUpdate Target { get; }
        public UpdateLoop Loop { get; }
        public UpdatePhase Phase { get; }
        public UpdateGroup Group { get; }
        public UpdateScope Scope { get; }
        public int Priority { get; }
        public UpdateSchedule Schedule { get; }
        public UpdateWorkClass WorkClass { get; }
        public bool Enabled { get; }
        public bool IsEnabled { get { return Enabled; } }
        public bool IsActive { get { return Handle.IsValid; } }
        public bool IsAlive { get; }
        public bool IsPaused { get; }
        public double AccumulatedTime { get; }
        public double PendingTime { get { return AccumulatedTime; } }
        public double RemainingTime { get; }
        public double RemainingWaiting { get { return RemainingTime; } }
        public double ElapsedTime { get; }
        public long ExecutionCount { get; }
        public long ExecutedCount { get { return ExecutionCount; } }
        public long DeferredCount { get; }
        public long FaultedCount { get; }
        public double MaxCallbackDeltaSeconds { get; }

        internal FrameRegistrationInfo(UpdateHandle handle, IFrameUpdate target, UpdateLoop loop,
            UpdatePhase phase, UpdateGroup group, UpdateScope scope, int priority,
            UpdateSchedule schedule, UpdateWorkClass workClass, bool enabled, bool alive,
            bool paused, double accumulatedTime, double remainingTime, double elapsedTime,
            long executionCount, long deferredCount, long faultedCount, double maxDelta)
        {
            Handle = handle;
            Target = target;
            Loop = loop;
            Phase = phase;
            Group = group;
            Scope = scope;
            Priority = priority;
            Schedule = schedule;
            WorkClass = workClass;
            Enabled = enabled;
            IsAlive = alive;
            IsPaused = paused;
            AccumulatedTime = accumulatedTime;
            RemainingTime = remainingTime;
            ElapsedTime = elapsedTime;
            ExecutionCount = executionCount;
            DeferredCount = deferredCount;
            FaultedCount = faultedCount;
            MaxCallbackDeltaSeconds = maxDelta;
        }
    }

    public readonly struct FrameUpdateStats
    {
        public DiagnosticsLevel Level { get; }
        public long HostFrameCount { get; }
        public bool HasHostFrame { get; }
        public double HostFps { get; }
        public long TotalTicks { get; }
        public long TotalExecuted { get; }
        public long TotalDeferred { get; }
        public long TotalFaulted { get; }
        public int ActiveRegistrationCount { get; }

        internal FrameUpdateStats(DiagnosticsLevel level, long hostFrameCount, bool hasHostFrame,
            double hostFps, long totalTicks, long totalExecuted, long totalDeferred,
            long totalFaulted, int activeRegistrationCount)
        {
            Level = level;
            HostFrameCount = hostFrameCount;
            HasHostFrame = hasHostFrame;
            HostFps = hostFps;
            TotalTicks = totalTicks;
            TotalExecuted = totalExecuted;
            TotalDeferred = totalDeferred;
            TotalFaulted = totalFaulted;
            ActiveRegistrationCount = activeRegistrationCount;
        }
    }

    public readonly struct UpdateLoopStats
    {
        public UpdateLoop Loop { get; }
        public string Name { get; }
        public bool HasDriver { get; }
        public string DriverName { get; }
        public int RegisteredCount { get; }
        public long TickCount { get; }
        public long ExecutedCount { get; }
        public long DeferredCount { get; }
        public long FaultedCount { get; }
        public double LastTickHz { get; }
        public double TotalCpuMilliseconds { get; }

        internal UpdateLoopStats(UpdateLoop loop, string name, bool hasDriver, string driverName,
            int registeredCount, long tickCount, long executedCount, long deferredCount,
            long faultedCount, double lastTickHz, double totalCpuMilliseconds)
        {
            Loop = loop; Name = name; HasDriver = hasDriver; DriverName = driverName;
            RegisteredCount = registeredCount; TickCount = tickCount; ExecutedCount = executedCount;
            DeferredCount = deferredCount; FaultedCount = faultedCount; LastTickHz = lastTickHz;
            TotalCpuMilliseconds = totalCpuMilliseconds;
        }
    }

    public readonly struct UpdateGroupStats
    {
        public UpdateGroup Group { get; }
        public string Name { get; }
        public int DirectRegistrationCount { get; }
        public int RegistrationCount { get; }
        public int PauseCount { get; }
        public double TimeScale { get; }
        public double EffectiveTimeScale { get; }
        public long ExecutedCount { get; }
        public long DeferredCount { get; }

        internal UpdateGroupStats(UpdateGroup group, string name, int directCount, int count,
            int pauseCount, double timeScale, double effectiveTimeScale, long executed, long deferred)
        {
            Group = group; Name = name; DirectRegistrationCount = directCount; RegistrationCount = count;
            PauseCount = pauseCount; TimeScale = timeScale; EffectiveTimeScale = effectiveTimeScale;
            ExecutedCount = executed; DeferredCount = deferred;
        }
    }

    public readonly struct UpdateEntryStats
    {
        public UpdateHandle Handle { get; }
        public UpdateLoop Loop { get; }
        public UpdatePhase Phase { get; }
        public UpdateGroup Group { get; }
        public int Priority { get; }
        public bool Enabled { get; }
        public long ExecutionCount { get; }
        public long ExecutedCount { get { return ExecutionCount; } }
        public long DeferredCount { get; }
        public long FaultedCount { get; }
        public long ConsecutiveDeferredCount { get; }
        public double LastDeltaTime { get; }
        public double ElapsedTime { get; }
        public double AccumulatedTime { get; }

        internal UpdateEntryStats(UpdateHandle handle, UpdateLoop loop, UpdatePhase phase,
            UpdateGroup group, int priority, bool enabled, long executionCount,
            long deferredCount, long faultedCount, long consecutiveDeferredCount,
            double lastDeltaTime, double elapsedTime, double accumulatedTime)
        {
            Handle = handle; Loop = loop; Phase = phase; Group = group; Priority = priority;
            Enabled = enabled; ExecutionCount = executionCount; DeferredCount = deferredCount;
            FaultedCount = faultedCount; ConsecutiveDeferredCount = consecutiveDeferredCount;
            LastDeltaTime = lastDeltaTime; ElapsedTime = elapsedTime; AccumulatedTime = accumulatedTime;
        }
    }

    public readonly struct FrameUpdateError
    {
        public long ErrorSequence { get; }
        public UpdateHandle Handle { get; }
        public UpdateLoop Loop { get; }
        public UpdatePhase Phase { get; }
        public long Sequence { get; }
        public Exception Exception { get; }
        public string Message { get { return Exception == null ? null : Exception.Message; } }
        public DateTimeOffset OccurredAt { get; }

        internal FrameUpdateError(long errorSequence, UpdateHandle handle, UpdateLoop loop,
            UpdatePhase phase, long sequence, Exception exception)
        {
            ErrorSequence = errorSequence; Handle = handle; Loop = loop; Phase = phase;
            Sequence = sequence; Exception = exception; OccurredAt = DateTimeOffset.UtcNow;
        }
    }
}
