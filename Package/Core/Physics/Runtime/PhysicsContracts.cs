using System;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.Physics
{
    public readonly struct PhysicsStepContext
    {
        public long StepSequence { get; }
        public long? RenderFrameId { get; }
        public double DeltaTime { get; }
        public double RawDeltaTime { get; }
        public bool IsFirstStep { get; }
        public UpdatePhase Phase { get; }

        internal PhysicsStepContext(in FrameUpdateContext frame, bool isFirstStep)
        {
            StepSequence = frame.Sequence;
            RenderFrameId = null;
            DeltaTime = frame.DeltaTime;
            RawDeltaTime = frame.RawDeltaTime;
            IsFirstStep = isFirstStep;
            Phase = frame.Phase;
        }
    }

    public interface IPhysicsFixedStepParticipant
    {
        void OnPhysicsStep(in PhysicsStepContext context);
    }

    public sealed class PhysicsParticipantHandle : IDisposable
    {
        private readonly PhysicsStepDispatcher _dispatcher;
        private readonly UpdateHandle _updateHandle;
        private bool _disposed;

        internal PhysicsParticipantHandle(PhysicsStepDispatcher dispatcher, UpdateHandle updateHandle)
        { _dispatcher = dispatcher; _updateHandle = updateHandle; }

        public UpdateHandle UpdateHandle => _updateHandle;
        public bool IsActive => !_disposed && _updateHandle.IsValid;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _dispatcher.Remove(_updateHandle);
        }
    }

    /// <summary>
    /// Registers business fixed-step work into the caller's existing FrameUpdate Physics loop.
    /// It never calls Physics.Simulate and never owns a hidden driver.
    /// </summary>
    public sealed class PhysicsStepDispatcher : IDisposable
    {
        private sealed class ParticipantAdapter : IFrameUpdate
        {
            internal readonly IPhysicsFixedStepParticipant Participant;
            internal bool FirstStep = true;
            internal ParticipantAdapter(IPhysicsFixedStepParticipant participant) { Participant = participant; }
            public void OnFrameUpdate(in FrameUpdateContext context)
            {
                var step = new PhysicsStepContext(context, FirstStep);
                FirstStep = false;
                Participant.OnPhysicsStep(in step);
            }
        }

        private readonly FrameUpdateManager _manager;
        private readonly UpdateLoop _loop;
        private readonly UpdateGroup _group;
        private readonly UpdateScope _scope;
        private readonly System.Collections.Generic.Dictionary<IPhysicsFixedStepParticipant, PhysicsParticipantHandle> _handles =
            new System.Collections.Generic.Dictionary<IPhysicsFixedStepParticipant, PhysicsParticipantHandle>(ReferenceComparer.Instance);
        private bool _disposed;

        public PhysicsStepDispatcher(FrameUpdateManager manager, UpdateLoop physicsLoop,
            UpdateGroup group = default(UpdateGroup), UpdateScope scope = default(UpdateScope))
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (!physicsLoop.IsValid) throw new ArgumentException("A valid Physics loop is required.", nameof(physicsLoop));
            _loop = physicsLoop;
            _group = group;
            _scope = scope;
        }

        public int ParticipantCount => _handles.Count;

        public PhysicsParticipantHandle Register(IPhysicsFixedStepParticipant participant,
            UpdatePhase phase = UpdatePhase.NormalUpdate, int priority = UpdatePriority.Normal,
            UpdateWorkClass workClass = UpdateWorkClass.Required)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PhysicsStepDispatcher));
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (_handles.ContainsKey(participant)) throw new InvalidOperationException("The participant is already registered.");
            var adapter = new ParticipantAdapter(participant);
            var updateHandle = _manager.Register(adapter, new FrameUpdateOptions
            {
                Loop = _loop,
                Phase = phase,
                Group = _group,
                Scope = _scope,
                Priority = priority,
                Schedule = UpdateSchedule.EveryStep(),
                WorkClass = workClass,
                Enabled = true
            });
            var result = new PhysicsParticipantHandle(this, updateHandle);
            _handles.Add(participant, result);
            return result;
        }

        internal void Remove(UpdateHandle handle)
        {
            if (_disposed || _manager.IsDisposed) return;
            _manager.Unregister(handle);
            IPhysicsFixedStepParticipant remove = null;
            foreach (var pair in _handles)
                if (pair.Value.UpdateHandle == handle) { remove = pair.Key; break; }
            if (remove != null) _handles.Remove(remove);
        }

        public void Dispose()
        {
            if (_disposed) return;
            var values = new PhysicsParticipantHandle[_handles.Count];
            _handles.Values.CopyTo(values, 0);
            _handles.Clear();
            _disposed = true;
            for (int i = 0; i < values.Length; i++)
                if (!_manager.IsDisposed) _manager.Unregister(values[i].UpdateHandle);
        }

        private sealed class ReferenceComparer : System.Collections.Generic.IEqualityComparer<IPhysicsFixedStepParticipant>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(IPhysicsFixedStepParticipant x, IPhysicsFixedStepParticipant y) => ReferenceEquals(x, y);
            public int GetHashCode(IPhysicsFixedStepParticipant obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
