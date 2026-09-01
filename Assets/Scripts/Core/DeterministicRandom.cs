namespace BlockBlast.Core
{
    /// <summary>
    /// A small seeded PRNG the spawn logic uses instead of UnityEngine.Random.
    ///
    /// The point is reproducibility. Spawn selection is the system whose feel is hardest
    /// to judge and easiest to break, and a global RNG makes a bad tray impossible to
    /// reproduce or regression-test. With an explicit seed, a run that felt unfair can be
    /// replayed exactly.
    ///
    /// xorshift32: not cryptographic, but uniform enough for weighted picks and far
    /// cheaper than anything stronger.
    /// </summary>
    public struct DeterministicRandom
    {
        private uint state;

        public DeterministicRandom(uint seed)
        {
            // Zero is a fixed point for xorshift, so it must never be the state.
            state = seed == 0u ? 0x9E3779B9u : seed;
        }

        public uint State => state;

        public uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>Uniform in [0, exclusiveMax). Returns 0 when the range is empty.</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 1) return 0;
            return (int)(NextUInt() % (uint)exclusiveMax);
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);
    }
}
