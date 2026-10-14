using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFrameWork.Physics.Unity
{
    [DisallowMultipleComponent]
    public sealed class PhysicsCollisionBridge3D : MonoBehaviour
    {
        private sealed class Subscription : IDisposable
        {
            private Action _remove;
            internal Subscription(Action remove) { _remove = remove; }
            public void Dispose() { var remove = _remove; _remove = null; remove?.Invoke(); }
        }

        private readonly List<Action<Collision>> _enter = new List<Action<Collision>>();
        private readonly List<Action<Collision>> _stay = new List<Action<Collision>>();
        private readonly List<Action<Collision>> _exit = new List<Action<Collision>>();
        private readonly List<Action<Collider>> _triggerEnter = new List<Action<Collider>>();
        private readonly List<Action<Collider>> _triggerStay = new List<Action<Collider>>();
        private readonly List<Action<Collider>> _triggerExit = new List<Action<Collider>>();

        public IDisposable SubscribeCollisionEnter(Action<Collision> callback) => Add(_enter, callback);
        public IDisposable SubscribeCollisionStay(Action<Collision> callback) => Add(_stay, callback);
        public IDisposable SubscribeCollisionExit(Action<Collision> callback) => Add(_exit, callback);
        public IDisposable SubscribeTriggerEnter(Action<Collider> callback) => Add(_triggerEnter, callback);
        public IDisposable SubscribeTriggerStay(Action<Collider> callback) => Add(_triggerStay, callback);
        public IDisposable SubscribeTriggerExit(Action<Collider> callback) => Add(_triggerExit, callback);

        private void OnCollisionEnter(Collision collision) => Dispatch(_enter, collision);
        private void OnCollisionStay(Collision collision) => Dispatch(_stay, collision);
        private void OnCollisionExit(Collision collision) => Dispatch(_exit, collision);
        private void OnTriggerEnter(Collider collider) => Dispatch(_triggerEnter, collider);
        private void OnTriggerStay(Collider collider) => Dispatch(_triggerStay, collider);
        private void OnTriggerExit(Collider collider) => Dispatch(_triggerExit, collider);

        private static IDisposable Add<T>(List<Action<T>> list, Action<T> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            list.Add(callback);
            return new Subscription(() => list.Remove(callback));
        }

        private static void Dispatch<T>(List<Action<T>> list, T payload)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                try { list[i](payload); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private void OnDestroy()
        {
            _enter.Clear(); _stay.Clear(); _exit.Clear();
            _triggerEnter.Clear(); _triggerStay.Clear(); _triggerExit.Clear();
        }
    }
}

