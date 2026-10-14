using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WFrameWork.Core.FrameUpdate
{
    /// <summary>
    /// A small, explicit scheduler for frame work. The manager owns no thread and is
    /// advanced only by a driver supplied by the host application.
    /// </summary>
    public sealed class FrameUpdateManager : IDisposable
    {
        private const double TimeEpsilon = 1e-10;

        private readonly FrameUpdateConfig _config;
        private readonly int _ownerThreadId;
        private readonly List<LoopState> _loops = new List<LoopState>();
        private readonly List<GroupState> _groups = new List<GroupState>();
        private readonly List<ScopeState> _scopes = new List<ScopeState>();
        private readonly List<PauseState> _pauses = new List<PauseState>();
        private readonly Dictionary<RegistrationKey, Entry> _registrations;
        private readonly Dictionary<int, EntrySlot> _slots = new Dictionary<int, EntrySlot>();
        private readonly Stack<int> _freeSlots = new Stack<int>();
        private readonly List<FrameUpdateError> _errors = new List<FrameUpdateError>();
        private readonly Queue<double> _hostDeltas = new Queue<double>();
        private readonly List<Entry> _scratchEntries = new List<Entry>();
        private readonly List<ScopeState> _scratchScopes = new List<ScopeState>();
        private readonly List<Entry> _tickSnapshot;
        private readonly List<EntryCandidate> _tickCandidates;

        private int _nextLoopId = 1;
        private int _nextGroupId = 1;
        private int _nextScopeId = 1;
        private int _nextSlotId = 1;
        private long _registrationOrder;
        private long _errorSequence;
        private bool _inTick;
        private bool _disposed;
        private int _globalPauseCount;
        private long? _lastHostFrameId;
        private long _hostFrameCount;
        private double _hostDeltaSum;
        private long _totalTicks;
        private long _totalExecuted;
        private long _totalDeferred;
        private long _totalFaulted;
        private int _activeRegistrationCount;

        public UpdateLoop DefaultLoop { get; }
        public UpdateGroup DefaultGroup { get; }
        public UpdateScope DefaultScope { get; }
        public bool IsDisposed { get { return _disposed; } }

        public FrameUpdateManager(FrameUpdateConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _config = config.CloneAndValidate();
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            _registrations = new Dictionary<RegistrationKey, Entry>(_config.InitialCapacity);
            _tickSnapshot = new List<Entry>(_config.InitialCapacity);
            _tickCandidates = new List<EntryCandidate>(_config.InitialCapacity);

            var loopState = new LoopState(this, _nextLoopId++, "Default") { IsDefault = true };
            _loops.Add(loopState);
            DefaultLoop = new UpdateLoop(this, loopState);

            var groupState = new GroupState(this, _nextGroupId++, "Default", null,
                _config.DefaultTimeSource, _config.DefaultGroupTimeScale) { IsDefault = true };
            _groups.Add(groupState);
            DefaultGroup = new UpdateGroup(this, groupState);

            var scopeState = new ScopeState(this, _nextScopeId++, "Default", null) { IsDefault = true };
            _scopes.Add(scopeState);
            DefaultScope = new UpdateScope(this, scopeState);
        }

        public UpdateLoop CreateLoop(string name)
        {
            EnsureUsable();
            EnsureOwnerThread();
            ValidateName(name, nameof(name));
            var state = new LoopState(this, _nextLoopId++, name);
            _loops.Add(state);
            return new UpdateLoop(this, state);
        }

        public bool RemoveLoop(UpdateLoop loop)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            if (!TryGetLoop(loop, out var state) || state.IsDefault || state.Driver != null || state.IsTicking) return false;
            for (int i = 0; i < state.Entries.Length; i++)
                for (int j = 0; j < state.Entries[i].Count; j++)
                    if (state.Entries[i][j].IsActive) return false;
            state.IsActive = false;
            return true;
        }

        public LoopDriverHandle BindDriver(UpdateLoop loop, string name,
            UpdatePhaseMask phases = UpdatePhaseMask.All)
        {
            EnsureUsable();
            EnsureOwnerThread();
            ValidateName(name, nameof(name));
            if (!TryGetLoop(loop, out var state)) throw new ArgumentException("The loop does not belong to this manager.", nameof(loop));
            ValidatePhaseMask(phases);
            if (state.Driver != null && state.Driver.IsActive)
                throw new InvalidOperationException("The loop already has an active driver.");
            var ordered = GetOrderedPhases(phases);
            for (int i = 0; i < state.Entries.Length; i++)
            {
                var entries = state.Entries[i];
                for (int j = 0; j < entries.Count; j++)
                    if (entries[j].IsActive && !HasPhase(phases, entries[j].Phase))
                        throw new InvalidOperationException("The driver does not cover every registered phase.");
            }
            var driverState = new DriverState(this, state, loop, name, phases, ordered);
            state.Driver = driverState;
            return new LoopDriverHandle(driverState);
        }

        public UpdateGroup CreateGroup(string name, in UpdateGroupOptions options,
            UpdateGroup parent = default(UpdateGroup))
        {
            EnsureUsable();
            EnsureOwnerThread();
            ValidateName(name, nameof(name));
            GroupState parentState = null;
            if (!parent.IsDefault)
            {
                if (!TryGetGroup(parent, out parentState))
                    throw new ArgumentException("The parent group does not belong to this manager.", nameof(parent));
                if (options.TimeSource.HasValue)
                    throw new ArgumentException("A child group inherits its parent's time source.", nameof(options));
            }

            var timeSource = parentState == null
                ? (options.TimeSource ?? _config.DefaultTimeSource)
                : parentState.RootTimeSource;
            if (!Enum.IsDefined(typeof(UpdateTimeSource), timeSource))
                throw new ArgumentOutOfRangeException(nameof(options));
            var timeScale = options.TimeScale ?? _config.DefaultGroupTimeScale;
            ValidateNonNegativeFinite(timeScale, nameof(options.TimeScale));
            var state = new GroupState(this, _nextGroupId++, name, parentState, timeSource, timeScale);
            _groups.Add(state);
            parentState?.Children.Add(state);
            return new UpdateGroup(this, state);
        }

        public UpdateScope CreateScope(string name, UpdateScope parent = default(UpdateScope))
        {
            EnsureUsable();
            EnsureOwnerThread();
            ValidateName(name, nameof(name));
            ScopeState parentState;
            if (parent.IsDefault) parentState = GetRequiredScope(DefaultScope);
            else if (!TryGetScope(parent, out parentState))
                throw new ArgumentException("The parent scope does not belong to this manager.", nameof(parent));
            var state = new ScopeState(this, _nextScopeId++, name, parentState);
            _scopes.Add(state);
            parentState.Children.Add(state);
            return new UpdateScope(this, state);
        }

        public UpdateHandle Register(IFrameUpdate target, in FrameUpdateOptions options,
            IUpdateLifetime lifetime = null)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.GetType().IsValueType)
                throw new ArgumentException("A value type cannot be registered because its boxed identity is unstable.", nameof(target));
            var resolved = ResolveOptions(options);
            var key = new RegistrationKey(target, resolved.LoopState, resolved.Phase);
            if (_registrations.TryGetValue(key, out var existing))
            {
                if (SameRegistration(existing, resolved, lifetime)) return MakeHandle(existing);
                throw new InvalidOperationException("The registration key already exists with a different configuration or lifetime.");
            }
            return MakeHandle(CreateEntry(key, target, lifetime, resolved));
        }

        public void RegisterBatch(IReadOnlyList<FrameUpdateRequest> requests, List<UpdateHandle> results)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (results == null) throw new ArgumentNullException(nameof(results));

            var resolved = new ResolvedRequest[requests.Count];
            var local = new Dictionary<RegistrationKey, ResolvedRequest>(requests.Count);
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request.Target == null) throw new ArgumentNullException("requests[" + i + "].Target");
                if (request.Target.GetType().IsValueType)
                    throw new ArgumentException("A value type cannot be registered.", "requests[" + i + "].Target");
                var options = ResolveOptions(request.Options);
                var item = new ResolvedRequest(request.Target, request.Lifetime, options);
                var key = new RegistrationKey(request.Target, options.LoopState, options.Phase);
                if (local.TryGetValue(key, out var previous))
                {
                    if (!SameRegistration(previous, item))
                        throw new InvalidOperationException("The batch contains conflicting duplicate registrations.");
                }
                else if (_registrations.TryGetValue(key, out var existing))
                {
                    if (!SameRegistration(existing, options, request.Lifetime))
                        throw new InvalidOperationException("The batch conflicts with an existing registration.");
                }
                else
                {
                    local.Add(key, item);
                }
                resolved[i] = item;
            }

            results.Clear();
            var handles = new Dictionary<RegistrationKey, UpdateHandle>(local.Count);
            for (int i = 0; i < resolved.Length; i++)
            {
                var item = resolved[i];
                var key = new RegistrationKey(item.Target, item.Options.LoopState, item.Options.Phase);
                if (handles.TryGetValue(key, out var handle))
                {
                    results.Add(handle);
                    continue;
                }
                if (_registrations.TryGetValue(key, out var existing))
                    handle = MakeHandle(existing);
                else
                    handle = MakeHandle(CreateEntry(key, item.Target, item.Lifetime, item.Options));
                handles.Add(key, handle);
                results.Add(handle);
            }
        }

        public bool Unregister(UpdateHandle handle)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            var entry = GetEntry(handle);
            if (entry == null) return false;
            RemoveEntry(entry);
            return true;
        }

        public int ReleaseScope(UpdateScope scope)
        {
            if (_disposed) return 0;
            EnsureOwnerThread();
            if (!TryGetScope(scope, out var state) || state.IsDefault) return 0;

            _scratchScopes.Clear();
            CollectScopeTree(state, _scratchScopes);
            for (int i = 0; i < _pauses.Count; i++)
            {
                var pause = _pauses[i];
                if (pause.IsActive && ContainsScope(_scratchScopes, pause.Owner)) ReleasePause(pause);
            }

            _scratchEntries.Clear();
            foreach (var slot in _slots.Values)
                if (slot.Entry != null && slot.Entry.IsActive && ContainsScope(_scratchScopes, slot.Entry.ScopeState))
                    _scratchEntries.Add(slot.Entry);
            int released = 0;
            for (int i = 0; i < _scratchEntries.Count; i++)
                if (Unregister(MakeHandle(_scratchEntries[i]))) released++;

            for (int i = 0; i < _scratchScopes.Count; i++)
            {
                var child = _scratchScopes[i];
                child.IsActive = false;
                child.Parent?.Children.Remove(child);
            }
            _scratchScopes.Clear();
            _scratchEntries.Clear();
            return released;
        }

        public bool RemoveGroup(UpdateGroup group)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            if (!TryGetGroup(group, out var state) || state.IsDefault || state.Children.Count != 0 || state.PauseCount != 0)
                return false;
            foreach (var slot in _slots.Values)
                if (slot.Entry != null && slot.Entry.IsActive && ReferenceEquals(slot.Entry.GroupState, state))
                    return false;
            state.IsActive = false;
            state.Parent?.Children.Remove(state);
            return true;
        }

        public bool SetEnabled(UpdateHandle handle, bool enabled)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            var entry = GetEntry(handle);
            if (entry == null) return false;
            entry.Enabled = enabled;
            return true;
        }

        public bool SetSchedule(UpdateHandle handle, UpdateSchedule schedule)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            schedule.Validate();
            var entry = GetEntry(handle);
            if (entry == null) return false;
            if (entry.Schedule == schedule) return true;
            entry.Schedule = schedule;
            entry.ScheduleVersion++;
            entry.FrameCounter = 0;
            entry.NextFrame = 0;
            if (schedule.Kind == UpdateScheduleKind.EveryNFrames)
                entry.NextFrame = schedule.FrameCount + schedule.Offset;
            else if (schedule.Kind == UpdateScheduleKind.AtFps || schedule.Kind == UpdateScheduleKind.AtInterval)
                entry.NextDeadline = entry.ScheduleClock + schedule.PeriodSeconds + schedule.InitialOffsetSeconds;
            return true;
        }

        public bool SetPriority(UpdateHandle handle, int priority)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            var entry = GetEntry(handle);
            if (entry == null) return false;
            if (entry.Priority != priority)
            {
                entry.Priority = priority;
                entry.LoopState.Dirty[PhaseIndex(entry.Phase)] = true;
            }
            return true;
        }

        public bool SetGroup(UpdateHandle handle, UpdateGroup group)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            if (!TryGetGroup(group, out var state)) return false;
            var entry = GetEntry(handle);
            if (entry == null) return false;
            entry.GroupState = state;
            return true;
        }

        public bool SetScope(UpdateHandle handle, UpdateScope scope)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            if (!TryGetScope(scope, out var state)) return false;
            var entry = GetEntry(handle);
            if (entry == null) return false;
            entry.ScopeState = state;
            return true;
        }

        public bool SetGroupTimeScale(UpdateGroup group, double timeScale)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            ValidateNonNegativeFinite(timeScale, nameof(timeScale));
            if (!TryGetGroup(group, out var state)) return false;
            state.TimeScale = timeScale;
            return true;
        }

        public bool SetPhaseBudget(UpdateLoop loop, UpdatePhase phase, double milliseconds)
        {
            if (_disposed) return false;
            EnsureOwnerThread();
            ValidatePhase(phase);
            ValidateNonNegativeFinite(milliseconds, nameof(milliseconds));
            if (!TryGetLoop(loop, out var state)) return false;
            int index = PhaseIndex(phase);
            state.BudgetMilliseconds[index] = milliseconds;
            state.BudgetOverrides[index] = true;
            return true;
        }

        public PauseHandle PauseAll(UpdateScope owner = default(UpdateScope))
        {
            EnsureUsable();
            EnsureOwnerThread();
            var ownerState = ResolveOwner(owner);
            var state = new PauseState(this, null, ownerState, true);
            _pauses.Add(state);
            _globalPauseCount++;
            return new PauseHandle(state);
        }

        public PauseHandle PauseGroup(UpdateGroup group, UpdateScope owner = default(UpdateScope))
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (!TryGetGroup(group, out var groupState))
                throw new ArgumentException("The group does not belong to this manager.", nameof(group));
            var ownerState = ResolveOwner(owner);
            var state = new PauseState(this, groupState, ownerState, false);
            _pauses.Add(state);
            groupState.PauseCount++;
            return new PauseHandle(state);
        }

        public void Tick(LoopDriverHandle driver, UpdatePhase phase, in FrameTimeSample time)
        {
            EnsureUsable();
            EnsureOwnerThread();
            ValidatePhase(phase);
            var driverState = driver == null ? null : driver.State;
            if (driverState == null || !ReferenceEquals(driverState.Manager, this) || !driverState.IsActive)
                throw new InvalidOperationException("The driver is not active for this manager.");
            if (_inTick)
                throw new InvalidOperationException("FrameUpdateManager does not allow reentrant Tick calls.");
            ValidateTime(time);
            int expectedIndex = driverState.ExpectedPhaseIndex;
            if (time.Sequence != driverState.ExpectedSequence)
                throw new InvalidOperationException("The driver sequence is not the next expected sequence.");
            if (expectedIndex >= driverState.OrderedPhases.Length || driverState.OrderedPhases[expectedIndex] != phase)
                throw new InvalidOperationException("The driver phases must arrive in their declared order.");
            if (driverState.HasSample && !SameSample(driverState.Sample, time))
                throw new InvalidOperationException("All phases in one driver cycle must use the same time sample.");

            if (!driverState.HasSample)
            {
                driverState.Sample = time;
                driverState.HasSample = true;
            }

            bool completed = false;
            try
            {
                RunPhase(driverState, phase, time);
                completed = true;
            }
            catch
            {
                // Propagate still ends the accepted phase. This lets a host decide
                // whether to continue with the next phase or release the driver,
                // without leaving the protocol permanently wedged.
                completed = true;
                throw;
            }
            finally
            {
                if (!_disposed && driverState.IsActive && completed)
                    CompleteDriverPhase(driverState);
            }
        }

        public bool TryGetRegistration(UpdateHandle handle, out FrameRegistrationInfo info)
        {
            EnsureUsable();
            EnsureOwnerThread();
            var entry = GetEntry(handle);
            if (entry == null) { info = default(FrameRegistrationInfo); return false; }
            info = MakeRegistrationInfo(entry);
            return true;
        }

        public void RecordHostFrame(in HostFrameSample sample)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (double.IsNaN(sample.UnscaledDeltaTime) || double.IsInfinity(sample.UnscaledDeltaTime) || sample.UnscaledDeltaTime < 0)
                throw new ArgumentOutOfRangeException(nameof(sample));
            if (_lastHostFrameId.HasValue && sample.FrameId <= _lastHostFrameId.Value)
                throw new InvalidOperationException("Host frame ids must be strictly increasing.");
            _lastHostFrameId = sample.FrameId;
            _hostFrameCount++;
            _hostDeltas.Enqueue(sample.UnscaledDeltaTime);
            _hostDeltaSum += sample.UnscaledDeltaTime;
            while (_hostDeltas.Count > _config.StatisticsWindowSamples)
                _hostDeltaSum -= _hostDeltas.Dequeue();
        }

        public FrameUpdateStats GetStats()
        {
            EnsureUsable();
            EnsureOwnerThread();
            double fps = _hostDeltaSum > 0 ? _hostDeltas.Count / _hostDeltaSum : 0;
            return new FrameUpdateStats(_config.DiagnosticsLevel, _hostFrameCount,
                _lastHostFrameId.HasValue, fps, _totalTicks, _totalExecuted,
                _totalDeferred, _totalFaulted, _activeRegistrationCount);
        }

        public void CopyLoopStats(List<UpdateLoopStats> results)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _loops.Count; i++)
            {
                var loop = _loops[i];
                if (!loop.IsActive) continue;
                int count = 0;
                for (int phase = 0; phase < loop.Entries.Length; phase++)
                    for (int j = 0; j < loop.Entries[phase].Count; j++)
                        if (loop.Entries[phase][j].IsActive) count++;
                results.Add(new UpdateLoopStats(new UpdateLoop(this, loop), loop.Name,
                    loop.Driver != null && loop.Driver.IsActive, loop.Driver?.Name, count,
                    loop.CompletedTicks, loop.ExecutedCount, loop.DeferredCount,
                    loop.FaultedCount, loop.LastTickHz, loop.TotalCpuMilliseconds));
            }
        }

        public void CopyGroupStats(List<UpdateGroupStats> results)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            for (int i = 0; i < _groups.Count; i++)
            {
                var group = _groups[i];
                if (!group.IsActive) continue;
                int direct = 0, total = 0;
                long executed = 0, deferred = 0;
                foreach (var slot in _slots.Values)
                {
                    var entry = slot.Entry;
                    if (entry == null || !entry.IsActive || !IsGroupWithin(entry.GroupState, group)) continue;
                    total++;
                    if (ReferenceEquals(entry.GroupState, group)) direct++;
                    executed += entry.ExecutionCount;
                    deferred += entry.DeferredCount;
                }
                results.Add(new UpdateGroupStats(new UpdateGroup(this, group), group.Name,
                    direct, total, group.PauseCount, group.TimeScale,
                    GetEffectiveScale(group), executed, deferred));
            }
        }

        public void CopyEntryStats(List<UpdateEntryStats> results)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (var slot in _slots.Values)
            {
                var entry = slot.Entry;
                if (entry == null || !entry.IsActive) continue;
                results.Add(MakeEntryStats(entry));
            }
        }

        public void CopyErrors(List<FrameUpdateError> results)
        {
            EnsureUsable();
            EnsureOwnerThread();
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(_errors);
        }

        public void Dispose()
        {
            if (_disposed) return;
            EnsureOwnerThread();
            _disposed = true;
            for (int i = 0; i < _pauses.Count; i++) _pauses[i].IsActive = false;
            for (int i = 0; i < _loops.Count; i++)
            {
                _loops[i].IsActive = false;
                if (_loops[i].Driver != null) _loops[i].Driver.IsActive = false;
                _loops[i].Driver = null;
            }
            for (int i = 0; i < _groups.Count; i++) _groups[i].IsActive = false;
            for (int i = 0; i < _scopes.Count; i++) _scopes[i].IsActive = false;
            foreach (var slot in _slots.Values)
                if (slot.Entry != null) slot.Entry.IsActive = false;
            _registrations.Clear();
            _slots.Clear();
            _freeSlots.Clear();
            _activeRegistrationCount = 0;
        }

        internal bool IsHandleValid(UpdateHandle handle)
        {
            return !_disposed && ReferenceEquals(handle.Manager, this) && GetEntry(handle) != null;
        }

        internal void ReleaseDriver(DriverState driver)
        {
            if (driver == null || !ReferenceEquals(driver.Manager, this) || !driver.IsActive) return;
            driver.IsActive = false;
            if (ReferenceEquals(driver.LoopState.Driver, driver)) driver.LoopState.Driver = null;
        }

        internal void ReleasePause(PauseState pause)
        {
            if (pause == null || !ReferenceEquals(pause.Manager, this) || !pause.IsActive) return;
            pause.IsActive = false;
            if (_disposed) return;
            if (pause.IsGlobal) _globalPauseCount = Math.Max(0, _globalPauseCount - 1);
            else if (pause.Group != null) pause.Group.PauseCount = Math.Max(0, pause.Group.PauseCount - 1);
        }

        private sealed class ResolvedOptions
        {
            internal LoopState LoopState;
            internal UpdateLoop Loop;
            internal UpdatePhase Phase;
            internal GroupState GroupState;
            internal ScopeState ScopeState;
            internal int Priority;
            internal UpdateSchedule Schedule;
            internal UpdateWorkClass WorkClass;
            internal bool Enabled;
            internal double MaxCallbackDeltaSeconds;
        }

        private readonly struct ResolvedRequest
        {
            internal readonly IFrameUpdate Target;
            internal readonly IUpdateLifetime Lifetime;
            internal readonly ResolvedOptions Options;
            internal ResolvedRequest(IFrameUpdate target, IUpdateLifetime lifetime, ResolvedOptions options)
            { Target = target; Lifetime = lifetime; Options = options; }
        }

        private ResolvedOptions ResolveOptions(FrameUpdateOptions options)
        {
            var loop = ResolveLoop(options.Loop);
            var phase = options.Phase ?? _config.DefaultPhase;
            ValidatePhase(phase);
            if (loop.State.Driver != null && loop.State.Driver.IsActive && !HasPhase(loop.State.Driver.Phases, phase))
                throw new ArgumentException("The active driver does not declare the selected phase.", nameof(options));
            var group = ResolveGroup(options.Group);
            var scope = ResolveScope(options.Scope);
            var schedule = options.Schedule ?? _config.DefaultSchedule;
            schedule.Validate();
            var workClass = options.WorkClass ?? _config.DefaultWorkClass;
            if (!Enum.IsDefined(typeof(UpdateWorkClass), workClass)) throw new ArgumentOutOfRangeException(nameof(options));
            var maxDelta = options.MaxCallbackDeltaSeconds ?? _config.MaxCallbackDeltaSeconds;
            ValidateNonNegativeFinite(maxDelta, nameof(options.MaxCallbackDeltaSeconds));
            return new ResolvedOptions
            {
                LoopState = loop.State,
                Loop = loop,
                Phase = phase,
                GroupState = group.State,
                ScopeState = scope.State,
                Priority = options.Priority ?? _config.DefaultPriority,
                Schedule = schedule,
                WorkClass = workClass,
                Enabled = options.Enabled ?? true,
                MaxCallbackDeltaSeconds = maxDelta
            };
        }

        private Entry CreateEntry(RegistrationKey key, IFrameUpdate target, IUpdateLifetime lifetime, ResolvedOptions options)
        {
            EntrySlot slot;
            if (_freeSlots.Count > 0)
            {
                slot = _slots[_freeSlots.Pop()];
                if (slot.Retired) return CreateEntry(key, target, lifetime, options);
            }
            else
            {
                slot = new EntrySlot(_nextSlotId++);
                _slots.Add(slot.Id, slot);
            }
            var entry = new Entry(this, slot, ++_registrationOrder, target, lifetime,
                options.LoopState, options.Loop, options.Phase, options.GroupState,
                options.ScopeState, options.Priority, options.Schedule, options.WorkClass,
                options.Enabled, options.MaxCallbackDeltaSeconds);
            slot.Entry = entry;
            _registrations.Add(key, entry);
            options.LoopState.Entries[PhaseIndex(options.Phase)].Add(entry);
            options.LoopState.Dirty[PhaseIndex(options.Phase)] = true;
            _activeRegistrationCount++;
            return entry;
        }

        private static UpdateHandle MakeHandle(Entry entry)
        { return new UpdateHandle(entry.Manager, entry.Slot, entry.Generation); }

        private static bool SameRegistration(Entry entry, ResolvedOptions options, IUpdateLifetime lifetime)
        {
            return ReferenceEquals(entry.Lifetime, lifetime) && ReferenceEquals(entry.LoopState, options.LoopState) &&
                entry.Phase == options.Phase && entry.Priority == options.Priority &&
                entry.Schedule == options.Schedule && entry.WorkClass == options.WorkClass &&
                entry.Enabled == options.Enabled && ReferenceEquals(entry.GroupState, options.GroupState) &&
                ReferenceEquals(entry.ScopeState, options.ScopeState) &&
                entry.MaxCallbackDeltaSeconds == options.MaxCallbackDeltaSeconds;
        }

        private static bool SameRegistration(ResolvedRequest left, ResolvedRequest right)
        {
            return ReferenceEquals(left.Target, right.Target) && ReferenceEquals(left.Lifetime, right.Lifetime) &&
                SameOptions(left.Options, right.Options);
        }

        private static bool SameOptions(ResolvedOptions left, ResolvedOptions right)
        {
            return ReferenceEquals(left.LoopState, right.LoopState) && left.Phase == right.Phase &&
                ReferenceEquals(left.GroupState, right.GroupState) && ReferenceEquals(left.ScopeState, right.ScopeState) &&
                left.Priority == right.Priority && left.Schedule == right.Schedule &&
                left.WorkClass == right.WorkClass && left.Enabled == right.Enabled &&
                left.MaxCallbackDeltaSeconds == right.MaxCallbackDeltaSeconds;
        }

        private Entry GetEntry(UpdateHandle handle)
        {
            if (!ReferenceEquals(handle.Manager, this) || handle.Slot <= 0) return null;
            if (!_slots.TryGetValue(handle.Slot, out var slot) || slot.Generation != handle.Generation) return null;
            return slot.Entry != null && slot.Entry.IsActive ? slot.Entry : null;
        }

        private void RemoveEntry(Entry entry)
        {
            if (entry == null || !entry.IsActive) return;
            entry.IsActive = false;
            _registrations.Remove(new RegistrationKey(entry.Target, entry.LoopState, entry.Phase));
            if (_slots.TryGetValue(entry.Slot, out var slot) && ReferenceEquals(slot.Entry, entry))
            {
                slot.Entry = null;
                if (slot.Generation == int.MaxValue) slot.Retired = true;
                else { slot.Generation++; _freeSlots.Push(slot.Id); }
            }
            _activeRegistrationCount = Math.Max(0, _activeRegistrationCount - 1);
        }

        private void RunPhase(DriverState driver, UpdatePhase phase, in FrameTimeSample time)
        {
            _inTick = true;
            driver.LoopState.IsTicking = true;
            long start = Stopwatch.GetTimestamp();
            try
            {
                var list = GetStableEntries(driver.LoopState, phase);
                var snapshot = _tickSnapshot;
                var candidates = _tickCandidates;
                snapshot.Clear();
                candidates.Clear();
                for (int i = 0; i < list.Count; i++) if (list[i].IsActive) snapshot.Add(list[i]);
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var entry = snapshot[i];
                    if (!entry.IsActive) continue;
                    if (!entry.Enabled) { entry.DisabledSkipCount++; continue; }
                    if (IsPaused(entry.GroupState)) { entry.PausedSkipCount++; continue; }
                    double scale = GetEffectiveScale(entry.GroupState);
                    if (scale <= 0) { entry.PausedSkipCount++; continue; }
                    if (!CheckLifetime(entry, driver, phase, time.Sequence)) continue;
                    double sourceDelta = entry.GroupState.RootTimeSource == UpdateTimeSource.Scaled
                        ? time.ScaledDeltaTime : time.UnscaledDeltaTime;
                    double delta = sourceDelta * scale;
                    if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0)
                        throw new InvalidOperationException("The effective group delta is not finite and non-negative.");
                    entry.ScheduleClock += delta;
                    entry.AccumulatedTime += delta;
                    bool due;
                    switch (entry.Schedule.Kind)
                    {
                        case UpdateScheduleKind.EveryStep:
                            due = true;
                            break;
                        case UpdateScheduleKind.EveryNFrames:
                            entry.FrameCounter++;
                            due = entry.FrameCounter >= entry.NextFrame;
                            break;
                        default:
                            due = entry.ScheduleClock + TimeEpsilon >= entry.NextDeadline;
                            break;
                    }
                    if (due)
                        candidates.Add(new EntryCandidate(entry, entry.GroupState, entry.Schedule,
                            entry.ScheduleVersion, entry.AccumulatedTime, true));
                    else
                        entry.FrequencySkipCount++;
                }

                double budget = driver.LoopState.BudgetOverrides[PhaseIndex(phase)]
                    ? driver.LoopState.BudgetMilliseconds[PhaseIndex(phase)]
                    : _config.LoopPhaseBudgetMilliseconds;
                long budgetStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    var entry = candidate.Entry;
                    if (!entry.IsActive) continue;
                    if (!driver.IsActive || _disposed) break;
                    if (!entry.Enabled) { entry.DisabledSkipCount++; continue; }
                    if (IsPaused(entry.GroupState) || GetEffectiveScale(entry.GroupState) <= 0)
                    { entry.PausedSkipCount++; continue; }
                    if (entry.WorkClass == UpdateWorkClass.Deferrable && budget > 0 &&
                        ElapsedMilliseconds(budgetStart) >= budget)
                    {
                        entry.DeferredCount++;
                        entry.ConsecutiveDeferredCount++;
                        driver.LoopState.DeferredCount++;
                        _totalDeferred++;
                        continue;
                    }

                    ConsumeCandidate(entry, candidate);
                    double raw = candidate.RawDelta;
                    double delivered = entry.MaxCallbackDeltaSeconds > 0
                        ? Math.Min(raw, entry.MaxCallbackDeltaSeconds) : raw;
                    double discarded = raw - delivered;
                    entry.ElapsedTime += delivered;
                    entry.LastDeltaTime = delivered;
                    entry.ExecutionCount++;
                    entry.LastExecutionTimestamp = Stopwatch.GetTimestamp();
                    entry.ConsecutiveDeferredCount = 0;
                    driver.LoopState.ExecutedCount++;
                    _totalExecuted++;
                    var context = new FrameUpdateContext(MakeHandle(entry), entry.Loop,
                        new UpdateGroup(this, candidate.Group), phase, time.Sequence,
                        delivered, raw, entry.ElapsedTime, discarded);
                    try
                    {
                        entry.Target.OnFrameUpdate(in context);
                    }
                    catch (Exception error)
                    {
                        entry.FaultedCount++;
                        driver.LoopState.FaultedCount++;
                        _totalFaulted++;
                        RecordError(entry, phase, time.Sequence, error);
                        if (_config.ExceptionPolicy == ExceptionPolicy.Propagate) throw;
                        entry.Enabled = false;
                    }
                }
            }
            finally
            {
                _inTick = false;
                driver.LoopState.IsTicking = false;
                _tickSnapshot.Clear();
                _tickCandidates.Clear();
                long elapsed = Stopwatch.GetTimestamp() - start;
                driver.LoopState.TotalCpuMilliseconds += elapsed * 1000.0 / Stopwatch.Frequency;
            }
        }

        private bool CheckLifetime(Entry entry, DriverState driver, UpdatePhase phase, long sequence)
        {
            if (entry.Lifetime == null) return true;
            bool alive;
            try { alive = entry.Lifetime.IsAlive; }
            catch (Exception error)
            {
                entry.FaultedCount++;
                driver.LoopState.FaultedCount++;
                _totalFaulted++;
                RecordError(entry, phase, sequence, error);
                if (_config.ExceptionPolicy == ExceptionPolicy.Propagate) throw;
                RemoveEntry(entry);
                return false;
            }
            if (!alive)
            {
                entry.LifetimeSkipCount++;
                RemoveEntry(entry);
                return false;
            }
            return true;
        }

        private void ConsumeCandidate(Entry entry, EntryCandidate candidate)
        {
            if (entry.ScheduleVersion == candidate.ScheduleVersion)
            {
                entry.AccumulatedTime = 0;
                if (candidate.Schedule.Kind == UpdateScheduleKind.AtFps || candidate.Schedule.Kind == UpdateScheduleKind.AtInterval)
                {
                    do { entry.NextDeadline += candidate.Schedule.PeriodSeconds; }
                    while (entry.NextDeadline <= entry.ScheduleClock + TimeEpsilon);
                }
                else if (candidate.Schedule.Kind == UpdateScheduleKind.EveryNFrames)
                {
                    do { entry.NextFrame += candidate.Schedule.FrameCount; }
                    while (entry.NextFrame <= entry.FrameCounter);
                }
            }
            else
            {
                // A callback earlier in this phase changed the plan. The changed plan
                // already chose its next deadline; consume only this old candidate.
                entry.AccumulatedTime = 0;
            }
        }

        private void CompleteDriverPhase(DriverState driver)
        {
            driver.ExpectedPhaseIndex++;
            if (driver.ExpectedPhaseIndex < driver.OrderedPhases.Length) return;
            driver.ExpectedPhaseIndex = 0;
            driver.HasSample = false;
            driver.ExpectedSequence = checked(driver.ExpectedSequence + 1);
            var loop = driver.LoopState;
            loop.CompletedTicks++;
            _totalTicks++;
            long now = Stopwatch.GetTimestamp();
            if (loop.LastCompletedTimestamp != 0)
            {
                double seconds = (now - loop.LastCompletedTimestamp) / (double)Stopwatch.Frequency;
                loop.LastTickHz = seconds > 0 ? 1.0 / seconds : 0;
            }
            loop.LastCompletedTimestamp = now;
        }

        private List<Entry> GetStableEntries(LoopState loop, UpdatePhase phase)
        {
            int index = PhaseIndex(phase);
            var list = loop.Entries[index];
            for (int i = list.Count - 1; i >= 0; i--)
                if (!list[i].IsActive) list.RemoveAt(i);
            if (loop.Dirty[index])
            {
                list.Sort(CompareEntries);
                loop.Dirty[index] = false;
            }
            return list;
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : left.RegistrationOrder.CompareTo(right.RegistrationOrder);
        }

        private FrameRegistrationInfo MakeRegistrationInfo(Entry entry)
        {
            bool alive = entry.Lifetime == null || SafeIsAlive(entry.Lifetime);
            bool paused = IsPaused(entry.GroupState);
            double remaining = 0;
            if (entry.Schedule.Kind == UpdateScheduleKind.AtFps || entry.Schedule.Kind == UpdateScheduleKind.AtInterval)
                remaining = Math.Max(0, entry.NextDeadline - entry.ScheduleClock);
            else if (entry.Schedule.Kind == UpdateScheduleKind.EveryNFrames)
                remaining = Math.Max(0, entry.NextFrame - entry.FrameCounter);
            return new FrameRegistrationInfo(MakeHandle(entry), entry.Target, entry.Loop,
                entry.Phase, new UpdateGroup(this, entry.GroupState), new UpdateScope(this, entry.ScopeState),
                entry.Priority, entry.Schedule, entry.WorkClass, entry.Enabled, alive, paused,
                entry.AccumulatedTime, remaining, entry.ElapsedTime, entry.ExecutionCount,
                entry.DeferredCount, entry.FaultedCount, entry.MaxCallbackDeltaSeconds);
        }

        private UpdateEntryStats MakeEntryStats(Entry entry)
        {
            return new UpdateEntryStats(MakeHandle(entry), entry.Loop, entry.Phase,
                new UpdateGroup(this, entry.GroupState), entry.Priority, entry.Enabled,
                entry.ExecutionCount, entry.DeferredCount, entry.FaultedCount,
                entry.ConsecutiveDeferredCount, entry.LastDeltaTime, entry.ElapsedTime,
                entry.AccumulatedTime);
        }

        private void RecordError(Entry entry, UpdatePhase phase, long sequence, Exception error)
        {
            var item = new FrameUpdateError(++_errorSequence, MakeHandle(entry), entry.Loop,
                phase, sequence, error);
            if (_errors.Count == _config.ErrorBufferCapacity) _errors.RemoveAt(0);
            _errors.Add(item);
        }

        private static bool SafeIsAlive(IUpdateLifetime lifetime)
        {
            try { return lifetime.IsAlive; }
            catch { return false; }
        }

        private void CollectScopeTree(ScopeState state, List<ScopeState> target)
        {
            target.Add(state);
            for (int i = 0; i < state.Children.Count; i++) CollectScopeTree(state.Children[i], target);
        }

        private static bool ContainsScope(List<ScopeState> scopes, ScopeState state)
        {
            for (int i = 0; i < scopes.Count; i++) if (ReferenceEquals(scopes[i], state)) return true;
            return false;
        }

        private static bool IsScopeDescendant(ScopeState value, ScopeState ancestor)
        {
            for (var current = value; current != null; current = current.Parent)
                if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        private static bool IsGroupWithin(GroupState value, GroupState ancestor)
        {
            for (var current = value; current != null; current = current.Parent)
                if (ReferenceEquals(current, ancestor)) return true;
            return false;
        }

        private bool IsPaused(GroupState group)
        {
            if (_globalPauseCount > 0) return true;
            for (var current = group; current != null; current = current.Parent)
                if (current.PauseCount > 0) return true;
            return false;
        }

        private static double GetEffectiveScale(GroupState group)
        {
            double result = 1;
            for (var current = group; current != null; current = current.Parent) result *= current.TimeScale;
            return result;
        }

        private ScopeState ResolveOwner(UpdateScope owner)
        { return owner.IsDefault ? DefaultScope.State : GetRequiredScope(owner); }

        private UpdateLoop ResolveLoop(UpdateLoop? value)
        {
            if (!value.HasValue || value.Value.IsDefault) return DefaultLoop;
            if (!TryGetLoop(value.Value, out var state)) throw new ArgumentException("The loop is invalid.", nameof(value));
            return new UpdateLoop(this, state);
        }

        private UpdateGroup ResolveGroup(UpdateGroup? value)
        {
            if (!value.HasValue || value.Value.IsDefault) return DefaultGroup;
            if (!TryGetGroup(value.Value, out var state)) throw new ArgumentException("The group is invalid.", nameof(value));
            return new UpdateGroup(this, state);
        }

        private UpdateScope ResolveScope(UpdateScope? value)
        {
            if (!value.HasValue || value.Value.IsDefault) return DefaultScope;
            if (!TryGetScope(value.Value, out var state)) throw new ArgumentException("The scope is invalid.", nameof(value));
            return new UpdateScope(this, state);
        }

        private ScopeState GetRequiredScope(UpdateScope scope)
        {
            if (!TryGetScope(scope, out var state)) throw new ArgumentException("The scope is invalid.", nameof(scope));
            return state;
        }

        private bool TryGetLoop(UpdateLoop value, out LoopState state)
        { state = value.State; return ReferenceEquals(value.Manager, this) && state != null && state.IsActive; }
        private bool TryGetGroup(UpdateGroup value, out GroupState state)
        { state = value.State; return ReferenceEquals(value.Manager, this) && state != null && state.IsActive; }
        private bool TryGetScope(UpdateScope value, out ScopeState state)
        { state = value.State; return ReferenceEquals(value.Manager, this) && state != null && state.IsActive; }

        private void EnsureUsable()
        { if (_disposed) throw new ObjectDisposedException(nameof(FrameUpdateManager)); }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
                throw new InvalidOperationException("FrameUpdateManager must be used from its owning thread.");
        }

        private static void ValidateName(string name, string parameter)
        { if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A diagnostic name is required.", parameter); }

        private static void ValidatePhase(UpdatePhase phase)
        { if (!Enum.IsDefined(typeof(UpdatePhase), phase)) throw new ArgumentOutOfRangeException(nameof(phase)); }

        private static void ValidatePhaseMask(UpdatePhaseMask mask)
        {
            if (mask == 0 || (((int)mask) & ~((int)UpdatePhaseMask.All)) != 0)
                throw new ArgumentOutOfRangeException(nameof(mask));
        }

        private static void ValidateNonNegativeFinite(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }

        private static int PhaseIndex(UpdatePhase phase) { return (int)phase; }
        private static bool HasPhase(UpdatePhaseMask mask, UpdatePhase phase)
        { return (mask & (UpdatePhaseMask)(1 << PhaseIndex(phase))) != 0; }

        private static UpdatePhase[] GetOrderedPhases(UpdatePhaseMask mask)
        {
            var result = new List<UpdatePhase>(3);
            if ((mask & UpdatePhaseMask.Early) != 0) result.Add(UpdatePhase.EarlyUpdate);
            if ((mask & UpdatePhaseMask.Normal) != 0) result.Add(UpdatePhase.NormalUpdate);
            if ((mask & UpdatePhaseMask.Late) != 0) result.Add(UpdatePhase.LateUpdate);
            return result.ToArray();
        }

        private static void ValidateTime(in FrameTimeSample time)
        {
            if (time.Sequence <= 0) throw new ArgumentOutOfRangeException(nameof(time));
            ValidateTimeValue(time.ScaledDeltaTime, nameof(time));
            ValidateTimeValue(time.UnscaledDeltaTime, nameof(time));
        }

        private static void ValidateTimeValue(double value, string name)
        { if (double.IsNaN(value) || double.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(name); }

        private static bool SameSample(FrameTimeSample left, FrameTimeSample right)
        {
            return left.Sequence == right.Sequence && left.ScaledDeltaTime == right.ScaledDeltaTime &&
                left.UnscaledDeltaTime == right.UnscaledDeltaTime && left.RenderFrameId == right.RenderFrameId;
        }

        private static double ElapsedMilliseconds(long start)
        { return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency; }

    }
}
