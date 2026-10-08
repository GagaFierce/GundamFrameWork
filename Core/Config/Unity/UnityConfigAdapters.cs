using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Config;
using WFrameWork.Core.ResLoad;

namespace WFrameWork.Config.Unity
{
    public sealed class UnityTextFileStore : ITextFileStore
    {
        public bool Exists(string path) => System.IO.File.Exists(path);
        public string Read(string path) => System.IO.File.ReadAllText(path);
        public void Write(string path, string contents) => System.IO.File.WriteAllText(path, contents);
        public void Delete(string path) { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        public void Replace(string sourcePath, string destinationPath, string backupPath)
        {
            if (System.IO.File.Exists(destinationPath)) System.IO.File.Replace(sourcePath, destinationPath, backupPath, true);
            else System.IO.File.Move(sourcePath, destinationPath);
        }
    }

    public sealed class JsonUtilitySerializer<T> : IUserDataSerializer<T> where T : new()
    {
        [Serializable] private sealed class Envelope { public int version; public T data; }
        private readonly Func<T> _default;
        private readonly Func<T, int, int, T> _migrate;
        public JsonUtilitySerializer(Func<T> defaultFactory = null, Func<T, int, int, T> migrate = null) { _default = defaultFactory; _migrate = migrate; }
        public string Serialize(T value, int version) => JsonUtility.ToJson(new Envelope { version = version, data = value });
        public T Deserialize(string text, out int version)
        { var value = JsonUtility.FromJson<Envelope>(text); if (value == null || object.Equals(value.data, null)) throw new InvalidOperationException("Invalid save data."); version = value.version; return value.data; }
        public T CreateDefault() => _default != null ? _default() : new T();
        public T Migrate(T value, int fromVersion, int currentVersion) => _migrate == null ? value : _migrate(value, fromVersion, currentVersion);
    }

    public sealed class AddressableConfigService
    {
        private readonly ResourceService _resources;
        public AddressableConfigService(ResourceService resources) { _resources = resources ?? throw new ArgumentNullException(nameof(resources)); }
        public async Task<ResourceLease<T>> LoadAsync<T>(string key, CancellationToken token = default(CancellationToken), Action<T> validate = null) where T : class
        {
            ResourceLease<T> lease = await _resources.LoadAssetAsync<T>(key, cancellationToken: token);
            try { validate?.Invoke(lease.Asset); return lease; }
            catch { lease.Dispose(); throw; }
        }
    }
}
