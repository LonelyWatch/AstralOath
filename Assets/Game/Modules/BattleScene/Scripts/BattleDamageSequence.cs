namespace AstralOath.Client.BattleScene
{
    internal static class BattleDamageSequence
    {
        internal readonly struct Hit
        {
            public readonly float At;
            public readonly int Amount;

            public Hit(float at, int amount)
            {
                At = at;
                Amount = amount;
            }
        }

        public static Hit[] Build(int total, float[] hitTimes, float fallbackImpact)
        {
            if (hitTimes == null || hitTimes.Length == 0)
                return new[] { new Hit(fallbackImpact, total) };

            var hits = new Hit[hitTimes.Length];
            int assigned = 0;
            for (int index = 0; index < hits.Length; index++)
            {
                int cumulative = (int)((long)total * (index + 1) / hits.Length);
                hits[index] = new Hit(hitTimes[index], cumulative - assigned);
                assigned = cumulative;
            }
            return hits;
        }
    }
}
