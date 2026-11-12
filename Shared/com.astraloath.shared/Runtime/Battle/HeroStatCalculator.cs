using System;
using System.Collections.Generic;

namespace AstralOath.Shared.Battle
{
    public static class HeroStatCalculator
    {
        public static BattleStats Calculate(HeroGrowthPreset preset, int level, int stars, int awakenings,
            IReadOnlyList<HeroAwakeningStep> steps)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (stars < preset.NativeStars || stars > 6) throw new ArgumentOutOfRangeException(nameof(stars));
            int levelCap = stars == 1 ? 15 : stars * 10;
            if (level < 1 || level > levelCap) throw new ArgumentOutOfRangeException(nameof(level));
            if (awakenings < 0 || awakenings > stars) throw new ArgumentOutOfRangeException(nameof(awakenings));
            if (steps.Count < awakenings || steps.Count > 6)
                throw new ArgumentException("Provide the awakening steps required by the requested rank.", nameof(steps));

            double starMultiplier = 1d + 0.075d * (stars - 1);
            int attack = Math.Max(1, Unawakened(preset.AttackCoefficientMilli, level, 6d, starMultiplier));
            int health = Math.Max(1, Unawakened(preset.HealthCoefficientMilli, level, 3d, starMultiplier));
            int defense = Unawakened(preset.DefenseCoefficientMilli, level, 8d, starMultiplier);
            long speed = Math.Max(1, preset.SpeedMilli / 1000);
            long attackPercent = 0, healthPercent = 0, defensePercent = 0;
            long attackFlat = 0, healthFlat = 0;
            long critChance = 1500, critDamage = 15000, effectiveness = 0, effectResistance = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                HeroAwakeningStep step = steps[i];
                if (step == null || step.Rank != i + 1)
                    throw new ArgumentException("Awakening steps must be in consecutive rank order.", nameof(steps));
                if (i >= awakenings) continue;

                attackPercent += step.AttackPercentBp;
                healthPercent += step.HealthPercentBp;
                defensePercent += step.DefensePercentBp;
                attackFlat += step.AttackFlat;
                healthFlat += step.HealthFlat;
                speed += step.SpeedFlat;
                critChance += step.CritChanceBp;
                critDamage += step.CritDamageBp;
                effectiveness += step.EffectivenessBp;
                effectResistance += step.EffectResistanceBp;
            }

            return new BattleStats(
                Awakened(attack, attackPercent, attackFlat),
                Awakened(health, healthPercent, healthFlat),
                Awakened(defense, defensePercent, 0),
                checked((int)speed), checked((int)critChance), checked((int)critDamage),
                checked((int)effectiveness), checked((int)effectResistance));
        }

        private static int Unawakened(int coefficientMilli, int level, double divisor, double starMultiplier)
        {
            // Retain the published operation order at integer floor boundaries.
            double coefficient = coefficientMilli / 1000d;
            return (int)Math.Floor(coefficient * (1d + level / divisor) * starMultiplier);
        }

        private static int Awakened(int unawakened, long percentBp, long flat)
        {
            // Percent bonuses scale the truncated pre-awakening value, excluding flat awakening bonuses.
            return checked((int)(unawakened + unawakened * percentBp / 10000L + flat));
        }
    }
}
