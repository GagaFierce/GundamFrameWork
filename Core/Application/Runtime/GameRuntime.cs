using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Audio;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.ResLoad;
using WFrameWork.Diagnostics;
using WFrameWork.Input;
using WFrameWork.Physics;
using WFrameWork.Scene;
using WFrameWork.Threading;
using WFrameWork.UI;

namespace WFrameWork.Application
{
    public enum GameRuntimeState { Created, Initializing, Running, Stopping, Stopped, Failed }

    public sealed class GameRuntimePart
    {
        public string Name { get; }
        public Func<CancellationToken, Task> Initialize { get; }
        public Func<Task> Shutdown { get; }
        public RuntimeOwnership Ownership { get; }
        public bool RequiresMainThread { get; }

        public GameRuntimePart(string name, Func<CancellationToken, Task> initialize = null,
            Func<Task> shutdown = null, RuntimeOwnership ownership = RuntimeOwnership.Owned, bool requiresMainThread = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Part name is required.", nameof(name));
            Name = name.Trim(); Initialize = initialize ?? (_ => Task.CompletedTask);
            Shutdown = shutdown ?? (() => Task.CompletedTask); Ownership = ownership; RequiresMainThread = requiresMainThread;
        }
    }

    /// <summary>
    /// Explicit application composition root. It owns only parts registered as Owned and
    /// serializes initialization/shutdown so callers can safely race those requests.
    /// </summary>
    public sealed class GameRuntime : IDisposable
    {
        private readonly object _gate = new object();
        private readonly List<GameRuntimePart> _parts = new List<GameRuntimePart>();
        private readonly List<GameRuntimePart> _started = new List<GameRuntimePart>();
        private readonly DiagnosticLogger _diagnostics;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly RuntimeScope _applicationScope;
        private Task _initializeTask;
        private Task _shutdownTask;
        private bool _shutdownRequested;
        private Exception _failure;
        private GameRuntimeState _state = GameRuntimeState.Created;
        private readonly IDisposable _diagnosticRegistration;

        public GameRuntime(FrameUpdateManager frameUpdate = null, IMainThreadDispatcher mainThread = null,
            IDiagnosticSink diagnostics = null)
        {
            FrameUpdate = frameUpdate ?? new FrameUpdateManager(FrameUpdateConfig.Default);
            OwnsFrameUpdate = frameUpdate == null;
            MainThread = mainThread ?? new InlineMainThreadDispatcher();
            OwnsMainThread = mainThread == null;
            _diagnostics = new DiagnosticLogger("Application", diagnostics);
            _applicationScope = new RuntimeScope("Application", diagnostics: diagnostics);
            Diagnostics = new DiagnosticRegistry();
            _diagnosticRegistration = Diagnostics.Register("Application", snapshot =>
            {
                snapshot.Set("state", State);
                snapshot.Set("ready", IsReady);
                snapshot.Set("scope", _applicationScope.State);
                snapshot.Set("scope.children", _applicationScope.ChildCount);
                RuntimeScopeSnapshot scopeSnapshot = _applicationScope.CaptureSnapshot();
                snapshot.Set("scope.operations", scopeSnapshot.ActiveOperationCount);
                snapshot.Set("scope.cleanups", scopeSnapshot.RegisteredCleanupCount);
                AppendScopeDiagnostics(snapshot, scopeSnapshot, "scope.tree");
                snapshot.Set("mainThread.accepting", MainThread.IsAcceptingWork);
                snapshot.Set("frameUpdate.owned", OwnsFrameUpdate);
                if (Resources != null)
                {
                    snapshot.Set("resources.assetEntries", Resources.ActiveAssetEntryCount);
                    snapshot.Set("resources.assetLeases", Resources.ActiveAssetLeaseCount);
                    snapshot.Set("resources.instanceLeases", Resources.ActiveInstanceLeaseCount);
                    snapshot.Set("resources.pendingOperations", Resources.PendingOperationCount);
                }
                if (SceneFlow != null)
                {
                    snapshot.Set("scene.state", SceneFlow.State);
                    snapshot.Set("scene.loaded", SceneFlow.LoadedSceneCount);
                    snapshot.Set("scene.loading", SceneFlow.IsLoading);
                    snapshot.Set("scene.loadingKey", SceneFlow.LoadingSceneKey);
                }
                if (UI != null)
                {
                    snapshot.Set("ui.openPanels", UI.OpenPanelCount);
                    snapshot.Set("ui.modalDepth", UI.ModalDepth);
                }
                if (Audio != null)
                {
                    snapshot.Set("audio.active", Audio.ActivePlaybackCount);
                    snapshot.Set("audio.pendingOperations", Audio.PendingOperationCount);
                }
            });
        }

        public FrameUpdateManager FrameUpdate { get; }
        public IMainThreadDispatcher MainThread { get; }
        public bool OwnsMainThread { get; }
        public RuntimeScope ApplicationScope => _applicationScope;
        public bool OwnsFrameUpdate { get; }
        public GameRuntimeState State { get { lock (_gate) return _state; } }
        public Exception Failure { get { lock (_gate) return _failure; } }
        public bool IsReady => State == GameRuntimeState.Running;
        public DiagnosticRegistry Diagnostics { get; }
        public DiagnosticSnapshot CaptureDiagnostics() => Diagnostics.Capture();

        private static void AppendScopeDiagnostics(DiagnosticSnapshot snapshot, RuntimeScopeSnapshot scope, string path)
        {
            snapshot.Set(path + ".state", scope.State);
            snapshot.Set(path + ".operations", scope.ActiveOperationCount);
            snapshot.Set(path + ".cleanups", scope.RegisteredCleanupCount);
            for (int i = 0; i < scope.Children.Count; i++)
                AppendScopeDiagnostics(snapshot, scope.Children[i], path + "." + i);
        }

        // These properties keep the composition root discoverable without making it a service locator.
        public ResourceService Resources { get; private set; }
        public SceneFlowService SceneFlow { get; private set; }
        public AudioService Audio { get; private set; }
        public InputService Input { get; private set; }
        public PhysicsStepDispatcher Physics { get; private set; }
        public UiPanelManager UI { get; private set; }

        public void SetModuleServices(ResourceService resources = null, SceneFlowService sceneFlow = null,
            AudioService audio = null, InputService input = null, PhysicsStepDispatcher physics = null,
            UiPanelManager ui = null)
        {
            lock (_gate)
            {
                if (_state != GameRuntimeState.Created) throw new InvalidOperationException("Module services must be set before initialization.");
                Resources = resources; SceneFlow = sceneFlow; Audio = audio; Input = input; Physics = physics; UI = ui;
            }
        }

        public void AddPart(GameRuntimePart part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            lock (_gate)
            {
                if (_state != GameRuntimeState.Created) throw new InvalidOperationException("Runtime parts must be added before initialization.");
                _parts.Add(part);
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            lock (_gate)
            {
                if (_state == GameRuntimeState.Running) return Task.CompletedTask;
                if (_state == GameRuntimeState.Initializing) return _initializeTask;
                if (_state == GameRuntimeState.Stopping || _state == GameRuntimeState.Stopped)
                    return Task.FromException(new InvalidOperationException("Runtime is stopping or stopped."));
                if (_state == GameRuntimeState.Failed) return Task.FromException(_failure ?? new InvalidOperationException("Runtime initialization failed."));
                _state = GameRuntimeState.Initializing;
                _initializeTask = InitializeCoreAsync(cancellationToken);
                return _initializeTask;
            }
        }

        public Task ShutdownAsync()
        {
            TaskCompletionSource<bool> completion;
            Task initialization;
            lock (_gate)
            {
                if (_shutdownTask != null) return _shutdownTask;
                _shutdownRequested = true;
                if (_state == GameRuntimeState.Created) _state = GameRuntimeState.Stopping;
                else if (_state != GameRuntimeState.Stopped) _state = GameRuntimeState.Stopping;
                initialization = _initializeTask;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _shutdownTask = completion.Task;
            }
            _ = ShutdownCoreAsync(initialization, completion);
            return completion.Task;
        }

        private async Task InitializeCoreAsync(CancellationToken callerToken)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _lifetime.Token))
            {
                try
                {
                    for (int i = 0; i < _parts.Count; i++)
                    {
                        GameRuntimePart part = _parts[i];
                        linked.Token.ThrowIfCancellationRequested();
                        await InvokePartAsync(part, linked.Token);
                        bool shutdownWonRace;
                        lock (_gate)
                        {
                            shutdownWonRace = _shutdownRequested;
                            // A late successful initializer owns real resources too. Include
                            // it in the same scope-first rollback as every other started part.
                            _started.Add(part);
                        }
                        if (shutdownWonRace)
                        {
                            throw new OperationCanceledException("Runtime shutdown started during initialization.");
                        }
                    }
                    lock (_gate)
                    {
                        if (_shutdownRequested) throw new OperationCanceledException("Runtime shutdown started during initialization.");
                        _state = GameRuntimeState.Running;
                    }
                }
                catch (Exception error)
                {
                    List<Exception> rollbackErrors = new List<Exception>();
                    try { await _applicationScope.CloseAsync(); }
                    catch (Exception cleanupError) { rollbackErrors.Add(cleanupError); }
                    await ShutdownStartedPartsAsync(rollbackErrors);
                    lock (_gate)
                    {
                        if (rollbackErrors.Count > 0) rollbackErrors.Insert(0, error);
                        _failure = rollbackErrors.Count == 0 ? error : new AggregateException("Runtime initialization and rollback failed.", rollbackErrors);
                        _state = _shutdownRequested ? GameRuntimeState.Stopped : GameRuntimeState.Failed;
                    }
                    if (!_shutdownRequested) _diagnostics.Error("Runtime initialization failed.", _failure);
                    if (rollbackErrors.Count > 0) throw _failure;
                    throw;
                }
            }
        }

        private async Task ShutdownCoreAsync(Task initialization, TaskCompletionSource<bool> completion)
        {
            List<Exception> errors = new List<Exception>();
            try { _lifetime.Cancel(); }
            catch (AggregateException aggregate) { errors.AddRange(aggregate.Flatten().InnerExceptions); }
            catch (Exception error) { errors.Add(error); }
            if (initialization != null)
            {
                try { await initialization; }
                catch (OperationCanceledException) { }
                catch (Exception error) { errors.Add(error); }
            }
            try { await _applicationScope.CloseAsync(); }
            catch (Exception error) { errors.Add(error); }
            // Business scopes release leases held by module consumers. Resource and other
            // runtime parts are closed only after those leases have been returned.
            await ShutdownStartedPartsAsync(errors);
            if (OwnsFrameUpdate)
            {
                try { await MainThread.RunAsync(() => FrameUpdate.Dispose()); }
                catch (Exception error) { errors.Add(error); }
            }
            if (OwnsMainThread) MainThread.StopAcceptingWork();
            lock (_gate)
            {
                if (errors.Count == 0) _state = GameRuntimeState.Stopped;
                else { _failure = new AggregateException("Runtime shutdown completed with errors.", errors); _state = GameRuntimeState.Failed; }
            }
            if (errors.Count > 0) completion.TrySetException(_failure);
            else completion.TrySetResult(true);
        }

        private Task ShutdownStartedPartsAsync() => ShutdownStartedPartsAsync(new List<Exception>());

        private async Task ShutdownStartedPartsAsync(List<Exception> errors)
        {
            GameRuntimePart[] started;
            lock (_gate) { started = _started.ToArray(); _started.Clear(); }
            for (int i = started.Length - 1; i >= 0; i--)
            {
                GameRuntimePart part = started[i];
                if (part.Ownership != RuntimeOwnership.Owned) continue;
                try { await InvokeShutdownAsync(part); }
                catch (OperationCanceledException) { }
                catch (Exception error) { errors.Add(error); _diagnostics.Error("Runtime part shutdown failed: " + part.Name, error); }
            }
        }

        private Task InvokePartAsync(GameRuntimePart part, CancellationToken token)
        { return part.RequiresMainThread ? MainThread.RunAsync(() => part.Initialize(token)).Unwrap() : part.Initialize(token); }

        private Task InvokeShutdownAsync(GameRuntimePart part)
        { return part.RequiresMainThread ? MainThread.RunAsync(() => part.Shutdown()).Unwrap() : part.Shutdown(); }

        public void Dispose() { _ = ShutdownAsync(); }
    }
}
