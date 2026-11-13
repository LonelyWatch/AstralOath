using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Shared.Battle;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleInspectionContent
    {
        private readonly IReadOnlyDictionary<string, string> messages;
        private readonly ILookup<string, BattleCatalogData.EffectRow> effects;
        private readonly ILookup<string, BattleCatalogData.EnhancementRow> enhancements;

        public BattleInspectionContent(BattleCatalogData catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            messages = catalog.messages.ToDictionary(row => row.key, row => row.zhCn, StringComparer.Ordinal);
            effects = catalog.effects.OrderBy(row => row.order).ToLookup(row => row.skillId, StringComparer.Ordinal);
            enhancements = catalog.enhancements.OrderBy(row => row.step).ToLookup(row => row.skillId, StringComparer.Ordinal);
        }

        // Export-BattleCatalog validates the description key against the stable skill ID.
        internal string SkillDescription(BattleCatalogData.SkillRow skill) =>
            Label("battle.skill." + skill.id + ".description");

        internal string StatusName(BattleStatusKind kind) => Label(StatusKey(kind) + ".name");

        internal string StatusDescription(BattleStatusKind kind) => Label(StatusKey(kind) + ".description");

        internal string StatusDescriptionWithStrength(BattleStatusKind kind, int strength)
        {
            string format = strength > 0 ? Label(StatusKey(kind) + ".strength") : string.Empty;
            if (string.IsNullOrEmpty(format)) return StatusDescription(kind);
            decimal percent = strength * 100m / EpicSevenCombatMath.Basis;
            return string.Format(CultureInfo.CurrentCulture, format, percent);
        }

        internal string SkillBurnDescription(BattleCatalogData.SkillRow skill)
        {
            if (skill.soulBurnCost <= 0) return string.Empty;
            string text = Label("battle.inspect.burn." + skill.soulBurnKind);
            return string.Format(CultureInfo.CurrentCulture, text, skill.soulBurnTurns);
        }

        internal string SkillEnhancements(BattleCatalogData.SkillRow skill)
        {
            var lines = new List<string>();
            foreach (var step in enhancements[skill.id])
            {
                string format = Label("battle.inspect.enhancement." + step.kind);
                if (string.IsNullOrEmpty(format)) continue;
                decimal amount = step.kind.EndsWith("_bp", StringComparison.Ordinal) ? step.value / 100m : step.value;
                lines.Add(string.Format(CultureInfo.CurrentCulture, format, step.step, amount));
            }
            return string.Join("\n", lines);
        }

        internal (string Title, string Description, BattleStatusKind? Status) SkillAuxiliaryEffect(BattleCatalogData.SkillRow skill)
        {
            foreach (var effect in effects[skill.id])
            {
                if (effect.operation == "dual_attack")
                    return (Label("battle.inspect.dual_attack.name"), Label("battle.inspect.dual_attack.description"), null);
                if (effect.operation == "apply_status" && Enum.TryParse(effect.status, out BattleStatusKind kind))
                {
                    string title = StatusName(kind);
                    if (!string.IsNullOrEmpty(title)) return (title, StatusDescriptionWithStrength(kind, effect.value), kind);
                }
            }
            return (string.Empty, string.Empty, null);
        }

        private static string StatusKey(BattleStatusKind kind) => "battle.status." + kind.ToString().ToLowerInvariant();

        private string Label(string key) => messages.TryGetValue(key, out string text) ? text : string.Empty;
    }
}
