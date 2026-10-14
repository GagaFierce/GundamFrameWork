using System;

namespace WFrameWork.Core.FrameUpdate
{
    public readonly struct UpdateLoop : IEquatable<UpdateLoop>
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly LoopState State;

        internal UpdateLoop(FrameUpdateManager manager, LoopState state)
        { Manager = manager; State = state; }

        public string Name { get { return State == null ? null : State.Name; } }
        public bool IsValid { get { return State != null && State.IsActive && Manager != null && !Manager.IsDisposed; } }
        internal bool IsDefault { get { return Manager == null && State == null; } }

        public bool Equals(UpdateLoop other) { return ReferenceEquals(Manager, other.Manager) && ReferenceEquals(State, other.State); }
        public override bool Equals(object obj) { return obj is UpdateLoop && Equals((UpdateLoop)obj); }
        public override int GetHashCode() { return State == null ? 0 : State.GetHashCode(); }
        public static bool operator ==(UpdateLoop left, UpdateLoop right) { return left.Equals(right); }
        public static bool operator !=(UpdateLoop left, UpdateLoop right) { return !left.Equals(right); }
        public override string ToString() { return IsValid ? Name : "<invalid loop>"; }
    }

    public sealed class LoopDriverHandle : IDisposable
    {
        private readonly DriverState _state;

        internal LoopDriverHandle(DriverState state) { _state = state; }
        public UpdateLoop Loop { get { return _state == null ? default(UpdateLoop) : _state.Loop; } }
        public string Name { get { return _state == null ? null : _state.Name; } }
        public UpdatePhaseMask Phases { get { return _state == null ? 0 : _state.Phases; } }
        public bool IsValid { get { return _state != null && _state.IsActive && _state.Manager != null && !_state.Manager.IsDisposed; } }
        internal DriverState State { get { return _state; } }
        public void Dispose() { _state?.Manager.ReleaseDriver(_state); }
    }

    public readonly struct UpdateGroup : IEquatable<UpdateGroup>
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly GroupState State;

        internal UpdateGroup(FrameUpdateManager manager, GroupState state)
        { Manager = manager; State = state; }

        public string Name { get { return State == null ? null : State.Name; } }
        public bool IsValid { get { return State != null && State.IsActive && Manager != null && !Manager.IsDisposed; } }
        internal bool IsDefault { get { return Manager == null && State == null; } }
        public bool Equals(UpdateGroup other) { return ReferenceEquals(Manager, other.Manager) && ReferenceEquals(State, other.State); }
        public override bool Equals(object obj) { return obj is UpdateGroup && Equals((UpdateGroup)obj); }
        public override int GetHashCode() { return State == null ? 0 : State.GetHashCode(); }
        public static bool operator ==(UpdateGroup left, UpdateGroup right) { return left.Equals(right); }
        public static bool operator !=(UpdateGroup left, UpdateGroup right) { return !left.Equals(right); }
        public override string ToString() { return IsValid ? Name : "<invalid group>"; }
    }

    public readonly struct UpdateScope : IEquatable<UpdateScope>
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly ScopeState State;

        internal UpdateScope(FrameUpdateManager manager, ScopeState state)
        { Manager = manager; State = state; }

        public string Name { get { return State == null ? null : State.Name; } }
        public bool IsValid { get { return State != null && State.IsActive && Manager != null && !Manager.IsDisposed; } }
        internal bool IsDefault { get { return Manager == null && State == null; } }
        public bool Equals(UpdateScope other) { return ReferenceEquals(Manager, other.Manager) && ReferenceEquals(State, other.State); }
        public override bool Equals(object obj) { return obj is UpdateScope && Equals((UpdateScope)obj); }
        public override int GetHashCode() { return State == null ? 0 : State.GetHashCode(); }
        public static bool operator ==(UpdateScope left, UpdateScope right) { return left.Equals(right); }
        public static bool operator !=(UpdateScope left, UpdateScope right) { return !left.Equals(right); }
        public override string ToString() { return IsValid ? Name : "<invalid scope>"; }
    }

    public readonly struct UpdateHandle : IEquatable<UpdateHandle>
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly int Slot;
        internal readonly int Generation;

        internal UpdateHandle(FrameUpdateManager manager, int slot, int generation)
        { Manager = manager; Slot = slot; Generation = generation; }

        public bool IsValid { get { return Manager != null && Manager.IsHandleValid(this); } }
        public bool Equals(UpdateHandle other)
        { return ReferenceEquals(Manager, other.Manager) && Slot == other.Slot && Generation == other.Generation; }
        public override bool Equals(object obj) { return obj is UpdateHandle && Equals((UpdateHandle)obj); }
        public override int GetHashCode() { unchecked { return (Manager == null ? 0 : Manager.GetHashCode()) * 397 ^ Slot * 31 ^ Generation; } }
        public static bool operator ==(UpdateHandle left, UpdateHandle right) { return left.Equals(right); }
        public static bool operator !=(UpdateHandle left, UpdateHandle right) { return !left.Equals(right); }
        public override string ToString() { return IsValid ? "UpdateHandle(" + Slot + ":" + Generation + ")" : "<invalid update handle>"; }
    }

    public readonly struct PauseHandle : IDisposable
    {
        private readonly PauseState _state;

        internal PauseHandle(PauseState state) { _state = state; }
        public bool IsValid { get { return _state != null && _state.IsActive && _state.Manager != null && !_state.Manager.IsDisposed; } }
        public void Dispose() { _state?.Manager.ReleasePause(_state); }
    }
}
