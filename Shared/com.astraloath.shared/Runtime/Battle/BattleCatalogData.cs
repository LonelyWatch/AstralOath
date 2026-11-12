using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    // Names match the generated Luban snapshot. Unity and the server choose their own JSON reader.
    [Serializable]
    public sealed class BattleCatalogData
    {
        public int schemaVersion;
        public HeroRow[] heroes;
        public GrowthRow[] growthPresets;
        public AwakeningRow[] awakeningSteps;
        public SkillRow[] skills;
        public EffectRow[] effects;
        public EnhancementRow[] enhancements;
        public MessageRow[] messages;

        [Serializable]
        public sealed class HeroRow
        {
            public string id, nameKey, element, heroClass, zodiac, growthPresetId, variant;
            public int level, stars, awakenings;
        }

        [Serializable]
        public sealed class GrowthRow
        {
            public string id;
            public int nativeStars, attackCoefficientMilli, healthCoefficientMilli, defenseCoefficientMilli, speedMilli;
        }

        [Serializable]
        public sealed class AwakeningRow
        {
            public string heroId;
            public int rank, attackFlat, healthFlat, attackPercentBp, healthPercentBp, defensePercentBp,
                speedFlat, critChanceBp, critDamageBp, effectivenessBp, effectResistanceBp;
        }

        [Serializable]
        public sealed class SkillRow
        {
            public string id, heroId, nameKey, presentationId, iconAsset, target, soulBurnKind;
            public int slot, cooldownTurns, soulGain, soulBurnCost, soulBurnTurns;
            public int? attackRateBp, powerBp, casterHealthDamageBp, casterHealthHealBp,
                casterHealthBarrierBp, burnAttackRateBp;
            internal SkillRow Copy() => (SkillRow)MemberwiseClone();
        }

        [Serializable]
        public sealed class EffectRow
        {
            public string id, skillId, phase, operation, recipient, status;
            public int order, chanceBp, turns, value;
            internal EffectRow Copy() => (EffectRow)MemberwiseClone();
        }

        [Serializable]
        public sealed class EnhancementRow
        {
            public string skillId, kind;
            public int step, value;
        }

        [Serializable]
        public sealed class MessageRow
        {
            public string key, zhCn;
        }
    }

    public static class BattlePreviewFactory
    {
        public const uint RulesVersion = 1;
        public const string SlimeSkillId = "preview.slime.s1";
        private static readonly string[] PreviewHeroes = { "c1001", "c1062", "c1005", "c1001" };

        public static BattleMatch Create(string battleId, ulong seed, BattleCatalogData data, bool duel = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != 2) throw new ArgumentException("Unsupported battle catalog schema.", nameof(data));
            var programs = new BattleSkillResolver(data, includePreviewSlime: !duel);
            var heroes = data.heroes.ToDictionary(row => row.id, StringComparer.Ordinal);
            var growth = data.growthPresets.ToDictionary(row => row.id, StringComparer.Ordinal);
            var roster = new List<BattleUnit>();
            for (int i = 0; i < PreviewHeroes.Length; i++)
            {
                var row = heroes[PreviewHeroes[i]];
                var presetRow = growth[row.growthPresetId];
                var preset = new HeroGrowthPreset(presetRow.id, presetRow.nativeStars, presetRow.attackCoefficientMilli,
                    presetRow.healthCoefficientMilli, presetRow.defenseCoefficientMilli, presetRow.speedMilli);
                var steps = data.awakeningSteps.Where(step => step.heroId == row.id).OrderBy(step => step.rank)
                    .Select(step => new HeroAwakeningStep(step.rank, step.attackFlat, step.healthFlat, step.attackPercentBp,
                        step.healthPercentBp, step.defensePercentBp, step.speedFlat, step.critChanceBp,
                        step.critDamageBp, step.effectivenessBp, step.effectResistanceBp));
                var form = new BattleUnitForm(row.variant, row.id,
                    data.skills.Where(skill => skill.heroId == row.id).OrderBy(skill => skill.slot).Select(skill => skill.id));
                var hero = new HeroDefinition(row.id, row.nameKey, ReadElement(row.element),
                    row.heroClass, row.zodiac, form, preset, steps);
                int slot = i + 1;
                roster.Add(BattleUnit.CreateHero($"ally-{slot}", hero, new BattleSlot(BattleSide.Ally, slot),
                    row.level, row.stars, row.awakenings));
                roster.Add(duel
                    ? BattleUnit.CreateHero($"enemy-{slot}", hero, new BattleSlot(BattleSide.Enemy, slot),
                        row.level, row.stars, row.awakenings)
                    : Slime(slot));
            }

            // The local exercise starts with souls to make all three heroes' Soul Burns testable.
            var rules = new BattleFlowRules(openingReadinessMaxBp: 500, initialAllySoul: duel ? 0 : 20);
            return new BattleMatch(battleId, RulesVersion, seed, new BattleFormation(roster), programs, rules);
        }

        private static BattleElement ReadElement(string element) =>
            Enum.TryParse<BattleElement>(element, true, out var value) && Enum.IsDefined(typeof(BattleElement), value)
                ? value : throw new ArgumentException($"Unknown element '{element}'.");

        private static BattleUnit Slime(int slot)
        {
            var elements = new[] { BattleElement.Ice, BattleElement.Fire, BattleElement.Wind, BattleElement.Dark };
            var form = new BattleUnitForm("preview", "slime", new[] { SlimeSkillId });
            var definition = new MonsterDefinition("preview.slime", "battle.preview.slime", elements[slot - 1], form);
            return BattleUnit.CreateMonster($"enemy-{slot}", definition, new BattleSlot(BattleSide.Enemy, slot),
                60, MonsterRank.Normal, new BattleStats(850, 3200, 460, 88 + slot));
        }
    }
}
