using System;
using UnityEngine;
using UnityPhysics = UnityEngine.Physics;

namespace WFrameWork.Physics.Unity
{
    public sealed class PhysicsQueryService3D
    {
        private readonly PhysicsScene _scene;

        public PhysicsQueryService3D() : this(UnityPhysics.defaultPhysicsScene) { }
        public PhysicsQueryService3D(PhysicsScene scene) { _scene = scene; }
        public PhysicsScene Scene => _scene;

        public PhysicsQueryResult Raycast(Ray ray, float maxDistance, in PhysicsQueryFilter3D filter, RaycastHit[] results)
        {
            ValidateBuffer(results); ValidateDistance(maxDistance);
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.Raycast(ray.origin, ray.direction, results, maxDistance, filter.LayerMask, filter.TriggerInteraction);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult SphereCast(Ray ray, float radius, float maxDistance,
            in PhysicsQueryFilter3D filter, RaycastHit[] results)
        {
            ValidateBuffer(results); ValidateDistance(maxDistance);
            if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.SphereCast(ray.origin, radius, ray.direction, results, maxDistance, filter.LayerMask, filter.TriggerInteraction);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction,
            Quaternion orientation, float maxDistance, in PhysicsQueryFilter3D filter, RaycastHit[] results)
        {
            ValidateBuffer(results); ValidateDistance(maxDistance);
            if (halfExtents.x < 0 || halfExtents.y < 0 || halfExtents.z < 0) throw new ArgumentOutOfRangeException(nameof(halfExtents));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.BoxCast(center, halfExtents, direction, results, orientation, maxDistance, filter.LayerMask, filter.TriggerInteraction);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult OverlapSphere(Vector3 center, float radius,
            in PhysicsQueryFilter3D filter, Collider[] results)
        {
            ValidateBuffer(results);
            if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.OverlapSphere(center, radius, results, filter.LayerMask, filter.TriggerInteraction);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation,
            in PhysicsQueryFilter3D filter, Collider[] results)
        {
            ValidateBuffer(results);
            if (halfExtents.x < 0 || halfExtents.y < 0 || halfExtents.z < 0) throw new ArgumentOutOfRangeException(nameof(halfExtents));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.OverlapBox(center, halfExtents, results, orientation, filter.LayerMask, filter.TriggerInteraction);
            return new PhysicsQueryResult(count, results.Length);
        }

        public static bool TryGetNearest(RaycastHit[] results, int count, out RaycastHit nearest)
        {
            ValidateCount(results, count);
            if (count == 0) { nearest = default(RaycastHit); return false; }
            int index = 0;
            for (int i = 1; i < count; i++) if (results[i].distance < results[index].distance) index = i;
            nearest = results[index];
            return true;
        }

        private static void Sort(RaycastHit[] results, int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit value = results[i];
                int j = i - 1;
                while (j >= 0 && results[j].distance > value.distance) { results[j + 1] = results[j]; j--; }
                results[j + 1] = value;
            }
        }

        private static void ValidateBuffer(Array buffer)
        {
            if (buffer == null || buffer.Length == 0) throw new ArgumentException("A non-empty result buffer is required.", nameof(buffer));
        }
        private static void ValidateCount(Array buffer, int count)
        { if (count < 0 || count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count)); }
        private static void ValidateDistance(float distance)
        { if (float.IsNaN(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance)); }
    }
}
