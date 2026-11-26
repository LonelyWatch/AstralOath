using System;
using System.Globalization;
using AstralOath.Shared.Battle;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private Func<string, (int stars, int level)> combatPowerArtifactProgress;

        internal void ConfigureCombatPower(Func<string, (int stars, int level)> artifactProgress) =>
            combatPowerArtifactProgress = artifactProgress;

        private bool TryCombatPowerQuote(int[] before, int[] after, out int current, out int proposed)
        {
            current = proposed = 0;
            if (combatPowerArtifactProgress == null || string.IsNullOrEmpty(targetId) ||
                before?.Length != 8 || after?.Length != 8) return false;
            try
            {
                // Both sides use one confirmed artifact snapshot. PanelValues already included its attack/health.
                var artifact = combatPowerArtifactProgress(targetId);
                if (artifact.stars < 0 || artifact.level < 0) return false;
                int Evaluate(int[] values) => CombatPowerCalculator.Calculate(new BattleStats(
                    values[0], values[2], values[1], values[3], values[4], values[5], values[6], values[7]),
                    artifact.stars, artifact.level, 0m, 0m, 0m);
                // Owned progression is explicitly skill +0, no exclusive equipment or specialty progress in this version.
                current = Evaluate(before);
                proposed = Evaluate(after);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        private static string CombatPowerNumber(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
