using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace WFrameWork.UI.Unity
{
    /// <summary>
    /// Migration-only Resources provider. New UI code must use AddressablesUiResourceProvider;
    /// this type is kept to preserve an existing public entry during address migration.
    /// </summary>
    [Obsolete("Use AddressablesUiResourceProvider and an explicit Addressables address.", false)]
    public sealed class ResourcesUiResourceProvider : IUiResourceProvider
    {
        public Task<UiResourceHandle> LoadAsync(string resourceKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) throw new ArgumentException("Resource key is required.", nameof(resourceKey));
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<UiResourceHandle>(cancellationToken);
            var request = Resources.LoadAsync<GameObject>(resourceKey);
            var completion = new TaskCompletionSource<UiResourceHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            int canceled = 0;
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);
            if (cancellationToken.CanBeCanceled)
                registration = cancellationToken.Register(() => { Interlocked.Exchange(ref canceled, 1); completion.TrySetCanceled(cancellationToken); });
            request.completed += _ =>
            {
                registration.Dispose();
                if (request.asset == null)
                { completion.TrySetException(new InvalidOperationException("UI resource was not found: " + resourceKey)); return; }
                if (Volatile.Read(ref canceled) != 0 || cancellationToken.IsCancellationRequested)
                { completion.TrySetCanceled(cancellationToken); return; }
                completion.TrySetResult(new UiResourceHandle(request.asset));
            };
            return completion.Task;
        }
    }
}
