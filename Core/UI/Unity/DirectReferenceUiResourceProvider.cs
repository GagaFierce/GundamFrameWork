using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WFrameWork.UI.Unity
{
    /// <summary>Loads UI prefabs from explicit inspector references without a global asset loader.</summary>
    [DisallowMultipleComponent]
    public sealed class DirectReferenceUiResourceProvider : MonoBehaviour, IUiResourceProvider
    {
        [Serializable]
        private sealed class Entry
        {
            public string key;
            public GameObject prefab;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public void Configure(string key, GameObject prefab)
        {
            if (string.IsNullOrWhiteSpace(key) || prefab == null) throw new ArgumentException("A resource key and prefab are required.");
            entries = new[] { new Entry { key = key, prefab = prefab } };
        }

        public Task<UiResourceHandle> LoadAsync(string resourceKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(resourceKey)) throw new ArgumentException("UI resource key is required.", nameof(resourceKey));
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if (entry == null || !string.Equals(entry.key, resourceKey, StringComparison.Ordinal)) continue;
                if (entry.prefab == null) throw new InvalidOperationException("The UI prefab reference is missing for '" + resourceKey + "'.");
                return Task.FromResult(new UiResourceHandle(entry.prefab));
            }
            throw new InvalidOperationException("No serialized UI prefab reference is registered for '" + resourceKey + "'.");
        }
    }
}
