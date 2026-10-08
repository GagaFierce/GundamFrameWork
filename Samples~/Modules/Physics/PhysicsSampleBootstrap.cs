using UnityEngine;
using WFrameWork.Core.FrameUpdate;
using WFrameWork.Core.FrameUpdate.Unity;
using WFrameWork.Physics;
using WFrameWork.Physics.Unity;

namespace WFrameWork.Samples.Physics
{
    public sealed class PhysicsSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private RigidbodyFixedStepBehaviour mover;
        [SerializeField] private Transform rayOrigin;
        private FrameUpdateManager _manager;
        private UnityFrameUpdateHost _host;
        private PhysicsStepDispatcher _dispatcher;
        private PhysicsQueryService3D _queries;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private void Awake()
        {
            _manager = new FrameUpdateManager(FrameUpdateConfig.Default);
            _host = UnityFrameUpdateHost.Install(_manager);
            _dispatcher = new PhysicsStepDispatcher(_manager, _host.Loops.Physics);
            if (mover != null) _dispatcher.Register(mover);
            _queries = new PhysicsQueryService3D();
        }

        private void Update()
        {
            if (rayOrigin == null) return;
            var filter = PhysicsQueryFilter3D.Default;
            filter.SortByDistance = true;
            var result = _queries.Raycast(new Ray(rayOrigin.position, rayOrigin.forward), 50, in filter, _hits);
            if (result.HasHits) Debug.DrawLine(rayOrigin.position, _hits[0].point, Color.yellow);
        }

        private void OnDestroy()
        {
            _dispatcher?.Dispose(); _host?.Dispose(); _manager?.Dispose();
        }
    }
}

