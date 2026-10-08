using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WFrameWork.Core.ResLoad;
using WFrameWork.Core.ResLoad.Unity;

namespace WFrameWork.Tests.Unity
{
    public sealed class GFrameworkAddressablesPlayModeTests
    {
        [UnityTest]
        public IEnumerator FrameworkResourceServiceInitializesAndCloses()
        {
            var service = new AddressablesResourceService();
            var initialize = service.InitializeAsync();
            yield return new WaitUntil(() => initialize.IsCompleted);
            Assert.That(initialize.Exception, Is.Null, "Configure Addressables Settings and build local content before running this test.");
            var close = service.CloseAsync();
            yield return new WaitUntil(() => close.IsCompleted);
            Assert.That(close.Exception, Is.Null);
        }

        [UnityTest]
        public IEnumerator FrameworkResourceServiceLoadsAndReleasesGeneratedSettingsPrefab()
        {
            var service = new AddressablesResourceService();
            var initialize = service.InitializeAsync();
            yield return new WaitUntil(() => initialize.IsCompleted);
            Assert.That(initialize.Exception, Is.Null, "Run the Combined sample generator and Build Player Content first.");
            var load = service.Service.LoadAssetAsync<GameObject>("GFramework.Samples.Settings");
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Exception, Is.Null, "Run the Combined sample generator and Build Player Content first.");
            var lease = load.Result;
            Assert.That(lease.Asset, Is.Not.Null);
            lease.Dispose();
            var close = service.CloseAsync();
            yield return new WaitUntil(() => close.IsCompleted);
            Assert.That(close.Exception, Is.Null);
        }
    }
}
