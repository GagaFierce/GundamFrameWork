using System;

namespace WFrameWork.Physics.Unity
{
    public readonly struct PhysicsQueryResult
    {
        public int Count { get; }
        /// <summary>True when Count equals the caller buffer length; more hits may exist.</summary>
        public bool MayBeTruncated { get; }
        public bool HasHits => Count > 0;
        internal PhysicsQueryResult(int count, int capacity)
        { Count = count; MayBeTruncated = capacity > 0 && count == capacity; }
    }

    [Serializable]
    public struct PhysicsQueryFilter3D
    {
        public int LayerMask;
        public UnityEngine.QueryTriggerInteraction TriggerInteraction;
        public bool SortByDistance;
        public static PhysicsQueryFilter3D Default => new PhysicsQueryFilter3D
        {
            LayerMask = UnityEngine.Physics.DefaultRaycastLayers,
            TriggerInteraction = UnityEngine.QueryTriggerInteraction.UseGlobal,
            SortByDistance = false
        };
    }

    [Serializable]
    public struct PhysicsQueryFilter2D
    {
        public int LayerMask;
        public bool UseTriggers;
        public bool SortByDistance;
        public static PhysicsQueryFilter2D Default => new PhysicsQueryFilter2D
        {
            LayerMask = UnityEngine.Physics2D.AllLayers,
            UseTriggers = true,
            SortByDistance = false
        };
    }
}

