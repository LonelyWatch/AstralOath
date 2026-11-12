using System;

namespace AstralOath.Shared.Battle
{
    public sealed class BattleStats
    {
        public BattleStats(int attack, int maxHealth, int defense, int speed, int critChanceBp = 1500,
            int critDamageBp = 15000, int effectivenessBp = 0, int effectResistanceBp = 0)
        {
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            Attack = NonNegative(attack, nameof(attack));
            MaxHealth = maxHealth;
            Defense = NonNegative(defense, nameof(defense));
            Speed = NonNegative(speed, nameof(speed));
            CritChanceBp = NonNegative(critChanceBp, nameof(critChanceBp));
            CritDamageBp = NonNegative(critDamageBp, nameof(critDamageBp));
            EffectivenessBp = NonNegative(effectivenessBp, nameof(effectivenessBp));
            EffectResistanceBp = NonNegative(effectResistanceBp, nameof(effectResistanceBp));
        }

        public int Attack { get; }
        public int MaxHealth { get; }
        public int Defense { get; }
        public int Speed { get; }
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
