namespace AstralOath.Shared.Battle
{
    /// <summary>SplitMix64 stream. Replays preserve the seed and call order.</summary>
    public sealed class BattleRandom
    {
        private ulong state;

        public BattleRandom(ulong seed) => state = seed;

        public ulong NextUInt64()
        {
            unchecked
            {
                state += 0x9e3779b97f4a7c15UL;
                var value = (state ^ (state >> 30)) * 0xbf58476d1ce4e5b9UL;
                value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }
    }
}
