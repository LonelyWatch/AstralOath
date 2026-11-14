using System;

namespace AstralOath.Shared.Battle
{
    public static class EpicSevenCombatMath
    {
        public const int Basis = 10000;

        public static int ElementRelation(BattleElement attacker, BattleElement defender)
        {
            if (attacker == defender) return 0;
            if ((attacker == BattleElement.Light && defender == BattleElement.Dark) ||
                (attacker == BattleElement.Dark && defender == BattleElement.Light)) return 1;
            if (attacker == BattleElement.Light || attacker == BattleElement.Dark ||
                defender == BattleElement.Light || defender == BattleElement.Dark) return 0;
            return (attacker == BattleElement.Fire && defender == BattleElement.Wind) ||
                (attacker == BattleElement.Wind && defender == BattleElement.Ice) ||
                (attacker == BattleElement.Ice && defender == BattleElement.Fire) ? 1 : -1;
        }

        public static int EffectSuccessBp(int effectiveness, int resistance) =>
            (int)Math.Clamp(10000L + effectiveness - resistance, 0L, 8500L);

        public static bool Roll(BattleRandom random, int chanceBp)
        {
            if (chanceBp <= 0) return false;
            if (chanceBp >= Basis) return true;
            return random.NextUInt64() % Basis < (ulong)chanceBp;
        }

        public static BattleHitKind RollHit(BattleRandom random, int relation, int criticalChance,
            int evasion = 0, int criticalResistance = 0, int hitChance = 0, bool alwaysCritical = false)
        {
            if (Roll(random, evasion + (relation < 0 ? 5000 : 0) - hitChance)) return BattleHitKind.Miss;
            if (alwaysCritical || Roll(random, criticalChance + (relation > 0 ? 1500 : 0) - criticalResistance)) return BattleHitKind.Critical;
            return Roll(random, 3000) ? BattleHitKind.Crushing : BattleHitKind.Normal;
        }

        public static int Damage(decimal attack, decimal defense, int maximumHealth, int attackRateBp, int powerBp,
            int healthRateBp, int enhancementBp, int elementRelation, BattleHitKind hit, int criticalDamageBp,
            decimal additionalBaseDamage = 0m, decimal damageMultiplier = 1m)
        {
            decimal baseDamage = (attack * attackRateBp + (decimal)maximumHealth * healthRateBp) / Basis + additionalBaseDamage;
            decimal hitMultiplier = hit switch
            {
                BattleHitKind.Miss => 0.75m,
                BattleHitKind.Crushing => 1.3m,
                BattleHitKind.Critical => criticalDamageBp / (decimal)Basis,
                _ => 1m
            };
            decimal result = baseDamage * (powerBp / (decimal)Basis) * 1.871m *
                (1m + enhancementBp / (decimal)Basis) * (elementRelation > 0 ? 1.1m : 1m) *
                hitMultiplier * damageMultiplier / (1m + Math.Max(0m, defense) / 300m);
            return (int)Math.Clamp(Math.Round(result, MidpointRounding.AwayFromZero), 1m, int.MaxValue);
        }

        internal static int PeriodicDamage(int baseDamage, decimal defense) =>
            (int)Math.Clamp(Math.Round(baseDamage * 1.871m / (1m + Math.Max(0m, defense) * .3m / 300m),
                MidpointRounding.AwayFromZero), 1m, int.MaxValue);

        internal static int ScaleHealth(decimal health, int rateBp, int enhancementBp = 0) =>
            (int)Math.Clamp(Math.Round(health * rateBp / Basis * (1m + enhancementBp / (decimal)Basis),
                MidpointRounding.AwayFromZero), 0m, int.MaxValue);
    }
}
