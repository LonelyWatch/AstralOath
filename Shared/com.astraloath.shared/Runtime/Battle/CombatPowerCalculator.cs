using System;

namespace AstralOath.Shared.Battle
{
    /// <summary>
    /// Project CP: the 2019 researched P1 and pack-15-r4262 artifact coefficients.
    /// Uses final permanent integer panel values, decimal arithmetic, final floor, and no C/D caps.
    /// This is not a verified reconstruction of the current original game's CP or internal precision.
    /// </summary>
    public static class CombatPowerCalculator
    {
        // Current owned heroes explicitly have skill +0, no exclusive equipment or specialty progress.
        // Callers must pass those known coefficients; the calculator never infers player progression.
        public const string RulesVersion = "combat-power.project-v1.historical-p1.source-r4262-artifact.owned-progress-zero";

        /// <param name="panel">Final permanent stats, including artifact attack/health exactly once.</param>
        /// <param name="artifactStars">0 for confirmed unequipped, otherwise the artifact template's 3/4/5 stars.</param>
        /// <param name="artifactLevel">Actual artifact enhancement level, 0..30; not its skill rank.</param>
        /// <param name="skillUpgradeBonus">Explicit completed skill-upgrade coefficient; current owned scope is 0.</param>
        /// <param name="exclusiveEquipmentBonus">Explicit equipped-exclusive coefficient; current owned scope is 0.</param>
        /// <param name="specialtyChangeBonus">Explicit completed specialty coefficient; current owned scope is 0.</param>
        public static int Calculate(BattleStats panel, int artifactStars, int artifactLevel,
            decimal skillUpgradeBonus, decimal exclusiveEquipmentBonus, decimal specialtyChangeBonus)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            if (skillUpgradeBonus < 0) throw new ArgumentOutOfRangeException(nameof(skillUpgradeBonus));
            if (exclusiveEquipmentBonus < 0) throw new ArgumentOutOfRangeException(nameof(exclusiveEquipmentBonus));
            if (specialtyChangeBonus < 0) throw new ArgumentOutOfRangeException(nameof(specialtyChangeBonus));
            decimal artifact = ArtifactBonus(artifactStars, artifactLevel);
            decimal criticalChance = panel.CritChanceBp / 10000m;
            decimal criticalDamage = panel.CritDamageBp / 10000m;
            decimal effect = (panel.EffectivenessBp / 10000m + panel.EffectResistanceBp / 10000m) / 4m;
            decimal attack = panel.Attack * 1.6m * (1m + criticalChance * criticalDamage);
            decimal speed = 1m + (panel.Speed - 45m) * .02m;
            decimal basis = (attack * speed + panel.MaxHealth + panel.Defense * 9.3m) * (1m + effect);
            decimal multiplier = 1m + artifact + skillUpgradeBonus + exclusiveEquipmentBonus + specialtyChangeBonus;
            return checked((int)decimal.Floor(basis * multiplier));
        }

        private static decimal ArtifactBonus(int stars, int level)
        {
            if (level < 0 || level > 30 || (stars == 0 && level != 0))
                throw new ArgumentOutOfRangeException(nameof(level));
            // combat_power.db SHA-256 747807e538e58017fd776abdbfbff6a64436381e5fabd7fde0a155dbef2bbb66.
            // All 33 type=arti rows are linear in skill rank 1..11; 4-star rank 2 is .0495, not .049.
            // The project's existing artifact effect/UI mapping is skill rank = 1 + enhancement level / 3.
            int upgrades = level / 3;
            switch (stars)
            {
                case 0: return 0m;
                case 3: return .030m + .003m * upgrades;
                case 4: return .045m + .0045m * upgrades;
                case 5: return .060m + .006m * upgrades;
                default: throw new ArgumentOutOfRangeException(nameof(stars));
            }
        }
    }
}
