using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    /// <summary>A binding has shared ownership: disposing any alias unregisters it once.</summary>
    public sealed class UnityFrameBinding : IDisposable
    {
        private readonly FrameUpdateManager _manager;
        private readonly Action<UpdateHandle> _remove;
        private bool _disposed;
        public UpdateHandle Handle { get; }
        public bool IsValid => !_disposed && Handle.IsValid;

        internal UnityFrameBinding(FrameUpdateManager manager, UpdateHandle handle, Action<UpdateHandle> remove)
        { _manager = manager; Handle = handle; _remove = remove; }

        /// <summary>Move ownership before unloading the old scene, including a move to DontDestroyOnLoad.</summary>
        public bool RebindScope(UpdateScope scope)
        {
            return !_disposed && !_manager.IsDisposed && _manager.SetScope(Handle, scope);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_manager.IsDisposed) _manager.Unregister(Handle);
            _remove(Handle);
        }
    }

    public static class UnityFrameRegistration
    {
        private sealed class OwnerLifetime : IUpdateLifetime
        {
            private UnityEngine.Object _owner;
            internal OwnerLifetime(UnityEngine.Object owner) { _owner = owner; }
            public bool IsAlive
            {
                get
                {
                    if (_owner != null) return true;
                    _owner = null; // Release the managed wrapper after Unity destruction.
                    return false;
                }
            }
        }

        private sealed class Bindings
        {
            internal readonly Dictionary<UpdateHandle, UnityFrameBinding> Items =
                new Dictionary<UpdateHandle, UnityFrameBinding>();
            internal readonly List<UpdateHandle> Stale = new List<UpdateHandle>();
            internal void Remove(UpdateHandle handle) { Items.Remove(handle); }
            internal void Prune()
            {
                foreach (var pair in Items) if (!pair.Value.IsValid) Stale.Add(pair.Key);
                for (int i = 0; i < Stale.Count; i++) Items.Remove(Stale[i]);
                Stale.Clear();
            }
        }

        private sealed class ManagerBindings
        {
            internal readonly ConditionalWeakTable<IFrameUpdate, Bindings> Targets =
                new ConditionalWeakTable<IFrameUpdate, Bindings>();
        }

        private static ConditionalWeakTable<UnityEngine.Object, OwnerLifetime> Owners =
            new ConditionalWeakTable<UnityEngine.Object, OwnerLifetime>();
        private static ConditionalWeakTable<FrameUpdateManager, ManagerBindings> Managers =
            new ConditionalWeakTable<FrameUpdateManager, ManagerBindings>();
        private static readonly List<WeakReference<UnityFrameBinding>> LiveBindings =
            new List<WeakReference<UnityFrameBinding>>();

        public static IDisposable Bind(FrameUpdateManager manager, IFrameUpdate target,
            UnityEngine.Object owner, in FrameUpdateOptions options)
        {
            return BindHandle(manager, target, owner, options);
        }

        /// <summary>Typed equivalent of Bind, useful for scope migration and handle queries.</summary>
        public static UnityFrameBinding BindHandle(FrameUpdateManager manager, IFrameUpdate target,
            UnityEngine.Object owner, in FrameUpdateOptions options)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (owner == null) throw new ArgumentException("A live Unity owner is required.", nameof(owner));
            var lifetime = Owners.GetValue(owner, CreateOwner);
            // Keeping the original target and one cached lifetime preserves core identity and
            // lets Register reject a changed owner/configuration before cache mutation.
            var handle = manager.Register(target, options, lifetime);
            var managerBindings = Managers.GetValue(manager, CreateManager);
            var bindings = managerBindings.Targets.GetValue(target, CreateBindings);
            bindings.Prune();
            if (bindings.Items.TryGetValue(handle, out var existing)) return existing;
            var binding = new UnityFrameBinding(manager, handle, bindings.Remove);
            bindings.Items.Add(handle, binding);
            // Bookkeeping happens only on registration, never in the update hot path.
            for (int i = LiveBindings.Count - 1; i >= 0; i--)
                if (!LiveBindings[i].TryGetTarget(out var candidate) || !candidate.IsValid)
                    LiveBindings.RemoveAt(i);
            LiveBindings.Add(new WeakReference<UnityFrameBinding>(binding));
            return binding;
        }

        private static OwnerLifetime CreateOwner(UnityEngine.Object owner) => new OwnerLifetime(owner);
        private static ManagerBindings CreateManager(FrameUpdateManager manager) => new ManagerBindings();
        private static Bindings CreateBindings(IFrameUpdate target) => new Bindings();

        internal static void ResetStatics()
        {
            for (int i = 0; i < LiveBindings.Count; i++)
                if (LiveBindings[i].TryGetTarget(out var binding)) binding.Dispose();
            LiveBindings.Clear();
            Owners = new ConditionalWeakTable<UnityEngine.Object, OwnerLifetime>();
            Managers = new ConditionalWeakTable<FrameUpdateManager, ManagerBindings>();
        }
    }
}
