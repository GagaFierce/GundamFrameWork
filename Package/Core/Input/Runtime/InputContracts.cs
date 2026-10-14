using System;
using WFrameWork.Diagnostics;

namespace WFrameWork.Input
{
    public enum InputActionType
    {
        Button,
        Axis1D,
        Axis2D
    }

    public enum InputButtonPhase
    {
        None,
        Pressed,
        Held,
        Released,
        Canceled
    }

    public enum InputFixedEventPhase
    {
        Pressed,
        Released,
        Canceled
    }

    /// <summary>Stable logical identity. Device keys and Unity KeyCodes never cross this boundary.</summary>
    public readonly struct InputActionId : IEquatable<InputActionId>
    {
        public string Value { get; }

        public InputActionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Action id is required.", nameof(value));
            Value = value.Trim();
        }

        public bool Equals(InputActionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is InputActionId && Equals((InputActionId)obj);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(InputActionId left, InputActionId right) => left.Equals(right);
        public static bool operator !=(InputActionId left, InputActionId right) => !left.Equals(right);
        public override string ToString() => Value ?? "<invalid action>";
    }

    public readonly struct InputValue : IEquatable<InputValue>
    {
        public float X { get; }
        public float Y { get; }

        public InputValue(float x, float y = 0)
        {
            X = x;
            Y = y;
        }

        public static InputValue Zero => new InputValue(0, 0);
        public bool Equals(InputValue other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is InputValue && Equals((InputValue)obj);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => "(" + X + ", " + Y + ")";
    }

    public sealed class InputActionDefinition : IEquatable<InputActionDefinition>
    {
        public InputActionId Id { get; }
        public InputActionType Type { get; }
        public string ContextId { get; }
        public float DeadZone { get; }
        public bool InvertX { get; }
        public bool InvertY { get; }
        public float Scale { get; }

        public InputActionDefinition(InputActionId id, InputActionType type,
            string contextId = "Gameplay", float deadZone = 0,
            bool invertX = false, bool invertY = false, float scale = 1)
        {
            if (id.Value == null) throw new ArgumentException("Action id is required.", nameof(id));
            if (!Enum.IsDefined(typeof(InputActionType), type)) throw new ArgumentOutOfRangeException(nameof(type));
            if (deadZone < 0 || deadZone >= 1 || float.IsNaN(deadZone) || float.IsInfinity(deadZone))
                throw new ArgumentOutOfRangeException(nameof(deadZone), "Dead zone must be finite and in [0, 1).");
            if (float.IsNaN(scale) || float.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
            if (string.IsNullOrWhiteSpace(contextId)) throw new ArgumentException("Context id is required.", nameof(contextId));
            Id = id;
            Type = type;
            ContextId = contextId.Trim();
            DeadZone = deadZone;
            InvertX = invertX;
            InvertY = invertY;
            Scale = scale;
        }

        public bool Equals(InputActionDefinition other)
        {
            if (ReferenceEquals(other, null)) return false;
            return Id == other.Id && Type == other.Type &&
                string.Equals(ContextId, other.ContextId, StringComparison.Ordinal) &&
                DeadZone.Equals(other.DeadZone) && InvertX == other.InvertX &&
                InvertY == other.InvertY && Scale.Equals(other.Scale);
        }

        public override bool Equals(object obj) => Equals(obj as InputActionDefinition);
        public override int GetHashCode() => Id.GetHashCode();
    }

    public readonly struct InputActionState
    {
        public InputActionId Id { get; }
        public InputActionType Type { get; }
        public InputValue Value { get; }
        public InputButtonPhase ButtonPhase { get; }
        public bool IsPressed => ButtonPhase == InputButtonPhase.Pressed;
        public bool IsHeld => ButtonPhase == InputButtonPhase.Pressed || ButtonPhase == InputButtonPhase.Held;
        public bool IsReleased => ButtonPhase == InputButtonPhase.Released;
        public bool IsCanceled => ButtonPhase == InputButtonPhase.Canceled;

        internal InputActionState(InputActionId id, InputActionType type, InputValue value, InputButtonPhase phase)
        {
            Id = id;
            Type = type;
            Value = value;
            ButtonPhase = phase;
        }
    }

    public readonly struct InputActionEvent
    {
        public long Serial { get; }
        public long RenderFrameId { get; }
        public InputActionId ActionId { get; }
        public InputActionType ActionType { get; }
        public InputFixedEventPhase Phase { get; }
        public InputValue Value { get; }

        internal InputActionEvent(long serial, long renderFrameId, InputActionId actionId,
            InputActionType actionType, InputFixedEventPhase phase, InputValue value)
        {
            Serial = serial;
            RenderFrameId = renderFrameId;
            ActionId = actionId;
            ActionType = actionType;
            Phase = phase;
            Value = value;
        }
    }

    public sealed class InputServiceConfig
    {
        public int FixedEventCapacity { get; set; } = 128;
        public long FixedEventLifetimeFrames { get; set; } = 8;
        public IDiagnosticSink Diagnostics { get; set; }

        internal void Validate()
        {
            if (FixedEventCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(FixedEventCapacity));
            if (FixedEventLifetimeFrames < 0) throw new ArgumentOutOfRangeException(nameof(FixedEventLifetimeFrames));
        }
    }

    public interface IInputBackend : IDisposable
    {
        void Sample(InputSampleWriter writer);
        void Reset();
    }

    public interface IInputContextBlocker
    {
        bool IsContextBlocked(string contextId);
    }
}
