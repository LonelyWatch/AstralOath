using System;
using System.Collections.Generic;
using System.Linq;

namespace AstralOath.Shared.Battle
{
    public sealed class BattleAiChoice
    {
        internal BattleAiChoice(string skillId, string targetId, bool useSoulBurn)
        {
            SkillId = skillId;
            TargetId = targetId;
            UseSoulBurn = useSoulBurn;
        }

        public string SkillId { get; }
        public string TargetId { get; }
        public bool UseSoulBurn { get; }
    }

    // Returns an intent for the normal command path. A choice never mutates the snapshot or the random stream.
    public sealed class BattleAiPolicy
    {
        private readonly Dictionary<string, BattleCatalogData.SkillRow> skills;
        private readonly ILookup<string, BattleCatalogData.EffectRow> effects;
        private readonly HashSet<string> soulWeavers;

        private readonly ILookup<string, BattleCatalogData.EnhancementRow> resourceDiscounts;
        private readonly Func<string, string, int> enhancementRank;

        public BattleAiPolicy(BattleCatalogData catalog, Func<string, string, int> enhancementRank = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.skills == null || catalog.effects == null)
                throw new ArgumentException("Battle AI requires the skill and effect catalog.", nameof(catalog));
            skills = catalog.skills.ToDictionary(row => row.id, row => row.Copy(), StringComparer.Ordinal);
            skills.Add(BattlePreviewFactory.SlimeSkillId, BattleSkillResolver.CreatePreviewSlimeSkill());
            this.enhancementRank = enhancementRank;
            resourceDiscounts = (catalog.enhancements ?? Array.Empty<BattleCatalogData.EnhancementRow>())
                .Where(row => row.kind == "resource_cost")
                .Select(row => new BattleCatalogData.EnhancementRow { skillId = row.skillId, step = row.step, value = row.value })
                .ToLookup(row => row.skillId, StringComparer.Ordinal);
            effects = catalog.effects.Select(row => row.Copy()).ToLookup(row => row.skillId, StringComparer.Ordinal);
            soulWeavers = new HashSet<string>((catalog.heroes ?? Array.Empty<BattleCatalogData.HeroRow>())
                .Where(row => row.heroClass == "soul_weaver").Select(row => row.id), StringComparer.Ordinal);
        }

        public bool IsSkillAvailable(BattleSnapshot snapshot, string skillId, bool useSoulBurn = false)
        {
            var actor = CurrentActor(snapshot);
            if (actor == null || skillId == null || !actor.Form.SkillIds.Contains(skillId) ||
                !skills.TryGetValue(skillId, out var skill)) return false;
            int discount = 0;
            if (enhancementRank != null && resourceDiscounts.Contains(skillId))
            {
                int rank = enhancementRank(actor.InstanceId, skillId);
                discount = resourceDiscounts[skillId].Where(row => row.step <= rank).Sum(row => row.value);
            }
            return BattleSkillResolver.CanUseSkill(snapshot, actor, skill, useSoulBurn, discount);
        }

        public bool CanSelectTarget(BattleSnapshot snapshot, string skillId, string targetId, bool useSoulBurn = false)
        {
            if (!IsSkillAvailable(snapshot, skillId, useSoulBurn)) return false;
            var actor = CurrentActor(snapshot);
            return BattleTargeting.CanSelect(snapshot, actor, BattleTargeting.PrimaryRule(skills[skillId], actor), targetId);
        }

        public bool IsForcedAction(BattleSnapshot snapshot)
        {
            var actor = CurrentActor(snapshot);
            return actor != null && BattleTargeting.ActiveProvoker(snapshot, actor) != null;
        }

        public BattleAiChoice ChooseAction(BattleSnapshot snapshot)
        {
            var actor = CurrentActor(snapshot);
            if (actor == null) return null;
            var available = actor.Form.SkillIds.Where(id => IsSkillAvailable(snapshot, id))
                .Select(id => skills[id]).OrderByDescending(skill => skill.slot).ToArray();
            if (IsForcedAction(snapshot) || !soulWeavers.Contains(actor.DefinitionId))
                return available.Select(skill => ChooseTarget(snapshot, actor, skill, false)).FirstOrDefault(choice => choice != null);

            var allies = AliveOnSide(snapshot, actor.Slot.Side).ToArray();
            var support = available.Where(skill => TargetsAllies(skill.target)).ToArray();
            foreach (var skill in support)
            {
                var target = BestAllyTarget(skill, actor, allies);
                if (target != null && CleanseScore(skill, actor, target, allies) > 0)
                    return new BattleAiChoice(skill.id, target.InstanceId, false);
            }
            if (allies.Any(unit => IsBelowHalf(unit) && unit.GetStatus(BattleStatusKind.Unhealable) == null))
            {
                foreach (var skill in support)
                {
                    var target = BestAllyTarget(skill, actor, allies);
                    if (target != null) return new BattleAiChoice(skill.id, target.InstanceId, false);
                }
            }
            return available.Where(skill => skill.slot == 1)
                .Select(skill => ChooseTarget(snapshot, actor, skill, false)).FirstOrDefault(choice => choice != null);
        }

        public BattleAiChoice ChooseTarget(BattleSnapshot snapshot, string skillId, bool useSoulBurn = false)
        {
            if (!IsSkillAvailable(snapshot, skillId, useSoulBurn)) return null;
            return ChooseTarget(snapshot, CurrentActor(snapshot), skills[skillId], useSoulBurn);
        }

        private BattleAiChoice ChooseTarget(BattleSnapshot snapshot, BattleUnitSnapshot actor,
            BattleCatalogData.SkillRow skill, bool useSoulBurn)
        {
            string targetRule = BattleTargeting.PrimaryRule(skill, actor);
            var provoker = BattleTargeting.ActiveProvoker(snapshot, actor);
            if (provoker != null)
                return BattleTargeting.CanSelectUnit(snapshot, actor, targetRule, provoker)
                    ? new BattleAiChoice(skill.id, provoker.InstanceId, useSoulBurn) : null;

            BattleUnitSnapshot target;
            switch (targetRule)
            {
                case "self":
                    target = actor;
                    break;
                case "one_ally":
                case "all_allies":
                    target = BestAllyTarget(skill, actor, AliveOnSide(snapshot, actor.Slot.Side).ToArray());
                    break;
                case "one_enemy":
                case "two_enemies":
                case "all_enemies":
                    target = snapshot.Units.Where(unit => BattleTargeting.CanSelectUnit(snapshot, actor, targetRule, unit))
                        .OrderByDescending(unit => EpicSevenCombatMath.ElementRelation(actor.Element, unit.Element) > 0)
                        .ThenBy(HealthRatio).ThenBy(unit => unit.Slot.Position).FirstOrDefault();
                    break;
                default:
                    return null;
            }
            return target == null ? null : new BattleAiChoice(skill.id, target.InstanceId, useSoulBurn);
        }

        private BattleUnitSnapshot BestAllyTarget(BattleCatalogData.SkillRow skill, BattleUnitSnapshot actor,
            BattleUnitSnapshot[] allies)
        {
            bool heals = effects[skill.id].Any(effect => IsHealing(effect.operation));
            return allies.OrderByDescending(unit => CleanseScore(skill, actor, unit, allies))
                .ThenBy(unit => heals && unit.GetStatus(BattleStatusKind.Unhealable) != null)
                .ThenBy(HealthRatio).ThenBy(unit => unit.Slot.Position).FirstOrDefault();
        }

        private int CleanseScore(BattleCatalogData.SkillRow skill, BattleUnitSnapshot actor,
            BattleUnitSnapshot target, BattleUnitSnapshot[] allies)
        {
            int score = 0;
            foreach (var effect in effects[skill.id])
            {
                bool all = effect.operation == "cleanse_all";
                if ((!all && (effect.operation != "cleanse" || effect.value <= 0)) ||
                    effect.condition == "caster_health_below_half" && !IsBelowHalf(actor))
                    continue;
                int maximum = all ? int.MaxValue : effect.value;
                if (effect.recipient == "self")
                    score += CountRemovals(actor, maximum);
                else if (effect.recipient == "allies" || effect.recipient == "targets" && skill.target == "all_allies")
                    score += allies.Sum(unit => CountRemovals(unit, maximum));
                else if (effect.recipient == "targets" || effect.recipient == "target_and_lowest_ally")
                {
                    score += CountRemovals(target, maximum);
                    if (effect.recipient == "target_and_lowest_ally")
                    {
                        var lowest = allies.OrderBy(HealthRatio).ThenBy(unit => unit.Slot.Position).FirstOrDefault();
                        if (lowest != null && lowest != target)
                            score += CountRemovals(lowest, maximum);
                    }
                }
            }
            return score;
        }

        private static int CountRemovals(BattleUnitSnapshot unit, int maximum) =>
            Math.Min(maximum, unit.Statuses.Count(status => BattleSkillResolver.IsHarmful(status.Kind)));

        private static bool IsHealing(string operation) => operation == "heal" || operation == "heal_target_health";
        private static bool TargetsAllies(string target) => target == "one_ally" || target == "all_allies" || target == "self";
        private static bool IsBelowHalf(BattleUnitSnapshot unit) => 2L * unit.CurrentHealth < unit.MaximumHealth;
        private static decimal HealthRatio(BattleUnitSnapshot unit) => unit.CurrentHealth / (decimal)unit.MaximumHealth;

        private static IEnumerable<BattleUnitSnapshot> AliveOnSide(BattleSnapshot snapshot, BattleSide side) =>
            snapshot.Units.Where(unit => unit.IsAlive && unit.Slot.Side == side);

        private static BattleUnitSnapshot CurrentActor(BattleSnapshot snapshot) =>
            snapshot?.Outcome == BattleOutcome.InProgress ? snapshot.GetUnit(snapshot.CurrentActorId) : null;
    }
}
