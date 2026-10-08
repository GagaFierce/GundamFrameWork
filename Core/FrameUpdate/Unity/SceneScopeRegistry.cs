using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    /// <summary>Explicit scene ownership, without per-frame scene or object discovery.</summary>
    public sealed class SceneScopeRegistry : IDisposable
    {
        private static readonly List<SceneScopeRegistry> Registries = new List<SceneScopeRegistry>();
        private readonly FrameUpdateManager _manager;
        private readonly Dictionary<int, UpdateScope> _scopes = new Dictionary<int, UpdateScope>();
        private bool _disposed;

        public SceneScopeRegistry(FrameUpdateManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (manager.IsDisposed) throw new ObjectDisposedException(nameof(manager));
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Registries.Add(this);
        }

        public UpdateScope GetOrCreate(UnityScene scene)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SceneScopeRegistry));
            if (!scene.IsValid() || !scene.isLoaded)
                throw new ArgumentException("The scene must be valid and loaded.", nameof(scene));
            if (_scopes.TryGetValue(scene.handle, out var scope) && scope.IsValid) return scope;
            scope = _manager.CreateScope("Scene: " + scene.name + " (" + scene.handle + ")");
            _scopes[scene.handle] = scope;
            return scope;
        }

        public bool Rebind(UpdateHandle handle, UnityScene destination)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SceneScopeRegistry));
            return _manager.SetScope(handle, GetOrCreate(destination));
        }

        public bool Release(UnityScene scene)
        {
            if (_disposed || !_scopes.TryGetValue(scene.handle, out var scope)) return false;
            _scopes.Remove(scene.handle);
            if (!_manager.IsDisposed) _manager.ReleaseScope(scope);
            return true;
        }

        private void OnSceneUnloaded(UnityScene scene) { Release(scene); }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Registries.Remove(this);
            if (!_manager.IsDisposed)
                foreach (var pair in _scopes) _manager.ReleaseScope(pair.Value);
            _scopes.Clear();
        }

        internal static void ResetStatics()
        {
            while (Registries.Count > 0) Registries[Registries.Count - 1].Dispose();
        }
    }
}
