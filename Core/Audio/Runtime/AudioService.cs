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
        private readonly object _gate = new object();
        private Func<Task> _release;
        private Task _releaseTask;
        public object Clip { get; }
        public AudioBackendClip(object clip, Action release) : this(clip, () => { release?.Invoke(); return Task.CompletedTask; }, true) { }
        public AudioBackendClip(object clip, Func<Task> release, bool asynchronousRelease) { Clip = clip ?? throw new ArgumentNullException(nameof(clip)); _release = release; }
        public bool IsReleased { get { lock (_gate) return _releaseTask != null; } }
        public void Release() { _ = ReleaseAsync(); }
        public Task ReleaseAsync()
        {
            lock (_gate)
            {
                if (_releaseTask != null) return _releaseTask;
                var release = _release; _release = null;
                try { _releaseTask = release == null ? Task.CompletedTask : (release() ?? Task.CompletedTask); }
                catch (Exception error) { _releaseTask = Task.FromException(error); }
                return _releaseTask;
            }
        }
    }

    public interface IAudioPlayback : IDisposable
    {
        bool IsPlaying { get; }
        void Stop();
    }

    public interface IAudioPlaybackVolume
    {
        void SetVolume(float volume);
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
        private int _stopped;
        private readonly AudioBus _bus;
        private readonly float _requestVolume;
        internal AudioPlaybackHandle(AudioService owner, AudioEntry entry, IAudioPlayback playback, AudioBus bus, float requestVolume)
        { _owner = owner; _entry = entry; _playback = playback; _bus = bus; _requestVolume = requestVolume; }
        internal IAudioPlayback Playback => _playback;
        internal AudioBus Bus => _bus;
        internal float RequestVolume => _requestVolume;
        internal void ApplyVolume(float effective)
        { if (_playback is IAudioPlaybackVolume volume) volume.SetVolume(effective); }
        public bool IsPlaying => Volatile.Read(ref _stopped) == 0 && _playback != null && _playback.IsPlaying;
        public void Stop() { _ = StopAsync(); }
        public Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return Task.CompletedTask;
            IAudioPlayback playback = _playback; _playback = null;
            return _owner.ReleasePlayback(_entry, playback);
        }
        public void Dispose() => Stop();
    }

    internal sealed class AudioEntry
    {
        internal readonly string Key; internal readonly Task<AudioBackendClip> Load;
        internal AudioBackendClip Clip; internal int Holders; internal bool Removed; internal bool Released; internal Task ReleaseTask;
        internal AudioEntry(string key, Task<AudioBackendClip> load) { Key = key; Load = load; }
    }

    internal sealed class StaleAudioRequestException : OperationCanceledException
    {
        internal StaleAudioRequestException() : base("The audio request was superseded before playback was committed.") { }
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
        private readonly object _playbackCommitGate = new object();
        private long _bgmRequest;
        private AudioPlaybackHandle _bgm;
        private bool _disposed;
        private bool _muted;
        private Task _closeTask;
        private int _pendingOperations;
        private TaskCompletionSource<bool> _operationSignal = NewSignal();
        private readonly HashSet<Task> _pendingReleases = new HashSet<Task>();
        private readonly List<Exception> _cleanupFailures = new List<Exception>();
        private const int MaxRememberedCleanupFailures = 32;
        private TaskCompletionSource<bool> _releaseSignal = NewSignal();

        public AudioService(IAudioBackend backend, IDiagnosticSink diagnostics = null, IMainThreadDispatcher mainThread = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _diagnostics = new DiagnosticLogger("Audio", diagnostics);
            _mainThread = mainThread;
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus))) { _volume[bus] = 1; _limits[bus] = bus == AudioBus.Bgm ? 1 : 16; _active[bus] = new List<AudioPlaybackHandle>(); }
        }
        public bool IsMuted { get { lock (_gate) return _muted; } }
        public int ActivePlaybackCount { get { lock (_gate) { int count = 0; foreach (var pair in _active) count += pair.Value.Count; return count; } } }
        public int PendingOperationCount { get { lock (_gate) return _pendingOperations + _pendingReleases.Count; } }
        public float GetVolume(AudioBus bus) { lock (_gate) return _volume[bus]; }
        public void SetMuted(bool muted)
        {
            Action apply = () =>
            {
                lock (_gate) _muted = muted;
                ApplyCurrentVolumes();
            };
            DispatchOptional(apply);
        }
        public void SetVolume(AudioBus bus, float volume)
        {
            float clamped = Clamp(volume);
            DispatchOptional(() => { lock (_gate) _volume[bus] = clamped; ApplyCurrentVolumes(); });
        }
        public void SetConcurrencyLimit(AudioBus bus, int limit) { if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit)); lock (_gate) _limits[bus] = limit; }

        public async Task<AudioPlaybackHandle> PlayAsync(string key, AudioPlayRequest request, CancellationToken token = default(CancellationToken))
        { return await PlayAsyncCore(key, request, token, null).ConfigureAwait(false); }

        private async Task<AudioPlaybackHandle> PlayAsyncCore(string key, AudioPlayRequest request,
            CancellationToken token, Func<bool> commitGuard)
        {
            EnsureUsable();
            AudioEntry entry = GetOrCreateEntry(key);
            Interlocked.Increment(ref _pendingOperations);
            bool held = true;
            try
            {
                if (!await WaitForCompletion(entry.Load, token)) { await ReleaseClipHolderAsync(entry).ConfigureAwait(false); held = false; token.ThrowIfCancellationRequested(); }
                entry.Clip = await entry.Load;
                EnsureUsable();
                float effective;
                lock (_gate) effective = (_muted ? 0 : _volume[request.Bus]) * Clamp(request.Volume);
                AudioPlaybackHandle result = null;
                Func<AudioPlaybackHandle> commit = () => CommitPlayback(entry, request, effective, commitGuard);
                result = _mainThread == null ? commit() : await _mainThread.RunAsync(commit).ConfigureAwait(false);
                return result;
            }
            catch
            {
                if (held) await ReleaseClipHolderAsync(entry).ConfigureAwait(false);
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
            AudioPlaybackHandle next;
            try
            {
                next = await PlayAsyncCore(key, new AudioPlayRequest(AudioBus.Bgm, volume, true), token,
                    () => request == Volatile.Read(ref _bgmRequest) && !_disposed).ConfigureAwait(false);
            }
            catch (StaleAudioRequestException) { return null; }
            if (request != Volatile.Read(ref _bgmRequest)) { await next.StopAsync().ConfigureAwait(false); return null; }
            _bgm = next; return next;
        }

        internal Task ReleasePlayback(AudioEntry entry, IAudioPlayback playback)
        {
            lock (_gate) foreach (var pair in _active) pair.Value.RemoveAll(x => x == null || !x.IsPlaying);
            Action dispose = () =>
            {
                try { playback?.Dispose(); }
                catch (Exception error)
                {
                    _diagnostics.Error("Playback dispose failed", error);
                    lock (_gate) RememberCleanupFailureLocked(error);
                }
            };
            if (_mainThread != null && !_mainThread.IsMainThread)
            {
                Interlocked.Increment(ref _pendingOperations);
                return ReleasePlaybackOnMainAsync(entry, dispose);
            }
            dispose();
            return ReleaseClipHolderAsync(entry);
        }

        private async Task ReleasePlaybackOnMainAsync(AudioEntry entry, Action dispose)
        {
            try
            {
                try { await _mainThread.RunAsync(dispose); }
                catch (Exception error)
                {
                    _diagnostics.Error("Playback main-thread cleanup failed", error);
                    lock (_gate) RememberCleanupFailureLocked(error);
                    throw;
                }
                finally { await ReleaseClipHolderAsync(entry).ConfigureAwait(false); }
            }
            finally
            {
                // Clip release can fail too. No cleanup exception may strand this count.
                if (Interlocked.Decrement(ref _pendingOperations) == 0) _operationSignal.TrySetResult(true);
            }
        }

        private AudioPlaybackHandle CommitPlayback(AudioEntry entry, AudioPlayRequest request, float effective, Func<bool> commitGuard)
        {
            lock (_playbackCommitGate)
            {
                if (commitGuard != null && !commitGuard()) throw new StaleAudioRequestException();
                AudioPlaybackHandle result = null;
                EnforceLimit(request.Bus);
                IAudioPlayback playback = _backend.Play(entry.Clip, request, effective, () => result?.Stop());
                if (playback == null) throw new InvalidOperationException("Audio backend returned null playback.");
                result = new AudioPlaybackHandle(this, entry, playback, request.Bus, request.Volume);
                lock (_gate) _active[request.Bus].Add(result);
                return result;
            }
        }

        private void ApplyCurrentVolumes()
        {
            AudioPlaybackHandle[] handles;
            bool muted;
            lock (_gate)
            {
                muted = _muted;
                var list = new List<AudioPlaybackHandle>();
                foreach (var pair in _active) list.AddRange(pair.Value);
                handles = list.ToArray();
            }
            for (int i = 0; i < handles.Length; i++)
            {
                float busVolume;
                lock (_gate) busVolume = _volume[handles[i].Bus];
                handles[i].ApplyVolume((muted ? 0 : busVolume) * Clamp(handles[i].RequestVolume));
            }
        }

        private void DispatchOptional(Action action)
        {
            if (_mainThread == null || _mainThread.IsMainThread) { action(); return; }
            _ = _mainThread.RunAsync(action).ContinueWith(completed =>
            {
                if (completed.IsFaulted) _diagnostics.Error("Audio volume update failed.", completed.Exception);
            }, TaskScheduler.Default);
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
            lock (_gate) if (entry.Holders == 0 && entry.Clip != null) { entry.Removed = true; if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key); StartReleaseClipLocked(entry); }
        }

        private Task ReleaseClipHolderAsync(AudioEntry entry)
        {
            lock (_gate)
            {
                if (entry.Holders > 0) entry.Holders--;
                if (entry.Holders == 0 && entry.Load.IsCompleted)
                {
                    entry.Removed = true; if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key);
                    return StartReleaseClipLocked(entry);
                }
                else if (entry.Holders == 0 && !entry.Load.IsCompleted)
                {
                    entry.Removed = true; if (_clips.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry)) _clips.Remove(entry.Key);
                }
                return Task.CompletedTask;
            }
        }

        private Task StartReleaseClipLocked(AudioEntry entry)
        {
            if (entry.Released) return entry.ReleaseTask ?? Task.CompletedTask;
            entry.Released = true;
            entry.ReleaseTask = entry.Clip == null ? Task.CompletedTask : entry.Clip.ReleaseAsync();
            if (!entry.ReleaseTask.IsCompleted)
            {
                _pendingReleases.Add(entry.ReleaseTask);
                _ = entry.ReleaseTask.ContinueWith(_ =>
                {
                    lock (_gate)
                    {
                        RecordReleaseResultLocked(entry.ReleaseTask);
                        _pendingReleases.Remove(entry.ReleaseTask);
                        _releaseSignal.TrySetResult(true);
                    }
                }, TaskScheduler.Default);
            }
            else RecordReleaseResultLocked(entry.ReleaseTask);
            return entry.ReleaseTask;
        }

        private void RecordReleaseResultLocked(Task release)
        {
            if (release.IsFaulted) RememberCleanupFailureLocked(release.Exception);
            else if (release.IsCanceled) RememberCleanupFailureLocked(new TaskCanceledException("Audio clip release was canceled."));
        }

        private void RememberCleanupFailureLocked(Exception error)
        {
            if (error is AggregateException aggregate)
            {
                foreach (var inner in aggregate.Flatten().InnerExceptions) RememberCleanupFailureLocked(inner);
            }
            else if (_cleanupFailures.Count < MaxRememberedCleanupFailures && !_cleanupFailures.Contains(error))
                _cleanupFailures.Add(error);
        }

        private void EnforceLimit(AudioBus bus)
        {
            while (true)
            {
                AudioPlaybackHandle oldest;
                lock (_gate)
                {
                    if (_active[bus].Count < _limits[bus]) return;
                    oldest = _active[bus][0];
                }
                oldest.Stop();
            }
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
            List<AudioPlaybackHandle> stopped = new List<AudioPlaybackHandle>();
            lock (_gate) foreach (var pair in _active) for (int i = pair.Value.Count - 1; i >= 0; i--) if (!pair.Value[i].IsPlaying) stopped.Add(pair.Value[i]);
            for (int i = 0; i < stopped.Count; i++) stopped[i].Stop();
        }
        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _disposed = true; Interlocked.Increment(ref _bgmRequest);
                _closeTask = CloseCoreAsync();
                return _closeTask;
            }
        }

        private async Task CloseCoreAsync()
        {
            List<AudioPlaybackHandle> active = new List<AudioPlaybackHandle>();
            lock (_gate) foreach (var pair in _active) active.AddRange(pair.Value);
            for (int i = 0; i < active.Count; i++)
            {
                try { await active[i].StopAsync().ConfigureAwait(false); }
                catch (Exception error) { lock (_gate) RememberCleanupFailureLocked(error); }
            }
            Task[] loads;
            lock (_gate)
            {
                var list = new List<Task>();
                foreach (var pair in _clips) list.Add(pair.Value.Load);
                loads = list.ToArray();
            }
            for (int i = 0; i < loads.Length; i++) { try { await loads[i]; } catch { } }
            while (true)
            {
                Task signal;
                Task releaseSignal;
                lock (_gate)
                {
                    if (_operationSignal.Task.IsCompleted) _operationSignal = NewSignal();
                    if (_releaseSignal.Task.IsCompleted) _releaseSignal = NewSignal();
                    if (Volatile.Read(ref _pendingOperations) == 0 && _pendingReleases.Count == 0) break;
                    signal = _operationSignal.Task;
                    releaseSignal = _releaseSignal.Task;
                }
                await Task.WhenAny(signal, releaseSignal).ConfigureAwait(false);
            }
            List<Task> releases = new List<Task>();
            lock (_gate)
            {
                foreach (var pair in _clips) { pair.Value.Removed = true; if (pair.Value.Holders == 0) releases.Add(StartReleaseClipLocked(pair.Value)); }
                _clips.Clear();
            }
            for (int i = 0; i < releases.Count; i++)
            {
                try { await releases[i].ConfigureAwait(false); }
                catch (Exception error) { lock (_gate) RememberCleanupFailureLocked(error); }
            }
            try
            {
                if (_mainThread == null) _backend.Dispose();
                else await _mainThread.RunAsync(() => _backend.Dispose()).ConfigureAwait(false);
            }
            catch (Exception error) { lock (_gate) RememberCleanupFailureLocked(error); }
            lock (_gate)
            {
                if (_cleanupFailures.Count > 0) throw new AggregateException("Audio service close failed.", _cleanupFailures);
            }
        }

        private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() { _ = CloseAsync(); }
    }
}
