using System;

namespace AfterAll.Generation
{
    /// <summary>
    /// Deterministic xorshift32 PRNG. Platform-stable unlike System.Random / UnityEngine.Random.
    /// Use Derive() for isolated child sequences (chunks, passes, edges).
    /// </summary>
    public sealed class Rng
    {
        private uint _state;

        public int Seed { get; }

        public Rng(int seed)
        {
            Seed = seed;
            _state = (uint)seed;
            if (_state == 0)
                _state = 0x6a09e667u;
        }

        /// <summary>Returns a random float in [min, max).</summary>
        public float Range(float min, float max) =>
            min + Value() * (max - min);

        /// <summary>Returns a random int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                return minInclusive;

            uint span = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt() % span);
        }

        /// <summary>Returns a random float in [0, 1).</summary>
        public float Value() => NextUInt() / (float)uint.MaxValue;

        /// <summary>Returns true with the given probability (0 = never, 1 = always).</summary>
        public bool Chance(float probability) => Value() < probability;

        /// <summary>
        /// Creates a child Rng with a seed derived from this seed and an extra integer.
        /// </summary>
        public Rng Derive(int extra)
        {
            unchecked
            {
                int h = Seed ^ (extra * (int)0x9e3779b9u);
                h ^= h >> 16;
                h *= (int)0x45d9f3b7u;
                h ^= h >> 16;
                return new Rng(h);
            }
        }

        /// <summary>Derives a child Rng from two integers (e.g. chunk grid X/Z).</summary>
        public Rng Derive(int a, int b) => Derive(a ^ (b * 1000003));

        private uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }
    }
}
