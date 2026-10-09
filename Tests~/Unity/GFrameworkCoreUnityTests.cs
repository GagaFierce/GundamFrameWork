using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WFrameWork.Application;
using WFrameWork.Application.Unity;
using WFrameWork.Input;
using WFrameWork.Pool;

namespace WFrameWork.Tests.Unity
{
    public sealed class GFrameworkCoreUnityTests
    {
        [UnityTest]
        public IEnumerator UnityGameRuntimeInitializesAndShutsDown()
        {
            var runtime = UnityGameRuntime.Create();
            var initialize = runtime.Runtime.InitializeAsync();
            yield return new WaitUntil(() => initialize.IsCompleted);
            Assert.That(initialize.Exception, Is.Null);
            Assert.That(runtime.Runtime.State, Is.EqualTo(GameRuntimeState.Running));
            var shutdown = runtime.ShutdownAsync();
            yield return new WaitUntil(() => shutdown.IsCompleted);
            Assert.That(shutdown.Exception, Is.Null);
            Assert.That(runtime.Runtime.State, Is.EqualTo(GameRuntimeState.Stopped));
        }

        [Test]
        public void ClearInputDoesNotReplayPressedEvent()
        {
            var backend = new InjectedInputBackend(); using (var input = new InputService(backend)) using (var reader = input.CreateFixedEventReader())
            {
                var id = new InputActionId("Jump"); input.RegisterContext("Gameplay"); input.RegisterAction(new InputActionDefinition(id, InputActionType.Button));
                backend.SetButton(id, true); input.Update(1); input.ClearInput(); Assert.That(reader.TryRead(2, out var item), Is.True); Assert.That(item.Phase, Is.EqualTo(InputFixedEventPhase.Canceled));
            }
        }

        [Test]
        public void PoolRejectsForeignReturn()
        {
            using (var pool = new ObjectPool<object>(() => new object(), maxCapacity: 1))
            { var item = pool.Rent(); Assert.That(pool.TryReturn(new object()), Is.False); Assert.That(pool.TryReturn(item), Is.True); Assert.That(pool.TryReturn(item), Is.False); }
        }
    }
}
