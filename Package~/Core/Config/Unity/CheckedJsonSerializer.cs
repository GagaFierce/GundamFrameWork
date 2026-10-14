using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace WFrameWork.Config.Unity
{
    /// <summary>Versioned JSON with corruption detection, retaining SaveService's atomic writes and backups.</summary>
    public sealed class CheckedJsonSerializer<T> : IUserDataSerializer<T> where T : class, new()
    {
        [Serializable] private sealed class Envelope { public int version; public string payload, checksum; }
        private readonly Func<T> _factory;
        public CheckedJsonSerializer(Func<T> factory = null) { _factory = factory ?? (() => new T()); }
        private static string Digest(string value)
        { using (var algorithm = SHA256.Create()) return Convert.ToBase64String(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value))); }
        public string Serialize(T value, int version)
        { string payload = JsonUtility.ToJson(value); return JsonUtility.ToJson(new Envelope { version = version, payload = payload, checksum = Digest(version + "/" + payload) }); }
        public T Deserialize(string text, out int version)
        {
            var envelope = JsonUtility.FromJson<Envelope>(text);
            if (envelope == null || envelope.payload == null || envelope.checksum != Digest(envelope.version + "/" + envelope.payload)) throw new InvalidDataException("Save checksum mismatch.");
            version = envelope.version; var result = JsonUtility.FromJson<T>(envelope.payload); if (result == null) throw new InvalidDataException("Empty save payload."); return result;
        }
        public T CreateDefault() => _factory();
        public T Migrate(T value, int fromVersion, int currentVersion)
        { if (fromVersion != currentVersion) throw new InvalidDataException("No compatible migration is registered."); return value; }
    }
}
