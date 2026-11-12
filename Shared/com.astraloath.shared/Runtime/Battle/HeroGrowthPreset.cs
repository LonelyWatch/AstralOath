using System;

namespace AstralOath.Shared.Battle
{
    public sealed class HeroGrowthPreset
    {
        public HeroGrowthPreset(string id, int nativeStars, int attackCoefficientMilli, int healthCoefficientMilli,
            int defenseCoefficientMilli, int speedMilli)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A growth preset needs an ID.", nameof(id));
            if (nativeStars < 1 || nativeStars > 6) throw new ArgumentOutOfRangeException(nameof(nativeStars));
            if (attackCoefficientMilli <= 0) throw new ArgumentOutOfRangeException(nameof(attackCoefficientMilli));
            if (healthCoefficientMilli <= 0) throw new ArgumentOutOfRangeException(nameof(healthCoefficientMilli));
            if (defenseCoefficientMilli < 0) throw new ArgumentOutOfRangeException(nameof(defenseCoefficientMilli));
            if (speedMilli <= 0) throw new ArgumentOutOfRangeException(nameof(speedMilli));

            Id = id;
            NativeStars = nativeStars;
            AttackCoefficientMilli = attackCoefficientMilli;
            HealthCoefficientMilli = healthCoefficientMilli;
            DefenseCoefficientMilli = defenseCoefficientMilli;
            SpeedMilli = speedMilli;
        }

        public string Id { get; }
        public int NativeStars { get; }
        public int AttackCoefficientMilli { get; }
        public int HealthCoefficientMilli { get; }
        public int DefenseCoefficientMilli { get; }
        public int SpeedMilli { get; }
    }

    public sealed class HeroAwakeningStep
    {
        public HeroAwakeningStep(int rank, int attackFlat, int healthFlat, int attackPercentBp = 0,
            int healthPercentBp = 0, int defensePercentBp = 0, int speedFlat = 0, int critChanceBp = 0,
            int critDamageBp = 0, int effectivenessBp = 0, int effectResistanceBp = 0)
        {
            if (rank < 1 || rank > 6) throw new ArgumentOutOfRangeException(nameof(rank));
            Rank = rank;
            AttackFlat = NonNegative(attackFlat, nameof(attackFlat));
            HealthFlat = NonNegative(healthFlat, nameof(healthFlat));
            AttackPercentBp = NonNegative(attackPercentBp, nameof(attackPercentBp));
            HealthPercentBp = NonNegative(healthPercentBp, nameof(healthPercentBp));
            DefensePercentBp = NonNegative(defensePercentBp, nameof(defensePercentBp));
            SpeedFlat = NonNegative(speedFlat, nameof(speedFlat));
            CritChanceBp = NonNegative(critChanceBp, nameof(critChanceBp));
            CritDamageBp = NonNegative(critDamageBp, nameof(critDamageBp));
            EffectivenessBp = NonNegative(effectivenessBp, nameof(effectivenessBp));
            EffectResistanceBp = NonNegative(effectResistanceBp, nameof(effectResistanceBp));
        }

        public int Rank { get; }
        public int AttackFlat { get; }
        public int HealthFlat { get; }
        public int AttackPercentBp { get; }
        public int HealthPercentBp { get; }
        public int DefensePercentBp { get; }
        public int SpeedFlat { get; }
        public int CritChanceBp { get; }
        public int CritDamageBp { get; }
        public int EffectivenessBp { get; }
        public int EffectResistanceBp { get; }

        private static int NonNegative(int value, string parameter)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(parameter);
            return value;
        }
    }
}
