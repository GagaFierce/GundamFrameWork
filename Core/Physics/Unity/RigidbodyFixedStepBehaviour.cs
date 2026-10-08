using UnityEngine;
using WFrameWork.Physics;

namespace WFrameWork.Physics.Unity
{
    /// <summary>Example business motion; registering it still requires an injected PhysicsStepDispatcher.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidbodyFixedStepBehaviour : MonoBehaviour, IPhysicsFixedStepParticipant
    {
        [Tooltip("World-space velocity applied through MovePosition on each framework Physics step.")]
        public Vector3 Velocity;
        [Tooltip("When true, only kinematic-style MovePosition is used. The framework never calls Physics.Simulate.")]
        public bool UseMovePosition = true;

        private Rigidbody _body;
        private void Awake() { _body = GetComponent<Rigidbody>(); }

        public void OnPhysicsStep(in PhysicsStepContext context)
        {
            if (_body == null || context.DeltaTime <= 0) return;
            if (UseMovePosition || _body.isKinematic)
                _body.MovePosition(_body.position + Velocity * (float)context.DeltaTime);
            else
                _body.AddForce(Velocity, ForceMode.Acceleration);
        }
    }
}

