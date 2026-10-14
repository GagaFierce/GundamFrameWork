using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WFrameWork.Core.FrameUpdate.Unity
{
    /// <summary>
    /// Explicit Unity driver. It owns driver bindings, never the manager or engine time.
    /// All methods must be called on Unity's main thread.
    /// </summary>
    public sealed class UnityFrameUpdateHost : IDisposable
    {
        private static readonly Dictionary<FrameUpdateManager, UnityFrameUpdateHost> Hosts =
            new Dictionary<FrameUpdateManager, UnityFrameUpdateHost>();
        private static int _mainThreadId;
        private readonly ApplicationPausePolicy _backgroundPolicy;
        private readonly UpdateGroup[] _backgroundGroups;
        private readonly List<UpdateLoop> _createdLoops = new List<UpdateLoop>(3);
        private readonly List<PauseHandle> _focusPauses = new List<PauseHandle>();
        private readonly List<PauseHandle> _applicationPauses = new List<PauseHandle>();
        private LoopDriverHandle _inputDriver, _simulationDriver, _physicsDriver, _presentationDriver;
        private UnityFrameUpdateDriver _component;
        private long _inputSequence, _simulationSequence, _physicsSequence, _presentationSequence;
        private bool _resetInput, _resetSimulation, _resetPhysics, _resetPresentation;
        private bool _hasPresentationSample, _focused = true, _applicationPaused;
        private FrameTimeSample _presentationSample;
        private bool _disposed;

        public FrameUpdateManager Manager { get; }
        public UnityFrameUpdateLoops Loops { get; private set; }
        public bool IsDisposed => _disposed;

        private UnityFrameUpdateHost(FrameUpdateManager manager, UnityFrameUpdateSettings settings)
        {
            Manager = manager;
            _backgroundPolicy = settings.BackgroundPolicy;
            if (!Enum.IsDefined(typeof(ApplicationPausePolicy), _backgroundPolicy))
                throw new ArgumentOutOfRangeException(nameof(settings.BackgroundPolicy));
            var groups = settings.BackgroundGroups;
            _backgroundGroups = new UpdateGroup[groups?.Count ?? 0];
            for (int i = 0; i < _backgroundGroups.Length; i++) _backgroundGroups[i] = groups[i];
        }

        public static UnityFrameUpdateHost Install(FrameUpdateManager manager,
            UnityFrameUpdateSettings settings = null)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            EnsureMainThread();
            if (!UnityEngine.Application.isPlaying) throw new InvalidOperationException("Install requires Play mode.");
            if (Hosts.ContainsKey(manager)) throw new InvalidOperationException("This manager already has a Unity host.");
            settings = settings ?? new UnityFrameUpdateSettings();
            var host = new UnityFrameUpdateHost(manager, settings);
            try
            {
                if (settings.Loops.HasValue) host.Loops = settings.Loops.Value;
                else
                {
                    UpdateLoop input = host.CreateLoop("Input");
                    UpdateLoop physics = host.CreateLoop("Physics");
                    UpdateLoop presentation = host.CreateLoop("Presentation");
                    host.Loops = new UnityFrameUpdateLoops(input, manager.DefaultLoop, physics, presentation);
                }
                host.BindDrivers();
                // Validate selected group ownership before creating a GameObject.
                if (host._backgroundPolicy == ApplicationPausePolicy.PauseSelectedGroups)
                {
                    host.AcquirePauses(host._focusPauses);
                    ReleasePauses(host._focusPauses);
                }
                var go = new GameObject(string.IsNullOrWhiteSpace(settings.HostName) ? "Frame Update Host" : settings.HostName);
                go.SetActive(false);
                host._component = go.AddComponent<UnityFrameUpdateDriver>();
                host._component.Attach(host);
                Object.DontDestroyOnLoad(go);
                Hosts.Add(manager, host);
                go.SetActive(true);
                host.SetApplicationFocus(UnityEngine.Application.isFocused);
                return host;
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        public void ResetTimeBaseline()
        {
            EnsureMainThread();
            if (_disposed) return;
            _resetInput = _resetSimulation = _resetPhysics = _resetPresentation = true;
        }

        private UpdateLoop CreateLoop(string name)
        {
            var loop = Manager.CreateLoop(name);
            _createdLoops.Add(loop);
            return loop;
        }

        private void BindDrivers()
        {
            _inputDriver = Manager.BindDriver(Loops.Input, "Unity.Input");
            _simulationDriver = Manager.BindDriver(Loops.Simulation, "Unity.Simulation");
            _physicsDriver = Manager.BindDriver(Loops.Physics, "Unity.Physics");
            _presentationDriver = Manager.BindDriver(Loops.Presentation, "Unity.Presentation");
        }

        internal void RenderUpdate(long frameId, double scaledDelta, double unscaledDelta)
        {
            if (_disposed) return;
            if (_hasPresentationSample)
                throw new InvalidOperationException("Presentation LateUpdate must finish before the next Update.");
            Manager.RecordHostFrame(new HostFrameSample(frameId, unscaledDelta));
            var input = Sample(ref _inputSequence, ref _resetInput, frameId, scaledDelta, unscaledDelta);
            TickAll(_inputDriver, input);
            if (_disposed) return;
            var simulation = Sample(ref _simulationSequence, ref _resetSimulation, frameId, scaledDelta, unscaledDelta);
            TickAll(_simulationDriver, simulation);
            if (_disposed) return;
            _presentationSample = Sample(ref _presentationSequence, ref _resetPresentation, frameId, scaledDelta, unscaledDelta);
            _hasPresentationSample = true;
            Manager.Tick(_presentationDriver, UpdatePhase.EarlyUpdate, _presentationSample);
        }

        internal void RenderLateUpdate()
        {
            if (_disposed || !_hasPresentationSample) return;
            Manager.Tick(_presentationDriver, UpdatePhase.NormalUpdate, _presentationSample);
            if (_disposed) return;
            Manager.Tick(_presentationDriver, UpdatePhase.LateUpdate, _presentationSample);
            _hasPresentationSample = false;
        }

        internal void PhysicsUpdate(long frameId, double scaledDelta, double unscaledDelta)
        {
            if (_disposed) return;
            var sample = Sample(ref _physicsSequence, ref _resetPhysics, frameId, scaledDelta, unscaledDelta);
            TickAll(_physicsDriver, sample);
        }

        private static FrameTimeSample Sample(ref long sequence, ref bool reset,
            long frameId, double scaledDelta, double unscaledDelta)
        {
            bool zero = reset;
            reset = false;
            return new FrameTimeSample(checked(++sequence), zero ? 0 : scaledDelta,
                zero ? 0 : unscaledDelta, frameId);
        }

        private void TickAll(LoopDriverHandle driver, in FrameTimeSample sample)
        {
            Manager.Tick(driver, UpdatePhase.EarlyUpdate, sample);
            if (_disposed) return;
            Manager.Tick(driver, UpdatePhase.NormalUpdate, sample);
            if (_disposed) return;
            Manager.Tick(driver, UpdatePhase.LateUpdate, sample);
        }

        internal void SetApplicationFocus(bool focused)
        {
            if (_disposed || _focused == focused) return;
            _focused = focused;
            if (focused) ReleasePauses(_focusPauses);
            else AcquirePauses(_focusPauses);
        }

        internal void SetApplicationPaused(bool paused)
        {
            if (_disposed || _applicationPaused == paused) return;
            _applicationPaused = paused;
            if (paused) AcquirePauses(_applicationPauses);
            else ReleasePauses(_applicationPauses);
            ResetTimeBaseline();
        }

        private void AcquirePauses(List<PauseHandle> pauses)
        {
            if (_backgroundPolicy == ApplicationPausePolicy.PauseAll) pauses.Add(Manager.PauseAll());
            else if (_backgroundPolicy == ApplicationPausePolicy.PauseSelectedGroups)
            {
                try
                {
                    for (int i = 0; i < _backgroundGroups.Length; i++)
                        pauses.Add(Manager.PauseGroup(_backgroundGroups[i]));
                }
                catch { ReleasePauses(pauses); throw; }
            }
        }

        private static void ReleasePauses(List<PauseHandle> pauses)
        {
            for (int i = 0; i < pauses.Count; i++) pauses[i].Dispose();
            pauses.Clear();
        }

        public void Dispose()
        {
            EnsureMainThread();
            if (_disposed) return;
            _disposed = true;
            _hasPresentationSample = false;
            Hosts.Remove(Manager);
            if (_component != null)
            {
                var go = _component.gameObject;
                _component.Detach();
                _component.enabled = false;
                _component = null;
                if (UnityEngine.Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
            }
            ReleasePauses(_focusPauses);
            ReleasePauses(_applicationPauses);
            _inputDriver?.Dispose();
            _simulationDriver?.Dispose();
            _physicsDriver?.Dispose();
            _presentationDriver?.Dispose();
            // A live registration keeps its loop available for a replacement driver.
            for (int i = _createdLoops.Count - 1; i >= 0; i--)
            {
                try { Manager.RemoveLoop(_createdLoops[i]); }
                catch (ObjectDisposedException) { break; }
            }
            _createdLoops.Clear();
        }

        private static void EnsureMainThread()
        {
            if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != _mainThreadId)
                throw new InvalidOperationException("Unity frame adapters must run on the Unity main thread.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            while (Hosts.Count > 0)
            {
                UnityFrameUpdateHost host = null;
                foreach (var pair in Hosts) { host = pair.Value; break; }
                host.Dispose();
            }
            UnityFrameRegistration.ResetStatics();
            SceneScopeRegistry.ResetStatics();
        }
    }
}
