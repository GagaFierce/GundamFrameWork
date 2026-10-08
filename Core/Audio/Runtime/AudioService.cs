using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;
using WFrameWork.Threading;

namespace WFrameWork.Audio
{
    public enum AudioBus { Bgm, Sfx, Ui }

    public readonly struct AudioPlayRequest
    {
        public AudioBus Bus { get; }
        public float Volume { get; }
        public bool Loop { get; }
        public bool Spatial { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public AudioPlayRequest(AudioBus bus, float volume = 1, bool loop = false, bool spatial = false, float x = 0, float y = 0, float z = 0)
        { Bus = bus; Volume = volume; Loop = loop; Spatial = spatial; X = x; Y = y; Z = z; }
    }

    public sealed class AudioBackendClip
    {
        private Action _release;
        public object Clip { get; }
        internal AudioBackendClip(object clip, Action release) { Clip = clip ?? throw new ArgumentNullException(nameof(clip)); _release = release; }
        public bool IsReleased { get; private set; }
        public void Release() { if (IsReleased) return; IsReleased = true; var release = _release; _release = null; release?.Invoke(); }
    }

    public interface IAudioPlayback : IDisposable
    {
        bool IsPlaying { get; }
        void Stop();
    }

    public interface IAudioBackend : IDisposable
    {
        Task<AudioBackendClip> LoadClipAsync(string key, CancellationToken token);
        IAudioPlayback Play(AudioBackendClip clip, AudioPlayRequest request, float effectiveVolume, Action completed);
        void Tick();
    }

    public sealed class AudioPlaybackHandle : IDisposable
    {
        private readonly AudioService _owner;
        private readonly AudioEntry _entry;
        private IAudioPlayback _playback;
        private bool _stopped;
        internal AudioPlaybackHandle(AudioService owner, AudioEntry entry, IAudioPlayback playback) { _owner = owner; _entry = entry; _playback = playback; }
        public bool IsPlaying => !_stopped && _playback != null && _playback.IsPlaying;
        public void Stop() { if (_stopped) return; _stopped = true; _owner.ReleasePlayback(_entry, _playback); _playback = null; }
        public void Dispose() => Stop();
    }

    internal sealed class AudioEntry
    {
        internal readonly string Key; internal readonly Task<AudioBackendClip> Load;
        internal AudioBackendClip Clip; internal int Holders; internal bool Removed; internal bool Released;
        internal AudioEntry(string key, Task<AudioBackendClip> load) { Key = key; Load = load; }
    }

    public sealed class AudioService : IDisposable
    {
        private readonly IAudioBackend _backend;
        private readonly DiagnosticLogger _diagnostics;
        private readonly IMainThreadDispatcher _mainThread;
        private readonly Dictionary<string, AudioEntry> _clips = new Dictionary<string, AudioEntry>(StringComparer.Ordinal);
        private readonly Dictionary<AudioBus, List<AudioPlaybackHandle>> _active = new Dictionary<AudioBus, List<AudioPlaybackHandle>>();
        private readonly Dictionary<AudioBus, float> _volume = new Dictionary<AudioBus, float>();
        private readonly Dictionary<AudioBus, int> _limits = new Dictionary<AudioBus, int>();
        private readonly object _gate = new object();
        private long _bgmRequest;
        private AudioPlaybackHandle _bgm;
        private bool _disposed;
        private bool _muted;
        private Task _closeTask;
        private int _pendingOperations;
        private TaskCompletionSource<bool> _operationSignal = NewSignal();

        public AudioService(IAudioBackend backend, IDiagnosticSink diagnostics = null, IMainThreadDispatcher mainThread = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnostics = new DiagnosticLogger("Audio", diagnostics);
            _mainThread = mainThread;
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus))) { _volume[bus] = 1; _limits[bus] = bus == AudioBus.Bgm ? 1 : 16; _active[bus] = new List<AudioPlaybackHandle>(); }
        }
        public bool IsMuted => _muted;
        public int ActivePlaybackCount { get { int count = 0; foreach (var pair in _active) count += pair.Value.Count; return count; } }
        public float GetVolume(AudioBus bus) => _volume[bus];
        public void SetMuted(bool muted) { _muted = muted; }
        public void SetVolume(AudioBus bus, float volume) { _volume[bus] = Clamp(volume); }
        public void SetConcurrencyLimit(AudioBus bus, int limit) { if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit)); _limits[bus] = limit; }

        public async Task<AudioPlaybackHandle> PlayAsync(string key, AudioPlayRequest request, CancellationToken token = default(CancellationToken))
        {
            EnsureUsable();
            AudioEntry entry = GetOrCreateEntry(key);
            Interlocked.Increment(ref _pendingOperations);
            bool held = true;
            try
            {
                if (!await WaitForCompletion(entry.Load, token)) { ReleaseClipHolder(entry); held = false; token.ThrowIfCancellationRequested(); }
                entry.Clip = await entry.Load;
                EnsureUsable();
                float effective = (_muted ? 0 : _volume[request.Bus]) * Clamp(request.Volume);
                EnforceLimit(request.Bus);
                AudioPlaybackHandle result = null;
                IAudioPlayback playback = _mainThread == null
                    ? _backend.Play(entry.Clip, request, effective, () => result?.Stop())
                    : await _mainThread.RunAsync(() => _backend.Play(entry.Clip, request, effective, () => result?.Stop()));
                if (playback == null) throw new InvalidOperationException("Audio backend returned null playback.");
                result = new AudioPlaybackHandle(this, entry, playback);
                _active[request.Bus].Add(result);
                return result;
            }
            catch
            {
                if (held) ReleaseClipHolder(entry);
                throw;
            }
            finally
            {
                if (Interlocked.Decrement(ref _pendingOperations) == 0) _operationSignal.TrySetResult(true);
            }
        }

        public async Task<AudioPlaybackHandle> PlayBgmAsync(string key, float volume = 1, CancellationToken token = default(CancellationToken))
        {
            long request = Interlocked.Increment(ref _bgmRequest);
            _bgm?.Stop(); _bgm = null;
            AudioPlaybackHandle next = await PlayAsync(key, new AudioPlayRequest(AudioBus.Bgm, volume, true), token);
            if (request != Volatile.Read(ref _bgmRequest)) { next.Stop(); return null; }
            _bgm = next; return next;
        }

        internal void ReleasePlayback(AudioEntry entry, IAudioPlayback playback)
        {
            foreach (var pair in _active) pair.Value.RemoveAll(x => x == null || !x.IsPlaying);
            Action dispose = () =>
            {
                try { playback?.Dispose(); } catch (Exception error) { _diagnostics.Error("Playback dispose failed", error); }
            };
            if (_mainThread != null && !_mainThread.IsMainThread)
            {
                Interlocked.Increment(ref _pendingOperations);
                _ = ReleasePlaybackOnMainAsync(entry, dispose);
                return;
            }
            dispose();
            ReleaseClipHolder(entry);
        }

        private async Task ReleasePlaybackOnMainAsync(AudioEntry entry, Action dispose)
        {
            try { await _mainThread.RunAsync(dispose); }
            catch (Exception error) { _diagnostics.Error("Playback main-thread cleanup failed", error); }
            finally { ReleaseClipHolder(entry); }
            if (Interlocked.Decrement(ref _pendingOperations) == 0) _operationSignal.TrySetResult(true);
        }

        private AudioEntry GetOrCreateEntry(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Audio key is required.", nameof(key));
            lock (_gate)
            {
                EnsureUsable();
                if (_clips.TryGetValue(key, out var existing)) { existing.Holders++; return existing; }
                Task<AudioBackendClip> load = _backend.LoadClipAsync(key.Trim(), CancellationToken.None);
                var entry = new AudioEntry(key.Trim(), load) { Holders = 1 }; _clips.Add(entry.Key, entry); _ = ObserveClip(entry); return entry;
            }
        }

        private async Task ObserveClip(AudioEntry entry)
        {
            try { entry.Clip = await entry.Load.ConfigureAwait(false); }
            catch (Exception error) { _diagnostics.Error("Audio clip load failed: " + entry.Key, error); lock (_gate) { if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key); } }
            if (entry.Holders == 0 && entry.Clip != null) ReleaseClipHolder(entry);
        }

        private void ReleaseClipHolder(AudioEntry entry)
        {
            lock (_gate)
            {
                if (entry.Holders > 0) entry.Holders--;
                if (entry.Holders == 0 && entry.Load.IsCompleted)
                {
                    entry.Removed = true; if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key);
                    if (!entry.Released) { entry.Released = true; entry.Clip?.Release(); }
                }
                else if (entry.Holders == 0 && !entry.Load.IsCompleted)
                {
                    entry.Removed = true; if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key);
                }
            }
        }

        private void EnforceLimit(AudioBus bus)
        {
            List<AudioPlaybackHandle> list = _active[bus];
            while (list.Count >= _limits[bus]) list[0].Stop();
        }
        private static float Clamp(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value));
        private static async Task<bool> WaitForCompletion(Task operation, CancellationToken token)
        {
            if (!token.CanBeCanceled) { await operation.ConfigureAwait(false); return true; }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true))) return ReferenceEquals(await Task.WhenAny(operation, canceled.Task).ConfigureAwait(false), operation);
        }
        private void EnsureUsable() { if (_disposed) throw new ObjectDisposedException(nameof(AudioService)); }

        public void Tick()
        {
            if (_disposed) return;
            _backend.Tick();
            foreach (var pair in _active)
                for (int i = pair.Value.Count - 1; i >= 0; i--)
                    if (!pair.Value[i].IsPlaying) pair.Value[i].Stop();
        }
        public Task CloseAsync()
        {
            if (_closeTask != null) return _closeTask;
            _disposed = true; Interlocked.Increment(ref _bgmRequest);
            _closeTask = CloseCoreAsync();
            return _closeTask;
        }

        private async Task CloseCoreAsync()
        {
            foreach (var pair in _active) for (int i = pair.Value.Count - 1; i >= 0; i--) pair.Value[i]?.Stop();
            Task[] loads;
            lock (_gate)
            {
                var list = new List<Task>();
                foreach (var pair in _clips) list.Add(pair.Value.Load);
                loads = list.ToArray();
            }
            for (int i = 0; i < loads.Length; i++) { try { await loads[i]; } catch { } }
            while (Volatile.Read(ref _pendingOperations) > 0)
            {
                Task signal = _operationSignal.Task;
                await signal;
                if (Volatile.Read(ref _pendingOperations) > 0) _operationSignal = NewSignal();
            }
            foreach (var pair in _clips) if (pair.Value.Holders == 0) pair.Value.Clip?.Release();
            _clips.Clear(); _backend.Dispose();
        }

        private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() { _ = CloseAsync(); }
    }
}
