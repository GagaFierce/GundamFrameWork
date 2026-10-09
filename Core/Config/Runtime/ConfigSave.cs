using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WFrameWork.Diagnostics;

namespace WFrameWork.Config
{
    public interface ITextFileStore
    {
        bool Exists(string path);
        string Read(string path);
        void Write(string path, string contents);
        void Delete(string path);
        void Replace(string sourcePath, string destinationPath, string backupPath);
    }

    public sealed class SystemTextFileStore : ITextFileStore
    {
        public bool Exists(string path) => File.Exists(path);
        public string Read(string path) => File.ReadAllText(path);
        public void Write(string path, string contents) => File.WriteAllText(path, contents);
        public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
        public void Replace(string sourcePath, string destinationPath, string backupPath)
        {
            if (File.Exists(destinationPath)) File.Replace(sourcePath, destinationPath, backupPath, true);
            else File.Move(sourcePath, destinationPath);
        }
    }

    public interface IUserDataSerializer<T>
    {
        string Serialize(T value, int version);
        T Deserialize(string text, out int version);
        T CreateDefault();
        T Migrate(T value, int fromVersion, int currentVersion);
    }

    public sealed class SaveResult<T>
    {
        public T Value { get; }
        public bool UsedBackup { get; }
        public bool UsedDefault { get; }
        public int Version { get; }
        internal SaveResult(T value, bool usedBackup, bool usedDefault, int version)
        { Value = value; UsedBackup = usedBackup; UsedDefault = usedDefault; Version = version; }
    }

    public sealed class SaveService<T> : IDisposable
    {
        private readonly ITextFileStore _files;
        private readonly IUserDataSerializer<T> _serializer;
        private readonly DiagnosticLogger _diagnostics;
        private readonly Dictionary<string, SemaphoreSlim> _locks = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private readonly int _version;
        private readonly string _rootPath;
        private readonly Action<T> _validate;
        private bool _disposed;
        private Task _closeTask;
        private int _pendingOperations;
        private TaskCompletionSource<bool> _operationSignal = NewSignal();

        public SaveService(ITextFileStore files, IUserDataSerializer<T> serializer, int currentVersion, IDiagnosticSink diagnostics = null, string rootPath = null, Action<T> validate = null)
        {
            _files = files ?? throw new ArgumentNullException(nameof(files)); _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            if (currentVersion < 0) throw new ArgumentOutOfRangeException(nameof(currentVersion));
            _version = currentVersion;
            _rootPath = string.IsNullOrWhiteSpace(rootPath) ? null : Path.GetFullPath(rootPath.Trim());
            _validate = validate; _diagnostics = new DiagnosticLogger("Config.Save", diagnostics);
        }

        public async Task<SaveResult<T>> LoadAsync(string key, CancellationToken token = default(CancellationToken))
        {
            string path = Normalize(key); BeginOperation(); SemaphoreSlim gate = GetLock(path); bool entered = false;
            try
            {
                await gate.WaitAsync(token); entered = true;
                bool backup = false; bool defaultValue = false; T value;
                try { value = ReadAndMigrate(path); }
                catch (Exception primary)
                {
                    _diagnostics.Warning("Save read failed, trying backup: " + path, primary);
                    try { value = ReadAndMigrate(path + ".bak"); backup = true; }
                    catch (Exception backupError)
                    {
                        _diagnostics.Warning("Backup read failed, using defaults: " + path, backupError);
                        value = _serializer.CreateDefault(); defaultValue = true;
                    }
                }
                return new SaveResult<T>(value, backup, defaultValue, _version);
            }
            finally { if (entered) gate.Release(); EndOperation(); }
        }

        public async Task SaveAsync(string key, T value, CancellationToken token = default(CancellationToken))
        {
            string path = Normalize(key); BeginOperation(); SemaphoreSlim gate = GetLock(path); bool entered = false;
            string temp = null; bool tempOwned = false;
            try
            {
                await gate.WaitAsync(token); entered = true;
                string directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                temp = path + ".tmp." + Guid.NewGuid().ToString("N");
                tempOwned = true;
                _validate?.Invoke(value); _files.Write(temp, _serializer.Serialize(value, _version));
                _files.Replace(temp, path, path + ".bak");
                tempOwned = false;
            }
            catch (Exception error)
            {
                _diagnostics.Error("Save write failed: " + path, error);
                if (tempOwned && temp != null) { try { _files.Delete(temp); } catch { } }
                throw;
            }
            finally { if (entered) gate.Release(); EndOperation(); }
        }

        private T ReadAndMigrate(string path)
        {
            string text = _files.Read(path); int version; T value = _serializer.Deserialize(text, out version);
            if (version < _version) value = _serializer.Migrate(value, version, _version);
            if (version > _version) throw new InvalidDataException("Save version is newer than this package.");
            _validate?.Invoke(value);
            return value;
        }

        private SemaphoreSlim GetLock(string path)
        {
            lock (_gate) { if (!_locks.TryGetValue(path, out var gate)) _locks.Add(path, gate = new SemaphoreSlim(1, 1)); return gate; }
        }
        private string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Save key is required.", nameof(key));
            string relative = key.Trim();
            if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.StartsWith("\\\\", StringComparison.Ordinal) || relative.IndexOf('\0') >= 0)
                throw new ArgumentException("Save key must be a relative name.", nameof(key));
            string[] segments = relative.Split(new[] { '/', '\\' }, StringSplitOptions.None);
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == ".." ||
                    segments[i].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReservedWindowsName(segments[i]))
                    throw new ArgumentException("Save key contains an invalid path segment.", nameof(key));
            }
            string path;
            try { path = Path.GetFullPath(_rootPath == null ? relative : Path.Combine(_rootPath, relative)); }
            catch (Exception error) { throw new ArgumentException("Save key is not a valid path.", nameof(key), error); }
            if (_rootPath != null)
            {
                string rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                    _rootPath.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                    ? _rootPath : _rootPath + Path.DirectorySeparatorChar;
                StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!path.StartsWith(rootPrefix, comparison)) throw new ArgumentException("Save key must stay under the configured root.", nameof(key));
            }
            return path;
        }

        private static bool IsReservedWindowsName(string segment)
        {
            string name = segment.TrimEnd(' ', '.');
            int dot = name.IndexOf('.'); if (dot >= 0) name = name.Substring(0, dot);
            if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                name[3] >= '1' && name[3] <= '9') return true;
            return false;
        }
        private void BeginOperation()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(SaveService<T>));
                Interlocked.Increment(ref _pendingOperations);
            }
        }
        public Task CloseAsync()
        {
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _disposed = true; _closeTask = CloseCoreAsync(); return _closeTask;
            }
        }

        private async Task CloseCoreAsync()
        {
            while (Volatile.Read(ref _pendingOperations) > 0)
            {
                Task signal = _operationSignal.Task;
                await signal;
                if (Volatile.Read(ref _pendingOperations) > 0) _operationSignal = NewSignal();
            }
            lock (_gate) { foreach (var pair in _locks) pair.Value.Dispose(); _locks.Clear(); }
        }

        private void EndOperation()
        { if (Interlocked.Decrement(ref _pendingOperations) == 0) _operationSignal.TrySetResult(true); }

        private static TaskCompletionSource<bool> NewSignal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() { _ = CloseAsync(); }
    }
}
