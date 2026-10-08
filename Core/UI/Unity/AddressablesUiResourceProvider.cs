using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WFrameWork.Core.ResLoad;

namespace WFrameWork.UI.Unity
{
    public sealed class AddressablesUiResourceProvider : IUiResourceProvider
    {
        private readonly ResourceService _resources;
        private readonly Task _initialization;
        public AddressablesUiResourceProvider(ResourceService resources, Task initialization = null)
        { _resources = resources ?? throw new ArgumentNullException(nameof(resources)); _initialization = initialization; }

        public async Task<UiResourceHandle> LoadAsync(string resourceKey, CancellationToken cancellationToken)
        {
            if (_initialization != null) await _initialization;
            ResourceLease<GameObject> lease = await _resources.LoadAssetAsync<GameObject>(resourceKey, cancellationToken: cancellationToken);
            return new UiResourceHandle(lease.Asset, lease.Dispose);
        }
    }
}
