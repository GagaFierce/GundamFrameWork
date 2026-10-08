using System;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Application;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.Diagnostics;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Application.Unity
{
    /// <summary>
    /// Unity-facing composition shell. It owns the manager, frame host and main-thread
    /// boundary; GameRuntime owns only the registered application parts.
    /// </summary>
    public sealed class UnityGameRuntime : IDisposable
    {
        private readonly UnityMainThreadDispatcher _mainThread;
        private readonly UnityFrameUpdateHost _host;
        private readonly FrameUpdateManager _manager;
        private readonly GameRuntime _runtime;
        private readonly object _gate = new object();
        private Task _shutdownTask;

        private UnityGameRuntime(FrameUpdateManager manager, UnityFrameUpdateHost host,
            UnityMainThreadDispatcher mainThread, GameRuntime runtime)
        { _manager = manager; _host = host; _mainThread = mainThread; _runtime = runtime; }

        public GameRuntime Runtime => _runtime;
        public FrameUpdateManager FrameUpdate => _manager;
        public UnityFrameUpdateHost Host => _host;
        public UnityMainThreadDispatcher MainThread => _mainThread;

        public static UnityGameRuntime Create(IDiagnosticSink diagnostics = null, UnityFrameUpdateSettings settings = null)
        {
            var manager = new FrameUpdateManager(FrameUpdateConfig.Default);
            var mainThread = new UnityMainThreadDispatcher();
            UnityFrameUpdateHost host;
            try { host = UnityFrameUpdateHost.Install(manager, settings); }
            catch { manager.Dispose(); mainThread.StopAcceptingWork(); throw; }
            var runtime = new GameRuntime(manager, mainThread, diagnostics);
            return new UnityGameRuntime(manager, host, mainThread, runtime);
        }

        public async Task ShutdownAsync()
        {
            Task shutdown;
            lock (_gate)
            {
                if (_shutdownTask == null) _shutdownTask = ShutdownCoreAsync();
                shutdown = _shutdownTask;
            }
            await shutdown;
        }

        private async Task ShutdownCoreAsync()
        {
            try { await _runtime.ShutdownAsync(); }
            finally
            {
                _host.Dispose();
                _manager.Dispose();
                _mainThread.StopAcceptingWork();
            }
        }

        public void Dispose() { _ = ShutdownAsync(); }
    }
}
