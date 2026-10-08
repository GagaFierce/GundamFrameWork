using System;
using UnityEngine;
using UnityPhysics2D = UnityEngine.Physics2D;

namespace WFrameWork.Physics.Unity
{
    public sealed class PhysicsQueryService2D
    {
        private readonly PhysicsScene2D _scene;
        public PhysicsQueryService2D() : this(UnityPhysics2D.defaultPhysicsScene) { }
        public PhysicsQueryService2D(PhysicsScene2D scene) { _scene = scene; }
        public PhysicsScene2D Scene => _scene;

        public PhysicsQueryResult Raycast(Vector2 origin, Vector2 direction, float distance,
            in PhysicsQueryFilter2D filter, RaycastHit2D[] results)
        {
            ValidateBuffer(results); ValidateDistance(distance);
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.Raycast(origin, direction, distance, MakeContactFilter(filter), results);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult CircleCast(Vector2 origin, float radius, Vector2 direction, float distance,
            in PhysicsQueryFilter2D filter, RaycastHit2D[] results)
        {
            ValidateBuffer(results); ValidateDistance(distance);
            if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.CircleCast(origin, radius, direction, distance, MakeContactFilter(filter), results);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance,
            in PhysicsQueryFilter2D filter, RaycastHit2D[] results)
        {
            ValidateBuffer(results); ValidateDistance(distance);
            if (size.x < 0 || size.y < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.BoxCast(origin, size, angle, direction, distance, MakeContactFilter(filter), results);
            if (filter.SortByDistance) Sort(results, count);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult OverlapCircle(Vector2 origin, float radius,
            in PhysicsQueryFilter2D filter, Collider2D[] results)
        {
            ValidateBuffer(results);
            if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.OverlapCircle(origin, radius, MakeContactFilter(filter), results);
            return new PhysicsQueryResult(count, results.Length);
        }

        public PhysicsQueryResult OverlapBox(Vector2 origin, Vector2 size, float angle,
            in PhysicsQueryFilter2D filter, Collider2D[] results)
        {
            ValidateBuffer(results);
            if (size.x < 0 || size.y < 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!_scene.IsValid()) return new PhysicsQueryResult(0, results.Length);
            int count = _scene.OverlapBox(origin, size, angle, MakeContactFilter(filter), results);
            return new PhysicsQueryResult(count, results.Length);
        }

        public static bool TryGetNearest(RaycastHit2D[] results, int count, out RaycastHit2D nearest)
        {
            ValidateCount(results, count);
            if (count == 0) { nearest = default(RaycastHit2D); return false; }
            int index = 0;
            for (int i = 1; i < count; i++) if (results[i].distance < results[index].distance) index = i;
            nearest = results[index];
            return true;
        }

        private static ContactFilter2D MakeContactFilter(PhysicsQueryFilter2D filter)
        {
            var contact = new ContactFilter2D { useLayerMask = true, layerMask = filter.LayerMask, useTriggers = filter.UseTriggers };
            return contact;
        }
        private static void Sort(RaycastHit2D[] results, int count)
        {
            for (int i = 1; i < count; i++)
            {
                RaycastHit2D value = results[i]; int j = i - 1;
                while (j >= 0 && results[j].distance > value.distance) { results[j + 1] = results[j]; j--; }
                results[j + 1] = value;
            }
        }
        private static void ValidateBuffer(Array buffer)
        { if (buffer == null || buffer.Length == 0) throw new ArgumentException("A non-empty result buffer is required.", nameof(buffer)); }
        private static void ValidateCount(Array buffer, int count)
        { if (count < 0 || count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count)); }
        private static void ValidateDistance(float distance)
        { if (float.IsNaN(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance)); }
    }
}
