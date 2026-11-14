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
            public string resourceKind;
            public int resourceMaximum, resourceTurnGain, resourceAttackedSpend, resourceOpeningGain, resourceAttackedGain;

            public BattleResourceDefinition GetResourceDefinition()
            {
                switch (resourceKind)
                {
                    case "none":
                    case null:
                    case "":
                        if (resourceMaximum != 0 || resourceTurnGain != 0 || resourceAttackedSpend != 0 ||
                            resourceOpeningGain != 0 || resourceAttackedGain != 0)
                            throw new ArgumentException($"Hero '{id}' configures resource amounts without a resource kind.");
                        return null;
                    case "focus":
                        return new BattleResourceDefinition(BattleResourceKind.Focus, resourceMaximum, resourceTurnGain, resourceAttackedSpend,
                            resourceOpeningGain, resourceAttackedGain);
                    case "fighting_spirit":
                        return new BattleResourceDefinition(BattleResourceKind.FightingSpirit, resourceMaximum, resourceTurnGain, resourceAttackedSpend,
                            resourceOpeningGain, resourceAttackedGain);
                    default: throw new ArgumentException($"Unknown resource kind '{resourceKind}' for '{id}'.");
                }
            }
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
            public int resourceCost, resourceGain, resourceGainWhenBuffed;
            public int? attackRateBp, powerBp, casterHealthDamageBp, casterHealthHealBp,
                casterHealthBarrierBp, burnAttackRateBp, casterDefenseBarrierBp, casterAttackBarrierBp, casterDefenseDamageBp,
                casterSpeedDamagePpm, burnSpeedDamagePpm, casterHealthRatioDamageBp, damageAgainstBuffBp,
                defensePenetrationBp, stealthPenetrationBp, highestDefensePenetrationBp;
            public bool alwaysCritical, aoeWhenBuffed, aoeOutsideTurn;
            public string burnPresentationId;
            public string speedUpPresentationId, speedUpIconAsset, speedUpNameKey, speedUpDescriptionKey;
            public int? speedUpAttackRateBp, speedUpPowerBp;
            internal SkillRow Copy() => (SkillRow)MemberwiseClone();
        }

        [Serializable]
        public sealed class EffectRow
        {
            public string id, skillId, phase, operation, recipient, status, condition, chanceBonusStatus;
            public int order, chanceBp, turns, value, chanceBonusBp;
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
        public const uint RulesVersion = 12;
        public const string SlimeSkillId = "preview.slime.s1";
        public static IReadOnlyList<string> DefaultHeroIds { get; } = Array.AsReadOnly(new[] { "c1001", "c1062", "c1005", "c1001" });

        public static BattleMatch Create(string battleId, ulong seed, BattleCatalogData data, bool duel = false,
            IReadOnlyList<string> allyHeroIds = null) => Create(battleId, seed, data, allyHeroIds, null, duel);

        public static BattleMatch Create(string battleId, ulong seed, BattleCatalogData data,
            IReadOnlyList<string> allyHeroIds, IReadOnlyList<string> enemyHeroIds, bool duel = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != 2) throw new ArgumentException("Unsupported battle catalog schema.", nameof(data));
            var heroes = data.heroes.ToDictionary(row => row.id, StringComparer.Ordinal);
            var allies = allyHeroIds ?? DefaultHeroIds;
            var enemies = enemyHeroIds ?? (duel ? allies : null);
            ValidateRoster(allies, nameof(allyHeroIds));
            if (enemies != null) ValidateRoster(enemies, nameof(enemyHeroIds));

            var programs = new BattleSkillResolver(data, includePreviewSlime: enemies == null);
            var growth = data.growthPresets.ToDictionary(row => row.id, StringComparer.Ordinal);
            var roster = new List<BattleUnit>();
            var definitions = new Dictionary<string, HeroDefinition>(StringComparer.Ordinal);
            for (int index = 0; index < allies.Count; index++)
            {
                int slot = index + 1;
                roster.Add(CreateHero(allies[index], BattleSide.Ally, "ally-", slot));
                roster.Add(enemies == null ? Slime(slot) : CreateHero(enemies[index], BattleSide.Enemy, "enemy-", slot));
            }

            // The local exercise starts with souls to make all three heroes' Soul Burns testable.
            var rules = new BattleFlowRules(openingReadinessMaxBp: 500, initialAllySoul: duel ? 0 : 20);
            return new BattleMatch(battleId, RulesVersion, seed, new BattleFormation(roster), programs, rules);

            void ValidateRoster(IReadOnlyList<string> selection, string parameter)
            {
                if (selection.Count != DefaultHeroIds.Count || selection.Any(id => id == null || !heroes.ContainsKey(id)))
                    throw new ArgumentException("The battle roster must specify four known heroes.", parameter);
            }

            BattleUnit CreateHero(string id, BattleSide side, string prefix, int slot)
            {
                var row = heroes[id];
                if (!definitions.TryGetValue(id, out var hero))
                {
                    var presetRow = growth[row.growthPresetId];
                    var preset = new HeroGrowthPreset(presetRow.id, presetRow.nativeStars, presetRow.attackCoefficientMilli,
                        presetRow.healthCoefficientMilli, presetRow.defenseCoefficientMilli, presetRow.speedMilli);
                    var steps = data.awakeningSteps.Where(step => step.heroId == row.id).OrderBy(step => step.rank)
                        .Select(step => new HeroAwakeningStep(step.rank, step.attackFlat, step.healthFlat, step.attackPercentBp,
                            step.healthPercentBp, step.defensePercentBp, step.speedFlat, step.critChanceBp,
                            step.critDamageBp, step.effectivenessBp, step.effectResistanceBp));
                    var form = new BattleUnitForm(row.variant, row.id,
                        data.skills.Where(skill => skill.heroId == row.id).OrderBy(skill => skill.slot).Select(skill => skill.id));
                    hero = new HeroDefinition(row.id, row.nameKey, ReadElement(row.element),
                        row.heroClass, row.zodiac, form, preset, steps, row.GetResourceDefinition());
                    definitions.Add(id, hero);
                }
                return BattleUnit.CreateHero(prefix + slot, hero, new BattleSlot(side, slot), row.level, row.stars, row.awakenings);
            }
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
