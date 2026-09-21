using System;
using System.Collections.Generic;
using WFrameWork.Core.FrameUpdate;

namespace WFrameWork.FrameUpdate.Tests
{
    internal sealed class TestCase
    {
        internal readonly string Name;
        internal readonly Action Body;
        internal TestCase(string name, Action body) { Name = name; Body = body; }
    }

    internal static class Assert
    {
        internal static void True(bool value, string message = "Expected true")
        { if (!value) throw new Exception(message); }
        internal static void False(bool value, string message = "Expected false") { True(!value, message); }
        internal static void Equal<T>(T expected, T actual, string message = null)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception((message ?? "Values differ") + $": expected {expected}; actual {actual}");
        }
        internal static void Near(double expected, double actual, double tolerance = 1e-9)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new Exception($"Expected {expected:R} +/- {tolerance:R}; actual {actual:R}");
        }
        internal static T Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T error) { return error; }
            catch (Exception error) { throw new Exception($"Expected {typeof(T).Name}, actual {error.GetType().Name}", error); }
            throw new Exception("Expected " + typeof(T).Name + ", but no exception was thrown");
        }
    }

    internal class Probe : IFrameUpdate
    {
        internal int Calls;
        internal double Total;
        internal FrameUpdateContext Last;
        internal Action<FrameUpdateContext> Action;
        public void OnFrameUpdate(in FrameUpdateContext context)
        {
            Calls++;
            Last = context;
            Total += context.DeltaTime;
            Action?.Invoke(context);
        }
    }

    internal sealed class EqualProbe : Probe
    {
        public override bool Equals(object obj) { return obj is EqualProbe; }
        public override int GetHashCode() { return 42; }
    }

    internal sealed class Lifetime : IUpdateLifetime
    {
        internal bool Alive = true;
        internal bool Throw;
        public bool IsAlive { get { if (Throw) throw new InvalidOperationException("lifetime failure"); return Alive; } }
    }

    internal struct ValueProbe : IFrameUpdate
    {
        public void OnFrameUpdate(in FrameUpdateContext context) { }
    }

    internal sealed class Rig : IDisposable
    {
        internal readonly FrameUpdateManager Manager;
        internal readonly LoopDriverHandle Driver;
        internal long Sequence;
        internal Rig(FrameUpdateConfig config = null)
        {
            Manager = new FrameUpdateManager(config ?? FrameUpdateConfig.Default);
            Driver = Manager.BindDriver(Manager.DefaultLoop, "Test", UpdatePhaseMask.Normal);
        }
        internal void Step(double delta = .02, double? unscaled = null)
        {
            Manager.Tick(Driver, UpdatePhase.NormalUpdate,
                new FrameTimeSample(++Sequence, delta, unscaled ?? delta));
        }
        internal UpdateHandle Register(Probe target, UpdateSchedule? schedule = null,
            UpdateGroup group = default, UpdateScope scope = default, int priority = 0)
        {
            return Manager.Register(target, new FrameUpdateOptions
            { Schedule = schedule, Group = group, Scope = scope, Priority = priority });
        }
        public void Dispose() { Driver.Dispose(); Manager.Dispose(); }
    }
}
