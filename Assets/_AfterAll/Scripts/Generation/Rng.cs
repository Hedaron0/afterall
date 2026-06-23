using System;

namespace AfterAll.Generation
{
    /// <summary>
    /// Deterministic seeded random number generator wrapping System.Random.
    /// Use Derive() to create isolated child RNGs for sub-systems (chunks, passes, etc.)
    /// so they don't pollute each other's sequence.
    /// </summary>
    public sealed class Rng
    {
        private readonly Random _rnd;

        public int Seed { get; }

        public Rng(int seed)
        {
            Seed = seed;
            _rnd = new Random(seed);
        }

        /// <summary>Returns a random float in [min, max).</summary>
        public float Range(float min, float max) =>
            min + (float)_rnd.NextDouble() * (max - min);

        /// <summary>Returns a random int in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive) =>
            _rnd.Next(minInclusive, maxExclusive);

        /// <summary>Returns a random float in [0, 1).</summary>
        public float Value() => (float)_rnd.NextDouble();

        /// <summary>Returns true with the given probability (0 = never, 1 = always).</summary>
        public bool Chance(float probability) => _rnd.NextDouble() < probability;

        /// <summary>
        /// Creates a child Rng with a seed derived from this seed and an extra integer.
        /// Lets chunk generation, per-partition passes, etc. each have their own
        /// independent sequence while remaining fully reproducible from the root seed.
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

        /// <summary>
        /// Derives a child Rng from two integers (e.g. chunk grid X/Z coordinates).
        /// </summary>
        public Rng Derive(int a, int b) => Derive(a ^ (b * 1000003));
    }
}
