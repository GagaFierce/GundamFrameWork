using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using WFrameWork.Audio;
using WFrameWork.Core.ResLoad;
using WFrameWork.Threading;
using WFrameWork.Threading.Unity;

namespace WFrameWork.Audio.Unity
{
    public sealed class AddressablesAudioBackend : IAudioBackend
    {
        private sealed class Playback : IAudioPlayback, IAudioPlaybackVolume
        {
            private readonly AddressablesAudioBackend _owner;
            internal readonly AudioSource Source;
            private bool _stopped;
            internal Playback(AddressablesAudioBackend owner, AudioSource source) { _owner = owner; Source = source; }
            public bool IsPlaying => !_stopped && Source != null && Source.isPlaying;
            public void Stop() { if (_stopped) return; _stopped = true; if (Source != null) Source.Stop(); _owner.Release(Source); }
            public void SetVolume(float volume)
            {
                if (_stopped || Source == null) return;
                if (!_owner._mainThread.IsMainThread) throw new InvalidOperationException("AudioSource volume must be changed on the Unity main thread.");
                Source.volume = volume;
            }
            public void Dispose() => Stop();
            internal void Complete() { if (_stopped) return; _stopped = true; _owner.Release(Source); }
        }

        private readonly ResourceService _resources;
        private readonly Transform _root;
        private readonly AudioMixerGroup _bgmGroup;
        private readonly AudioMixerGroup _sfxGroup;
        private readonly AudioMixerGroup _uiGroup;
        private readonly IMainThreadDispatcher _mainThread;
        private readonly Stack<AudioSource> _free = new Stack<AudioSource>();
        private readonly List<Playback> _active = new List<Playback>();
        private bool _disposed;

        public AddressablesAudioBackend(ResourceService resources, Transform root, AudioMixerGroup bgmGroup = null,
            AudioMixerGroup sfxGroup = null, AudioMixerGroup uiGroup = null, IMainThreadDispatcher mainThread = null)
        { _resources = resources ?? throw new ArgumentNullException(nameof(resources)); _root = root; _bgmGroup = bgmGroup; _sfxGroup = sfxGroup; _uiGroup = uiGroup; _mainThread = mainThread ?? new UnityMainThreadDispatcher(); }

        public async Task<AudioBackendClip> LoadClipAsync(string key, CancellationToken token)
        {
            ResourceLease<AudioClip> lease = await _resources.LoadAssetAsync<AudioClip>(key, cancellationToken: token);
            return new AudioBackendClip(lease.Asset, lease.DisposeAsync, true);
        }

        public IAudioPlayback Play(AudioBackendClip clip, AudioPlayRequest request, float effectiveVolume, Action completed)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AddressablesAudioBackend));
            if (_mainThread != null && !_mainThread.IsMainThread) throw new InvalidOperationException("AudioSource operations must run on the Unity main thread.");
            var audioClip = clip.Clip as AudioClip; if (audioClip == null) throw new InvalidOperationException("Loaded audio asset is not an AudioClip.");
            AudioSource source = _free.Count > 0 ? _free.Pop() : CreateSource();
            source.clip = audioClip; source.loop = request.Loop; source.volume = effectiveVolume; source.spatialBlend = request.Spatial ? 1 : 0;
            source.transform.position = new Vector3(request.X, request.Y, request.Z); source.outputAudioMixerGroup = Group(request.Bus); source.Play();
            var playback = new Playback(this, source); _active.Add(playback); return playback;
        }

        private AudioMixerGroup Group(AudioBus bus) => bus == AudioBus.Bgm ? _bgmGroup : bus == AudioBus.Ui ? _uiGroup : _sfxGroup;
        private AudioSource CreateSource()
        {
            var go = new GameObject("GFramework.AudioSource"); go.transform.SetParent(_root, false); return go.AddComponent<AudioSource>();
        }
        private void Release(AudioSource source)
        {
            if (source == null) return; source.Stop(); source.clip = null; source.loop = false; source.outputAudioMixerGroup = null;
            if (!_disposed) _free.Push(source); else UnityEngine.Object.Destroy(source.gameObject);
        }
        public void Tick()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var item = _active[i];
                if (item == null || !item.IsPlaying) { item?.Complete(); _active.RemoveAt(i); }
            }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            if (!_mainThread.IsMainThread) throw new InvalidOperationException("Audio backend must be disposed on the Unity main thread.");
            for (int i = _active.Count - 1; i >= 0; i--) _active[i].Stop();
            while (_free.Count > 0) { var source = _free.Pop(); if (source != null) UnityEngine.Object.Destroy(source.gameObject); }
        }
    }

    [DisallowMultipleComponent]
    public sealed class AudioServiceBehaviour : MonoBehaviour
    {
        [SerializeField] private Transform sourceRoot;
        [SerializeField] private AudioMixerGroup bgmGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        private AddressablesAudioBackend _backend;
        private AudioService _service;
        public AudioService Service => _service;

        public void Initialize(ResourceService resources, IMainThreadDispatcher mainThread = null)
        {
            if (_service != null) throw new InvalidOperationException("Audio service is already initialized.");
            _backend = new AddressablesAudioBackend(resources, sourceRoot == null ? transform : sourceRoot, bgmGroup, sfxGroup, uiGroup, mainThread);
            _service = new AudioService(_backend, mainThread: mainThread);
        }
        public Task ShutdownAsync() => _service == null ? Task.CompletedTask : _service.CloseAsync();
        private void Update() { _service?.Tick(); }
        private void OnDestroy() { _ = ShutdownAsync(); _service = null; _backend = null; }
    }
}
