using System;
using System.Collections.Generic;
using UnityEngine;

namespace WFrameWork.Physics.Unity
{
    [DisallowMultipleComponent]
    public sealed class PhysicsCollisionBridge2D : MonoBehaviour
    {
        private sealed class Subscription : IDisposable
        {
            private Action _remove;
            internal Subscription(Action remove) { _remove = remove; }
            public void Dispose() { var remove = _remove; _remove = null; remove?.Invoke(); }
        }

        private readonly List<Action<Collision2D>> _enter = new List<Action<Collision2D>>();
        private readonly List<Action<Collision2D>> _stay = new List<Action<Collision2D>>();
        private readonly List<Action<Collision2D>> _exit = new List<Action<Collision2D>>();
        private readonly List<Action<Collider2D>> _triggerEnter = new List<Action<Collider2D>>();
        private readonly List<Action<Collider2D>> _triggerStay = new List<Action<Collider2D>>();
        private readonly List<Action<Collider2D>> _triggerExit = new List<Action<Collider2D>>();

        public IDisposable SubscribeCollisionEnter(Action<Collision2D> callback) => Add(_enter, callback);
        public IDisposable SubscribeCollisionStay(Action<Collision2D> callback) => Add(_stay, callback);
        public IDisposable SubscribeCollisionExit(Action<Collision2D> callback) => Add(_exit, callback);
        public IDisposable SubscribeTriggerEnter(Action<Collider2D> callback) => Add(_triggerEnter, callback);
        public IDisposable SubscribeTriggerStay(Action<Collider2D> callback) => Add(_triggerStay, callback);
        public IDisposable SubscribeTriggerExit(Action<Collider2D> callback) => Add(_triggerExit, callback);

        private void OnCollisionEnter2D(Collision2D collision) => Dispatch(_enter, collision);
        private void OnCollisionStay2D(Collision2D collision) => Dispatch(_stay, collision);
        private void OnCollisionExit2D(Collision2D collision) => Dispatch(_exit, collision);
        private void OnTriggerEnter2D(Collider2D collider) => Dispatch(_triggerEnter, collider);
        private void OnTriggerStay2D(Collider2D collider) => Dispatch(_triggerStay, collider);
        private void OnTriggerExit2D(Collider2D collider) => Dispatch(_triggerExit, collider);

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

