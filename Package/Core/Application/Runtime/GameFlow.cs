using System;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;
using WFrameWork.Scene;

namespace WFrameWork.Application
{
    public enum GameFlowState { Boot, Menu, Loading, Playing, Returning, Failed }

    /// <summary>
    /// Small, explicit flow coordinator for the sample and host applications. Scene loading
    /// remains a service concern; this class only coordinates business intent and scope lifetime.
    /// </summary>
    public sealed class GameFlowService : IDisposable
    {
        private readonly Func<CancellationToken, Task<SceneLease>> _loadGame;
        private readonly Func<SceneLease, Task> _unload;
        private readonly Func<Task> _waitForSceneIdle;
        private readonly Func<RuntimeScope> _createSceneScope;
        private readonly DiagnosticLogger _diagnostics;
        private readonly object _gate = new object();
        private Task _transition;
        private CancellationTokenSource _transitionCancellation;
        private Task _returnTransition;
        private SceneLease _scene;
        private RuntimeScope _sceneScope;
        private GameFlowState _state = GameFlowState.Boot;
        private Exception _failure;

        public GameFlowService(Func<CancellationToken, Task<SceneLease>> loadGame, Func<SceneLease, Task> unload,
            Func<Task> waitForSceneIdle, Func<RuntimeScope> createSceneScope, IDiagnosticSink diagnostics = null)
        {
            _loadGame = loadGame ?? throw new ArgumentNullException(nameof(loadGame));
            _unload = unload ?? throw new ArgumentNullException(nameof(unload));
            _waitForSceneIdle = waitForSceneIdle ?? throw new ArgumentNullException(nameof(waitForSceneIdle));
            _createSceneScope = createSceneScope ?? throw new ArgumentNullException(nameof(createSceneScope));
            _diagnostics = new DiagnosticLogger("GameFlow", diagnostics);
        }

        public GameFlowState State { get { lock (_gate) return _state; } }
        public Exception Failure { get { lock (_gate) return _failure; } }
        public SceneLease CurrentScene { get { lock (_gate) return _scene; } }
        public RuntimeScope SceneScope { get { lock (_gate) return _sceneScope; } }

        public Task EnterMenuAsync()
        {
            lock (_gate)
            {
                if (_state == GameFlowState.Menu) return Task.CompletedTask;
                if (_state != GameFlowState.Boot) return Task.FromException(new InvalidOperationException("Menu can only be entered from Boot."));
                _state = GameFlowState.Menu;
                return Task.CompletedTask;
            }
        }

        public Task StartGameAsync()
        {
            lock (_gate)
            {
                if (_state == GameFlowState.Playing) return Task.CompletedTask;
                if (_state == GameFlowState.Loading) return _transition;
                if (_state != GameFlowState.Menu) return Task.FromException(new InvalidOperationException("Game can only start from Menu."));
                _transitionCancellation = new CancellationTokenSource();
                _state = GameFlowState.Loading;
                _transition = StartCoreAsync(_transitionCancellation.Token);
                return _transition;
            }
        }

        public Task ReturnToMenuAsync()
        {
            lock (_gate)
            {
                if (_state == GameFlowState.Menu) return Task.CompletedTask;
                if (_returnTransition != null)
                {
                    if (!_returnTransition.IsCompleted) return _returnTransition;
                    _returnTransition = null;
                }
                if (_state == GameFlowState.Loading || _state == GameFlowState.Playing)
                    _transitionCancellation?.Cancel();
                _state = GameFlowState.Returning;
                Task transition = _transition;
                _returnTransition = transition != null && !transition.IsCompleted
                    ? ReturnAfterTransitionAsync(transition)
                    : ReturnCoreAsync();
                return _returnTransition;
            }
        }

        private async Task ReturnAfterTransitionAsync(Task transition)
        {
            try { await transition; } catch (OperationCanceledException) { } catch { }
            await ReturnCoreAsync().ConfigureAwait(false);
        }

        private async Task StartCoreAsync(CancellationToken token)
        {
            SceneLease loaded = null;
            RuntimeScope scope = null;
            try
            {
                loaded = await _loadGame(token);
                token.ThrowIfCancellationRequested();
                lock (_gate) if (_state != GameFlowState.Loading) throw new OperationCanceledException(token);
                scope = _createSceneScope();
                lock (_gate)
                {
                    if (_state != GameFlowState.Loading)
                    {
                        // ReturnToMenuAsync won the transition race after the load completed.
                        throw new OperationCanceledException(token);
                    }
                    _scene = loaded;
                    _sceneScope = scope;
                    _state = GameFlowState.Playing;
                }
            }
            catch (OperationCanceledException)
            {
                if (scope != null) { try { await scope.CloseAsync().ConfigureAwait(false); } catch { } }
                if (loaded != null) await _unload(loaded).ConfigureAwait(false);
                await _waitForSceneIdle().ConfigureAwait(false);
                lock (_gate) _state = GameFlowState.Menu;
                throw;
            }
            catch (Exception error)
            {
                if (scope != null) { try { await scope.CloseAsync().ConfigureAwait(false); } catch { } }
                if (loaded != null) { try { await _unload(loaded).ConfigureAwait(false); } catch { } }
                try { await _waitForSceneIdle().ConfigureAwait(false); } catch { }
                lock (_gate) { _failure = error; _state = GameFlowState.Failed; }
                _diagnostics.Error("Game scene load failed.", error);
                throw;
            }
        }

        private async Task ReturnCoreAsync()
        {
            SceneLease scene;
            RuntimeScope scope;
            lock (_gate)
            {
                scene = _scene;
                scope = _sceneScope;
                _scene = null; _sceneScope = null;
            }
            ListException errors = new ListException();
            if (scope != null) { try { await scope.CloseAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); } }
            if (scene != null) { try { await _unload(scene).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); } }
            try { await _waitForSceneIdle().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            lock (_gate) { _state = errors.Count == 0 ? GameFlowState.Menu : GameFlowState.Failed; if (errors.Count > 0) _failure = errors.ToAggregate(); }
            if (errors.Count > 0) throw errors.ToAggregate();
        }

        public Task ShutdownAsync() => ReturnToMenuAsync();
        public void Dispose() { _transitionCancellation?.Cancel(); _ = ReturnToMenuAsync(); }

        private sealed class ListException
        {
            private readonly System.Collections.Generic.List<Exception> _items = new System.Collections.Generic.List<Exception>();
            internal int Count => _items.Count;
            internal void Add(Exception error) { _items.Add(error); }
            internal AggregateException ToAggregate() => new AggregateException("Game flow transition failed.", _items);
        }
    }
}
