using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using WFrameWork.Core.ResLoad;
using WFrameWork.Core.ResLoad.Unity;
using WFrameWork.Pool.Unity;
using WFrameWork.Scene;
using WFrameWork.Scene.Unity;
using WFrameWork.Threading.Unity;

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

        [UnityTest]
        public IEnumerator FrameworkResourceServiceAwaitsAsyncLeaseRelease()
        {
            var dispatcher = new UnityMainThreadDispatcher();
            var service = new AddressablesResourceService(mainThread: dispatcher);
            var initialize = service.InitializeAsync();
            yield return new WaitUntil(() => initialize.IsCompleted);
            Assert.That(initialize.Exception, Is.Null, "Run the Combined sample generator and Build Player Content first.");
            var load = service.Service.LoadAssetAsync<GameObject>("GFramework.Samples.Settings");
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Exception, Is.Null);
            var lease = load.Result;
            var close = service.CloseAsync();
            Assert.That(close.IsCompleted, Is.False, "Resource service must wait for the external lease.");
            var release = lease.DisposeAsync();
            yield return new WaitUntil(() => release.IsCompleted && close.IsCompleted);
            Assert.That(release.Exception, Is.Null);
            Assert.That(close.Exception, Is.Null);
        }

        [UnityTest]
        public IEnumerator FrameworkSceneFlowAwaitsGeneratedSceneUnload()
        {
            var dispatcher = new UnityMainThreadDispatcher();
            var flow = new SceneFlowService(new AddressablesSceneBackend(dispatcher));
            var load = flow.LoadAsync("GFramework.Samples.Game", SceneLoadMode.Additive);
            yield return new WaitUntil(() => load.IsCompleted);
            Assert.That(load.Exception, Is.Null, "Run the Combined sample generator and Build Player Content first.");
            var unload = flow.UnloadAsync(load.Result);
            yield return new WaitUntil(() => unload.IsCompleted);
            Assert.That(unload.Exception, Is.Null);
            var close = flow.ShutdownAsync();
            yield return new WaitUntil(() => close.IsCompleted);
            Assert.That(close.Exception, Is.Null);
        }

        [UnityTest]
        public IEnumerator FrameworkCanceledSceneLoadDrainsBeforeShutdown()
        {
            int sceneCountBefore = SceneManager.sceneCount;
            var flow = new SceneFlowService(new AddressablesSceneBackend(new UnityMainThreadDispatcher()));
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    var load = flow.LoadAsync("GFramework.Samples.Game", SceneLoadMode.Additive, token: cancellation.Token);
                    cancellation.Cancel();
                    var shutdown = flow.ShutdownAsync();
                    yield return WaitForTask(shutdown);
                    Assert.That(shutdown.Exception, Is.Null);
                    Assert.That(load.IsCompleted, Is.True);
                    Assert.That(load.IsFaulted, Is.False, "Build the generated local Addressables content before this test.");
                    Assert.That(flow.LoadedSceneCount, Is.Zero);
                    Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCountBefore), "Shutdown must include late native scene unloading.");
                }
                finally { flow.Dispose(); }
            }
        }

        private static IEnumerator WaitForTask(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(task.IsCompleted, Is.True, "Framework operation timed out.");
        }

        [UnityTest]
        public IEnumerator FrameworkPoolCloseWaitsForDestroyedInstances()
        {
            var dispatcher = new UnityMainThreadDispatcher();
            var service = new AddressablesResourceService(mainThread: dispatcher);
            var initialize = service.InitializeAsync();
            yield return new WaitUntil(() => initialize.IsCompleted);
            Assert.That(initialize.Exception, Is.Null, "Run the Combined sample generator and Build Player Content first.");
            var pool = new AddressableGameObjectPool(service.Service, "GFramework.Samples.Player", mainThread: dispatcher);
            var warmup = pool.WarmupAsync(1);
            yield return new WaitUntil(() => warmup.IsCompleted);
            Assert.That(warmup.Exception, Is.Null);
            var item = pool.Rent();
            Assert.That(item, Is.Not.Null);
            var close = pool.CloseAsync();
            yield return new WaitUntil(() => close.IsCompleted);
            Assert.That(close.Exception, Is.Null);
            var serviceClose = service.CloseAsync();
            yield return new WaitUntil(() => serviceClose.IsCompleted);
            Assert.That(serviceClose.Exception, Is.Null);
        }
    }
}
