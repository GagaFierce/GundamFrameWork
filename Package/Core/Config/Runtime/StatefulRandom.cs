using System;
namespace WFrameWork.Config
{
    /// <summary>Version-one xorshift32 stream. Persist State, not just its initial seed.</summary>
    public sealed class StatefulRandom
    {
        public const int AlgorithmVersion = 1;
        public uint State { get; private set; }
        public StatefulRandom(uint state) { State = state == 0 ? 0x6D2B79F5u : state; }
        public uint NextUInt() { uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x; return x; }
        public int Next(int min, int max)
        {
            if (max <= min) throw new ArgumentOutOfRangeException(nameof(max));
            uint range = (uint)((long)max - min), threshold = unchecked(0u - range) % range, x;
            do { x = NextUInt(); } while (x < threshold);
            return (int)(min + (long)(x % range));
        }
        public static uint Derive(int seed, string name)
        { uint hash = unchecked((uint)seed) ^ 2166136261u; foreach (char c in name) hash = (hash ^ c) * 16777619u; return hash == 0 ? 1u : hash; }
    }
}
