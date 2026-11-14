using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    // A program runs synchronously against a private projection. Only BattleMatch commits its typed changes.
    public sealed partial class BattleSkillResolver : IBattleActionResolver, IBattleLifecycleResolver
    {
        private readonly Dictionary<string, Program> skills;
        private readonly Func<string, string, int> enhancementRank;
        private readonly int randomDualChanceBp;

        public BattleSkillResolver(BattleCatalogData catalog, bool includePreviewSlime = false,
            Func<string, string, int> enhancementRank = null, int randomDualChanceBp = 300)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (catalog.schemaVersion != 2 || catalog.skills == null || catalog.effects == null || catalog.enhancements == null)
                throw new ArgumentException("Battle skill catalog is incomplete.", nameof(catalog));
            if (randomDualChanceBp < 0 || randomDualChanceBp > Basis)
                throw new ArgumentOutOfRangeException(nameof(randomDualChanceBp));
            this.enhancementRank = enhancementRank ?? ((_, _) => 0);
            this.randomDualChanceBp = randomDualChanceBp;
            var effectGroups = catalog.effects.ToLookup(row => row.skillId, StringComparer.Ordinal);
            var rankGroups = catalog.enhancements.ToLookup(row => row.skillId, StringComparer.Ordinal);
            skills = catalog.skills.ToDictionary(row => row.id,
                row => new Program(row, effectGroups[row.id], rankGroups[row.id]), StringComparer.Ordinal);
            if (catalog.effects.Any(row => !skills.ContainsKey(row.skillId)))
                throw new ArgumentException("A skill effect refers to an unknown skill.", nameof(catalog));
            if (includePreviewSlime)
            {
                var slime = CreatePreviewSlimeSkill();
                skills.Add(slime.id, new Program(slime, new[]
                {
                    new BattleCatalogData.EffectRow { order = 1, phase = "impact", operation = "damage", recipient = "targets", status = "none", chanceBp = Basis }
                }, Array.Empty<BattleCatalogData.EnhancementRow>()));
            }
        }

        public bool TryResolve(BattleActionContext context, BattleSkillCommand command, BattleRandom random,
            out BattleActionResolution resolution)
        {
            resolution = null;
            var work = new Execution(this, context.Snapshot, random);
            var actor = work.Get(command.ActorId);
            if (actor == null || !skills.TryGetValue(command.SkillId, out var program) ||
                !CanUseSkill(context.Snapshot, actor.Source, program.Row, command.UseSoulBurn, ModifiersFor(actor, program).ResourceCost))
                return false;

            var selected = work.Get(command.TargetId);
            string targetRule = BattleTargeting.PrimaryRule(program.Row, actor.Source);
            if (!BattleTargeting.CanSelect(context.Snapshot, actor.Source, targetRule, command.TargetId)) return false;
            var modifiers = ModifiersFor(actor, program);
            int cooldown = Math.Max(0, program.Row.cooldownTurns - modifiers.CooldownTurns);
            if (command.UseSoulBurn)
            {
                work.Changes.Add(BattleChange.Soul(actor.Id, actor.Side, -program.Row.soulBurnCost));
                if (program.Row.soulBurnKind == "reduce_cooldown")
                    cooldown = Math.Max(0, cooldown - program.Row.soulBurnTurns);
            }
            if (cooldown > 0)
                work.ChangeCooldown(actor, actor, program.Row.id, cooldown);
            actor.Readiness = 0;
            string presentationId = work.Cast(actor, program, selected, command.UseSoulBurn, BattleActionOrigin.Primary);
            if (command.UseSoulBurn && actor.Alive)
            {
                if (program.Row.soulBurnKind == "extra_turn") work.Changes.Add(BattleChange.ExtraTurn(actor.Id));
                if (program.Row.soulBurnKind == "refill_resource")
                    work.ChangeResource(actor, actor, actor.Source.Resource?.Maximum ?? 0);
            }
            int soulGain = program.Row.soulGain + modifiers.SoulGain;
            if (soulGain > 0)
                work.Changes.Add(BattleChange.Soul(actor.Id, actor.Side, soulGain));
            resolution = new BattleActionResolution(work.Changes, presentationId);
            return true;
        }

        internal static BattleCatalogData.SkillRow CreatePreviewSlimeSkill() => new BattleCatalogData.SkillRow
        {
            id = BattlePreviewFactory.SlimeSkillId, heroId = "preview.slime", slot = 1,
            target = "one_enemy", soulBurnKind = "none", attackRateBp = 10000, powerBp = 10000
        };

        internal static bool CanUseSkill(BattleSnapshot snapshot, BattleUnitSnapshot actor,
            BattleCatalogData.SkillRow skill, bool useSoulBurn, int resourceDiscount = 0) =>
            actor.IsAlive && skill.target != "passive" && actor.GetCooldown(skill.id) == 0 &&
            (skill.slot == 1 && !useSoulBurn || BattleTargeting.ActiveProvoker(snapshot, actor) == null) &&
            (skill.slot <= 1 || actor.GetStatus(BattleStatusKind.Silence) == null) &&
            (actor.Resource?.Amount ?? 0) >= Math.Max(0, skill.resourceCost - resourceDiscount) &&
            (!useSoulBurn || skill.soulBurnCost > 0 && snapshot.GetSoul(actor.Slot.Side) >= skill.soulBurnCost);

        internal static bool IsHarmful(BattleStatusKind kind) =>
            kind == BattleStatusKind.Stun || kind == BattleStatusKind.SpeedDown ||
            kind == BattleStatusKind.AttackDown || kind == BattleStatusKind.DefenseDown ||
            kind == BattleStatusKind.Poison || kind == BattleStatusKind.Burn || kind == BattleStatusKind.Bleed ||
            kind == BattleStatusKind.HitChanceDown || kind == BattleStatusKind.Unbuffable || kind == BattleStatusKind.Unhealable || kind == BattleStatusKind.Silence || kind == BattleStatusKind.Provoke;

        private Modifiers ModifiersFor(Unit actor, Program program)
        {
            return ModifiersFor(actor.Id, program);
        }

        private Modifiers ModifiersFor(string actorId, Program program)
        {
            int rank = enhancementRank(actorId, program.Row.id);
            if (rank < 0 || rank >= program.Modifiers.Length)
                throw new ArgumentOutOfRangeException(nameof(rank), "Skill enhancement is outside the loaded program.");
            return program.Modifiers[rank];
        }

        private sealed class Program
        {
            public readonly BattleCatalogData.SkillRow Row;
            public readonly BattleCatalogData.EffectRow[] Effects;
            public readonly Modifiers[] Modifiers;
            public readonly BattleCatalogData.EffectRow AllyAttackTrigger;
            public readonly BattleCatalogData.EffectRow[] CriticalEffects;
            public readonly BattleCatalogData.EffectRow[] AllySkillEffects;
            public readonly bool TracksHealthLoss;
            public readonly BattleCatalogData.EffectRow[] CriticalBuffOptions;

            public Program(BattleCatalogData.SkillRow row, IEnumerable<BattleCatalogData.EffectRow> effects,
                IEnumerable<BattleCatalogData.EnhancementRow> ranks)
            {
                Row = row.Copy();
                Effects = effects.OrderBy(item => item.order).Select(item => item.Copy()).ToArray();
                TracksHealthLoss = Effects.Any(effect => effect.operation == "lifesteal" || effect.operation == "injury");
                if ((row.target != "passive" && Effects.Length == 0) || Effects.Length > 64)
                    throw new ArgumentException($"Skill '{row.id}' has an invalid number of effects.");
                for (int i = 0; i < Effects.Length; i++)
                {
                    var effect = Effects[i];
                    if (effect.order != i + 1 || effect.chanceBp < 1 || effect.chanceBp > Basis)
                        throw new ArgumentException($"Skill '{row.id}' has an invalid effect sequence.");
                }
                AllyAttackTrigger = Effects.SingleOrDefault(effect =>
                    effect.phase == "after_ally_attack" && effect.operation == "trigger");
                if (AllyAttackTrigger == null && Effects.Any(effect => effect.phase == "after_ally_attack"))
                    throw new ArgumentException($"Passive attack '{row.id}' has no trigger.");
                AllySkillEffects = Effects.Where(effect => effect.phase == "after_ally_skill").ToArray();
                CriticalEffects = Effects.Where(effect => effect.phase == "after_caster_critical" &&
                    effect.operation != "random_buff_option").ToArray();
                CriticalBuffOptions = Effects.Where(effect => effect.phase == "after_caster_critical" &&
                    effect.operation == "random_buff_option").ToArray();
                if (CriticalEffects.Count(effect => effect.operation == "random_buff") != (CriticalBuffOptions.Length > 0 ? 1 : 0))
                    throw new ArgumentException($"Passive '{row.id}' has inconsistent random buff options.");
                var rankRows = ranks.OrderBy(item => item.step).ToArray();
                Modifiers = new Modifiers[rankRows.Length + 1];
                for (int i = 0; i < rankRows.Length; i++)
                {
                    var step = rankRows[i];
                    if (step.step != i + 1) throw new ArgumentException($"Skill '{row.id}' has a missing enhancement.");
                    var current = Modifiers[i];
                    switch (step.kind)
                    {
                        case "damage_bp": current.DamageBp += step.value; break;
                        case "healing_bp": current.HealingBp += step.value; break;
                        case "effect_chance_bp": current.ChanceBp += step.value; break;
                        case "cooldown_turns": current.CooldownTurns += step.value; break;
                        case "readiness_bp": current.ReadinessBp += step.value; break;
                        case "soul_gain": current.SoulGain += step.value; break;
                        case "counter_chance_bp": current.CounterChanceBp += step.value; break;
                        case "barrier_bp": current.BarrierBp += step.value; break;
                        case "resource_cost": current.ResourceCost += step.value; break;
                        default: throw new ArgumentException($"Unsupported enhancement '{step.kind}'.");
                    }
                    Modifiers[i + 1] = current;
                }
            }
        }

        private struct Modifiers
        {
            public int DamageBp, HealingBp, ChanceBp, CooldownTurns, ReadinessBp, SoulGain, CounterChanceBp, BarrierBp, ResourceCost;
        }

        private static int ResourceCost(BattleCatalogData.SkillRow skill, Modifiers modifiers) =>
            Math.Max(0, skill.resourceCost - modifiers.ResourceCost);

        private sealed class Unit
        {
            public readonly BattleUnitSnapshot Source;
            public readonly List<BattleStatusSnapshot> Statuses;
            public int Health;
            public int InjuryAmount;
            public readonly HashSet<string> TriggeredTurnEffects;
            public int ResourceAmount;
            public int Readiness;
            public readonly Dictionary<string, int> Cooldowns;
            public readonly HashSet<string> TriggeredPassives = new HashSet<string>(StringComparer.Ordinal);

            public Unit(BattleUnitSnapshot source)
            {
                Source = source;
                Health = source.CurrentHealth;
                InjuryAmount = source.InjuryAmount;
                TriggeredTurnEffects = new HashSet<string>(source.TriggeredTurnEffects, StringComparer.Ordinal);
                ResourceAmount = source.Resource?.Amount ?? 0;
                Readiness = source.ReadinessBp;
                Cooldowns = source.Cooldowns.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                Statuses = source.Statuses.ToList();
            }

            public string Id => Source.InstanceId;
            public BattleSide Side => Source.Slot.Side;
            public bool Alive => Health > 0;
            public int MaximumHealth => Source.Stats.MaxHealth - InjuryAmount;
            public decimal Attack => Source.Stats.Attack * Math.Max(0m, 1m +
                (Math.Max(Strength(BattleStatusKind.AttackUp), Strength(BattleStatusKind.GreaterAttackUp)) +
                    Strength(BattleStatusKind.Rage) - (decimal)Strength(BattleStatusKind.AttackDown)) / Basis) + Source.OpeningAttackBonus;
            public int CriticalChance => Source.Stats.CritChanceBp + Strength(BattleStatusKind.CriticalChanceUp);
            public decimal Defense => ScaleStat(Source.Stats.Defense, BattleStatusKind.DefenseUp, BattleStatusKind.DefenseDown,
                Strength(BattleStatusKind.Indomitable));
            public int EffectResistance => Source.Stats.EffectResistanceBp + Strength(BattleStatusKind.Indomitable);
            public int Effectiveness => Source.Stats.EffectivenessBp + Strength(BattleStatusKind.EffectivenessUp);
            public int CriticalDamage => Math.Min(35000, Source.Stats.CritDamageBp + Strength(BattleStatusKind.CriticalDamageUp));
            public decimal Speed => Source.Stats.Speed * Math.Max(0m, 1m +
                (Math.Max(Strength(BattleStatusKind.SpeedUp), Strength(BattleStatusKind.GreaterSpeedUp)) -
                    (decimal)Strength(BattleStatusKind.SpeedDown) + Strength(BattleStatusKind.Rage)) / Basis);
            public bool HasSpeedBuff => Has(BattleStatusKind.SpeedUp) || Has(BattleStatusKind.GreaterSpeedUp);
            public BattleStatusSnapshot Status(BattleStatusKind kind) => Statuses.FirstOrDefault(item => item.Kind == kind);
            public bool Has(BattleStatusKind kind) => Status(kind) != null;
            public int Strength(BattleStatusKind kind) => Status(kind)?.Strength ?? 0;
            private decimal ScaleStat(int value, BattleStatusKind up, BattleStatusKind down, int bonusBp = 0) =>
                value * Math.Max(0m, 1m + (Strength(up) + bonusBp - (decimal)Strength(down)) / Basis);
        }

        private sealed partial class Execution
        {
            private readonly BattleSkillResolver owner;
            private readonly Unit[] units;
            private readonly BattleRandom random;
            private ulong nextStatusId;
            internal readonly List<BattleChange> Changes = new List<BattleChange>();

            public Execution(BattleSkillResolver owner, BattleSnapshot snapshot, BattleRandom random)
            {
                this.owner = owner;
                this.random = random;
                nextStatusId = snapshot.LastEventSequence;
                units = snapshot.Units.Select(unit => new Unit(unit)).ToArray();
            }

            public Unit Get(string id) => units.FirstOrDefault(unit => unit.Id == id);

            private Unit[] ChooseTargets(string rule, Unit actor, Unit selected)
            {
                if (rule == "all_enemies" || rule == "passive") return units.Where(unit => unit.Alive && unit.Side != actor.Side).ToArray();
                if (rule == "all_allies") return Allies(actor);
                if (rule == "self") return new[] { actor };
                if (rule == "one_enemy" || rule == "one_ally" || rule == "one_other_ally" || rule == "one_ally_including_dead")
                    return new[] { selected };
                if (rule != "two_enemies") throw new InvalidOperationException($"Unknown targeting rule '{rule}'.");
                var other = units.Where(unit => unit.Alive && unit.Side != actor.Side && unit != selected).ToArray();
                return other.Length == 0 ? new[] { selected } : new[] { selected, Choose(other) };
            }

            public string Cast(Unit actor, Program program, Unit selected, bool soulBurn, BattleActionOrigin origin,
                string triggerPhase = null)
            {
                var skill = program.Row;
                var modifiers = owner.ModifiersFor(actor, program);
                int resourceCost = ResourceCost(skill, modifiers);
                if (!actor.Alive || actor.ResourceAmount < resourceCost) return skill.presentationId;
                bool buffed = HasBuff(actor);
                bool speedBuffed = actor.HasSpeedBuff;
                bool speedVariant = speedBuffed && !string.IsNullOrEmpty(skill.speedUpPresentationId);
                string presentationId = speedVariant ? skill.speedUpPresentationId : skill.presentationId;
                if (soulBurn && !string.IsNullOrEmpty(skill.burnPresentationId)) presentationId = skill.burnPresentationId;
                bool nonDualBuffed = buffed && origin != BattleActionOrigin.DualAttack;
                bool fullResource = actor.Source.Resource != null && actor.ResourceAmount == actor.Source.Resource.Maximum;
                var variant = program.Effects.FirstOrDefault(effect => effect.operation == "presentation_from_slot" &&
                    ConditionMet(effect, actor, actor, false, fullResource: fullResource, nonDualBuffed: nonDualBuffed, soulBurn: soulBurn));
                if (variant != null) presentationId = FindProgram(actor, variant.value).Row.presentationId;
                string targeting = skill.target;
                if ((skill.aoeWhenBuffed && origin != BattleActionOrigin.DualAttack && buffed) ||
                    (skill.aoeOutsideTurn && (origin == BattleActionOrigin.Counter || origin == BattleActionOrigin.DualAttack)))
                    targeting = "all_enemies";
                var targets = ChooseTargets(targeting, actor, selected);
                bool allTargetAttack = targeting == "all_enemies" || targeting == "passive";
                bool multiTargetAttack = allTargetAttack || targeting == "two_enemies";
                var attacked = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> readinessDrained = null;
                var highestDefense = skill.highestDefensePenetrationBp.HasValue
                    ? targets.OrderByDescending(target => target.Defense).FirstOrDefault()
                    : null;
                var hit = new Dictionary<string, BattleHitKind>(StringComparer.Ordinal);
                var limitedDamage = new Dictionary<string, int>(StringComparer.Ordinal);
                if (origin != BattleActionOrigin.Primary)
                    Changes.Add(BattleChange.Action(actor.Id, selected?.Id, skill.id, origin, presentationId));
                ChangeResource(actor, actor, -resourceCost);

                HashSet<string> criticallyHit = null;
                bool attackCritical = false;
                bool tracksHealthLoss = program.TracksHealthLoss;
                Dictionary<string, int> damageDealt = null;
                bool defeatedTarget = false;
                var defeated = new HashSet<string>(StringComparer.Ordinal);
                int attackRateOverride = SkillModifier(program, "attack_rate_override", actor, actor, multiTargetAttack,
                    fullResource: fullResource, nonDualBuffed: nonDualBuffed, soulBurn: soulBurn);
                if (origin == BattleActionOrigin.ExtraAttack)
                    attackRateOverride = Math.Max(attackRateOverride, SkillModifier(program, "triggered_attack_rate_override", actor, actor, multiTargetAttack));
                bool ignoresResistance = SkillModifier(program, "ignore_resistance", actor, actor, multiTargetAttack,
                    fullResource: fullResource) > 0;
                int missingTargetsBonus = Math.Max(0, 4 - targets.Length) *
                    SkillModifier(program, "damage_bonus_per_missing_target", actor, actor, multiTargetAttack);
                foreach (var effect in program.Effects)
                {
                    if ((triggerPhase != null && effect.phase != triggerPhase) ||
                        (triggerPhase == null && effect.phase != "impact" && effect.phase != "after_hit" &&
                            effect.phase != "after_attack" && effect.phase != "before_attack")) continue;
                    switch (effect.operation)
                    {
                        case "trigger":
                        case "cooldown_immune":
                        case "presentation_from_slot":
                        case "attack_rate_override":
                        case "triggered_attack_rate_override":
                        case "ignore_resistance":
                        case "target_health_rate_bonus":
                        case "caster_missing_health_damage_bonus":
                        case "damage_bonus_per_missing_target":
                        case "burn_effect_chance_bonus":
                            continue;
                        case "trigger_skill":
                        case "trigger_skill_no_soul":
                            if ((origin == BattleActionOrigin.Primary || origin == BattleActionOrigin.Counter || origin == BattleActionOrigin.DualAttack) &&
                                ConditionMet(effect, actor, actor, multiTargetAttack, soulBurn: soulBurn) &&
                                Roll(random, effect.chanceBp))
                                TriggerSkill(actor, effect, selected);
                            continue;
                        case "grant_soul":
                            if (effect.value > 0) Changes.Add(BattleChange.Soul(actor.Id, actor.Side, effect.value));
                            continue;
                        case "damage_bonus":
                        case "damage_bonus_per_debuff":
                        case "critical_damage_bonus":
                        case "defense_penetration":
                        case "attack_rate_bonus":
                        case "defense_rate_bonus":
                        case "health_rate_bonus":
                        case "burn_health_rate_bonus":
                        case "burn_healing_rate_bonus":
                        case "elemental_advantage":
                            continue;
                        case "lifesteal":
                            if (actor.Alive && damageDealt != null)
                                Heal(actor, actor, damageDealt.Values.Sum(amount => (decimal)amount), effect.value, modifiers.HealingBp);
                            continue;
                        case "grant_extra_turn":
                            if (origin == BattleActionOrigin.Primary && actor.Alive &&
                                ConditionMet(effect, actor, actor, multiTargetAttack, attackCritical: attackCritical, defeatedTarget: defeatedTarget))
                                Changes.Add(BattleChange.ExtraTurn(actor.Id));
                            continue;
                        case "dual_attack":
                            if (origin == BattleActionOrigin.Primary && selected?.Alive == true)
                                ForceDual(actor, selected);
                            continue;
                        case "repeat_if_status":
                            if (origin == BattleActionOrigin.Primary && actor.Has(ParseStatus(effect.status)) &&
                                units.Any(unit => unit.Alive && unit.Side != actor.Side))
                                Cast(actor, program, selected?.Alive == true ? selected : null, false, BattleActionOrigin.ExtraAttack);
                            continue;
                    }

                    foreach (var target in Recipients(effect.recipient, actor, targets))
                    {
                        if (!target.Alive && effect.operation != "revive") continue;
                        bool hasHit = hit.TryGetValue(target.Id, out var lastHit);
                        if (effect.phase == "after_hit" && !hasHit) continue;
                        if (!ConditionMet(effect, actor, target, multiTargetAttack, lastHit, attackCritical,
                            readinessDrained?.Contains(target.Id) == true, speedBuffed, defeatedTarget, fullResource, nonDualBuffed, soulBurn))
                            continue;
                        if (effect.operation == "injury")
                        {
                            if (damageDealt != null && damageDealt.TryGetValue(target.Id, out int lostHealth))
                                ApplyInjury(actor, target, lostHealth, effect.value);
                            continue;
                        }
                        int previousReadiness = target.Readiness;
                        int healthBefore = target.Health;
                        ApplyEffect(actor, target, skill, effect, modifiers, soulBurn, lastHit, hit, target == highestDefense,
                            allTargetAttack, program, multiTargetAttack, speedVariant, speedBuffed,
                            attackRateOverride, ignoresResistance, missingTargetsBonus, fullResource, limitedDamage);
                        if (previousReadiness > 0 && target.Readiness == 0)
                        {
                            readinessDrained ??= new HashSet<string>(StringComparer.Ordinal);
                            readinessDrained.Add(target.Id);
                        }
                        if (effect.operation != "damage" || actor.Side == target.Side) continue;
                        if (tracksHealthLoss)
                        {
                            damageDealt ??= new Dictionary<string, int>(StringComparer.Ordinal);
                            damageDealt[target.Id] = damageDealt.GetValueOrDefault(target.Id) + healthBefore - target.Health;
                        }
                        attacked.Add(target.Id);
                        defeatedTarget |= !target.Alive;
                        if (!target.Alive) defeated.Add(target.Id);
                        if (hit[target.Id] == BattleHitKind.Critical)
                        {
                            attackCritical = true;
                            criticallyHit ??= new HashSet<string>(StringComparer.Ordinal);
                            criticallyHit.Add(target.Id);
                        }
                    }
                }

                int resourceGain = origin != BattleActionOrigin.DualAttack && buffed && skill.resourceGainWhenBuffed > 0
                    ? skill.resourceGainWhenBuffed : skill.resourceGain;
                ChangeResource(actor, actor, resourceGain);

                if (actor.Alive && criticallyHit != null) ResolveCriticalPassives(actor, criticallyHit.Count);
                if (defeated.Count > 0)
                {
                    ResolveEnemyDefeats(actor, defeated.Count);
                    if (triggerPhase != "after_caster_kill") ResolveKillPassives(actor, skill.slot);
                }
                ResolveAttackedPassives(actor, targets, attacked, criticallyHit);
                if (origin == BattleActionOrigin.Primary) ResolveAllySkillEffects(actor);
                if (origin != BattleActionOrigin.Passive) ResolveAllyAttackPassives(actor, targets, attacked);

                // Counters and random Dual Attacks are limited to the primary attack window.
                if (origin != BattleActionOrigin.Primary || hit.Count == 0) return presentationId;
                if (actor.Alive && program.Row.slot == 1 && targeting == "one_enemy" && program.Row.heroId != "preview.slime" &&
                    selected?.Alive == true && (!soulBurn || (skill.soulBurnKind != "increase_damage_no_dual" && skill.soulBurnKind != "increase_healing_no_dual")))
                {
                    var eligible = Allies(actor).Where(unit => unit != actor && CanReact(unit)).ToArray();
                    if (eligible.Length > 0 && Roll(random, owner.randomDualChanceBp))
                        CastBasic(Choose(eligible), selected, BattleActionOrigin.DualAttack);
                }
                foreach (var target in targets)
                {
                    if (!actor.Alive) break;
                    if (hit.ContainsKey(target.Id) && CanReact(target) && RollsCounter(target))
                        CastBasic(target, actor, BattleActionOrigin.Counter);
                }
                return presentationId;
            }

            private void ApplyEffect(Unit actor, Unit target, BattleCatalogData.SkillRow skill, BattleCatalogData.EffectRow effect,
                Modifiers modifiers, bool soulBurn, BattleHitKind lastHit, Dictionary<string, BattleHitKind> hit,
                bool highestDefense, bool allTargetAttack = false, Program program = null, bool multiTargetAttack = false,
                bool speedVariant = false, bool speedBuffed = false, int attackRateOverride = 0,
                bool conditionalIgnoreResistance = false, int missingTargetsBonus = 0, bool fullResource = false,
                Dictionary<string, int> limitedDamage = null)
            {
                int bonusChance = effect.chanceBonusBp > 0 && target.Has(ParseStatus(effect.chanceBonusStatus))
                    ? effect.chanceBonusBp : 0;
                int burnChance = soulBurn && effect.phase == "after_hit"
                    ? SkillModifier(program, "burn_effect_chance_bonus", actor, target, multiTargetAttack) : 0;
                int chance = Math.Min(Basis, effect.chanceBp + modifiers.ChanceBp + bonusChance + burnChance);
                bool ignoresResistance = conditionalIgnoreResistance || (soulBurn && skill.soulBurnKind == "ignore_resistance");
                switch (effect.operation)
                {
                    case "damage":
                        int relation = SkillModifier(program, "elemental_advantage", actor, target, multiTargetAttack,
                            speedBuffed) > 0 ? 1 : ElementRelation(actor.Source.Element, target.Source.Element);
                        var result = RollHit(random, relation, actor.CriticalChance,
                            target.Strength(BattleStatusKind.EvasionUp), target.Strength(BattleStatusKind.CriticalResistanceUp),
                            hitChance: -actor.Strength(BattleStatusKind.HitChanceDown), alwaysCritical: skill.alwaysCritical);
                        int rate = (attackRateOverride > 0 ? attackRateOverride : ((soulBurn ? skill.burnAttackRateBp : null) ??
                            (speedVariant ? skill.speedUpAttackRateBp : null) ?? skill.attackRateBp.Value)) +
                            SkillModifier(program, "attack_rate_bonus", actor, target, multiTargetAttack, fullResource: fullResource);
                        int defenseRate = (skill.casterDefenseDamageBp ?? 0) +
                            SkillModifier(program, "defense_rate_bonus", actor, target, multiTargetAttack);
                        int healthRate = (skill.casterHealthDamageBp ?? 0) +
                            SkillModifier(program, "health_rate_bonus", actor, target, multiTargetAttack);
                        if (soulBurn) healthRate += SkillModifier(program, "burn_health_rate_bonus", actor, target, multiTargetAttack);
                        int penetration = (skill.defensePenetrationBp ?? 0) +
                            (actor.Has(BattleStatusKind.Stealth) ? skill.stealthPenetrationBp ?? 0 : 0) +
                            (highestDefense ? skill.highestDefensePenetrationBp ?? 0 : 0) +
                            SkillModifier(program, "defense_penetration", actor, target, multiTargetAttack);
                        decimal defense = target.Defense * (1m - Math.Min(Basis, penetration) / (decimal)Basis);
                        int speedScale = (soulBurn ? skill.burnSpeedDamagePpm : null) ?? skill.casterSpeedDamagePpm ?? 0;
                        int perDebuff = SkillModifier(program, "damage_bonus_per_debuff", actor, target, multiTargetAttack);
                        int debuffBonus = perDebuff == 0 ? 0 : perDebuff * target.Statuses.Count(status => IsHarmful(status.Kind));
                        int missingHealthRate = SkillModifier(program, "caster_missing_health_damage_bonus", actor, target, multiTargetAttack);
                        decimal damageBonus = actor.Speed * speedScale / 1000000m +
                            (1m - (decimal)actor.Health / actor.MaximumHealth) * missingHealthRate / Basis +
                            (decimal)actor.Health / actor.MaximumHealth * (skill.casterHealthRatioDamageBp ?? 0) / Basis +
                            (HasBuff(target) ? (skill.damageAgainstBuffBp ?? 0) / (decimal)Basis : 0m) +
                            (debuffBonus + missingTargetsBonus + SkillModifier(program, "damage_bonus", actor, target, multiTargetAttack) +
                                Passives(actor).Sum(passive => SkillModifier(passive, "damage_bonus", actor, target, multiTargetAttack))) / (decimal)Basis;
                        int reduction = DamageReduction(target, multiTargetAttack);
                        int criticalDamage = actor.CriticalDamage +
                            SkillModifier(program, "critical_damage_bonus", actor, target, multiTargetAttack);
                        int power = (speedVariant ? skill.speedUpPowerBp : null) ?? skill.powerBp.Value;
                        int damage = Damage(actor.Attack, defense, actor.MaximumHealth, rate, power,
                            healthRate, modifiers.DamageBp, relation, result, criticalDamage,
                            additionalBaseDamage: (actor.Defense * defenseRate + target.MaximumHealth *
                                (decimal)SkillModifier(program, "target_health_rate_bonus", actor, target, multiTargetAttack)) / Basis,
                            damageMultiplier: (1m + damageBonus) * (1m - reduction / (decimal)Basis) *
                                (allTargetAttack && target.Has(BattleStatusKind.Stealth) ? .5m : 1m));
                        hit[target.Id] = result;
                        ProjectDamage(actor, target, damage, result, limitedDamage);
                        break;
                    case "heal":
                    case "heal_target_health":
                        bool scalesFromTarget = effect.operation == "heal_target_health";
                        int healRate = scalesFromTarget ? effect.value : skill.casterHealthHealBp.Value;
                        if (soulBurn && (skill.soulBurnKind == "increase_healing" || skill.soulBurnKind == "increase_healing_no_dual"))
                        {
                            int bonus = SkillModifier(program, "burn_healing_rate_bonus", actor, target, multiTargetAttack);
                            healRate += bonus > 0 ? bonus : healRate;
                        }
                        int maximumHealth = scalesFromTarget ? target.MaximumHealth : actor.MaximumHealth;
                        Heal(actor, target, maximumHealth, healRate, modifiers.HealingBp);
                        break;
                    case "resource_gain":
                        ChangeResource(actor, target, effect.value);
                        break;
                    case "resource_spend_all":
                        ChangeResource(actor, target, -target.ResourceAmount);
                        break;
                    case "revive":
                        Revive(actor, target, effect.value);
                        break;
                    case "extend_buffs":
                        foreach (var status in target.Statuses.Where(item => !IsHarmful(item.Kind) &&
                            item.Kind != BattleStatusKind.SpiritlordProtection).ToArray())
                            UpdateStatusDuration(actor, target, status, effect.value);
                        break;
                    case "readiness":
                    case "readiness_fixed":
                        ChangeReadiness(actor, target, effect.value + (effect.operation == "readiness" ? modifiers.ReadinessBp : 0));
                        break;
                    case "reduce_readiness":
                    case "reduce_readiness_double":
                        if (CheckEffect(actor, target, null, chance, lastHit, hostile: true, checksImmunity: true, ignoresResistance))
                        {
                            int reductionBp = effect.value + modifiers.ReadinessBp;
                            if (effect.operation == "reduce_readiness_double") reductionBp *= 2;
                            ChangeReadiness(actor, target, -reductionBp);
                        }
                        break;
                    case "reset_cooldown":
                        foreach (var id in target.Source.Form.SkillIds)
                        {
                            if (!owner.skills.TryGetValue(id, out var reset) || reset.Row.slot != effect.value || IsCooldownImmune(id)) continue;
                            int remaining = target.Cooldowns.GetValueOrDefault(id);
                            if (remaining > 0) ChangeCooldown(actor, target, id, -remaining);
                        }
                        break;
                    case "reduce_cooldown":
                        foreach (var id in target.Source.Form.SkillIds)
                            if (target.Cooldowns.GetValueOrDefault(id) > 0 && !IsCooldownImmune(id))
                                ChangeCooldown(actor, target, id, -effect.value);
                        break;
                    case "reduce_own_cooldown":
                        if (target.Cooldowns.GetValueOrDefault(skill.id) > 0)
                            ChangeCooldown(actor, target, skill.id, -effect.value);
                        break;
                    case "extend_cooldown":
                        for (int turn = 0; turn < effect.value; turn++)
                        {
                            if (!CheckEffect(actor, target, null, chance, lastHit, hostile: true, checksImmunity: true, ignoresResistance)) continue;
                            foreach (var id in target.Source.Form.SkillIds.Skip(1))
                                if (owner.skills.TryGetValue(id, out var delayed) && delayed.Row.cooldownTurns > 0 && !IsCooldownImmune(id))
                                    ChangeCooldown(actor, target, id, 1);
                        }
                        break;
                    case "cleanse":
                    case "cleanse_all":
                        int maximum = effect.operation == "cleanse_all" ? int.MaxValue : effect.value;
                        foreach (var status in target.Statuses.Where(item => IsHarmful(item.Kind)).Take(maximum).ToArray())
                        {
                            target.Statuses.Remove(status);
                            Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, status));
                        }
                        break;
                    case "dispel":
                    case "dispel_all":
                        var buffs = target.Statuses.Where(item => IsDispellableBuff(item.Kind))
                            .Take(effect.operation == "dispel_all" ? int.MaxValue : effect.value).ToArray();
                        if (effect.operation == "dispel_all" && buffs.Length > 0 &&
                            !CheckEffect(actor, target, null, chance, lastHit, hostile: true, checksImmunity: false, ignoresResistance))
                            break;
                        foreach (var status in buffs)
                            if (effect.operation == "dispel_all" ||
                                CheckEffect(actor, target, status.Kind, chance, lastHit, hostile: true, checksImmunity: false, ignoresResistance))
                            {
                                target.Statuses.Remove(status);
                                Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, status));
                            }
                        break;
                    case "transfer_debuff":
                        foreach (var status in actor.Statuses.Where(item => IsHarmful(item.Kind)).Take(effect.value).ToArray())
                        {
                            if (!CheckEffect(actor, target, status.Kind, chance, lastHit, hostile: true, checksImmunity: true, ignoresResistance)) continue;
                            ApplyStatus(actor, target, status.Kind, status.RemainingTurns, status.Strength, status.SourceId);
                            actor.Statuses.Remove(status);
                            Changes.Add(BattleChange.RemoveStatus(actor.Id, actor.Id, status));
                        }
                        break;
                    case "target_health_barrier":
                    case "apply_status":
                        bool targetHealthBarrier = effect.operation == "target_health_barrier";
                        var kind = targetHealthBarrier ? BattleStatusKind.Barrier : ParseStatus(effect.status);
                        bool harmful = IsHarmful(kind);
                        if (!CheckEffect(actor, target, kind, chance, lastHit, harmful, harmful, ignoresResistance)) break;
                        int duration = effect.turns;
                        if (soulBurn && !harmful && skill.soulBurnKind == "extend_granted_buffs") duration += skill.soulBurnTurns;
                        int strength = effect.value;
                        if (targetHealthBarrier) strength = ScaleHealth(target.MaximumHealth, effect.value + modifiers.BarrierBp);
                        else if (kind == BattleStatusKind.Barrier) strength = BarrierStrength(actor, skill, modifiers.BarrierBp);
                        else if (kind == BattleStatusKind.Burn || kind == BattleStatusKind.Bleed)
                            strength = ScaleHealth(actor.Attack, effect.value);
                        ApplyStatus(actor, target, kind, duration, strength);
                        break;
                    default: throw new InvalidOperationException($"Unknown skill effect '{effect.operation}'.");
                }
            }

            private void Heal(Unit source, Unit target, decimal basis, int rate, int enhancement)
            {
                if (target.Has(BattleStatusKind.Unhealable))
                {
                    Changes.Add(BattleChange.EffectRejected(source.Id, target.Id, BattleStatusKind.Unhealable,
                        BattleEffectRejection.HealingPrevented));
                    return;
                }
                int healing = ScaleHealth(basis, rate, enhancement);
                target.Health += Math.Min(healing, target.MaximumHealth - target.Health);
                Changes.Add(BattleChange.Healing(source.Id, target.Id, healing));
            }

            private void ApplyInjury(Unit source, Unit target, int lostHealth, int limitBp)
            {
                if (!target.Source.IsHero || lostHealth <= 0) return;
                int baseHealth = target.Source.Stats.MaxHealth;
                int maximumThisCast = (int)((long)baseHealth * limitBp / Basis);
                int remainingLimit = baseHealth / 2 - target.InjuryAmount;
                int injury = Math.Min(lostHealth, Math.Min(maximumThisCast, remainingLimit));
                if (injury <= 0) return;
                target.InjuryAmount += injury;
                target.Health = Math.Min(target.Health, target.MaximumHealth);
                Changes.Add(BattleChange.Injury(source.Id, target.Id, injury));
            }

            private void ApplyStatus(Unit actor, Unit target, BattleStatusKind kind, int duration, int strength, string sourceId = null)
            {
                if (!IsHarmful(kind) && kind != BattleStatusKind.SpiritlordProtection && target.Has(BattleStatusKind.Unbuffable))
                {
                    Changes.Add(BattleChange.EffectRejected(actor.Id, target.Id, kind, BattleEffectRejection.Blocked));
                    return;
                }
                sourceId ??= actor.Id;
                if (BattleStatusSnapshot.IsStackable(kind))
                {
                    var added = new BattleStatusSnapshot(kind, duration, strength, sourceId, ++nextStatusId);
                    target.Statuses.Add(added);
                    Changes.Add(BattleChange.ApplyStatus(actor.Id, target.Id, added));
                    return;
                }
                var previous = target.Status(kind);
                if (previous != null)
                {
                    duration = Math.Max(duration, previous.RemainingTurns);
                    strength = Math.Max(strength, previous.Strength);
                }
                SetStatus(target, kind, duration, strength, actor.Id);
                Changes.Add(BattleChange.ApplyStatus(actor.Id, target.Id, kind, duration, strength));
            }

            public void ChangeCooldown(Unit source, Unit target, string skillId, int delta)
            {
                int current = target.Cooldowns.GetValueOrDefault(skillId);
                target.Cooldowns[skillId] = (int)Math.Clamp((long)current + delta, 0L, 60L);
                Changes.Add(BattleChange.Cooldown(source.Id, target.Id, skillId, delta));
            }

            private void ChangeReadiness(Unit source, Unit target, int delta)
            {
                target.Readiness = (int)Math.Clamp((long)target.Readiness + delta, 0L, 1000000L);
                Changes.Add(BattleChange.Readiness(source.Id, target.Id, delta));
            }

            private static bool IsDispellableBuff(BattleStatusKind kind) =>
                !IsHarmful(kind) && kind != BattleStatusKind.Indomitable &&
                kind != BattleStatusKind.Rage && kind != BattleStatusKind.SpiritlordProtection;

            private static int BarrierStrength(Unit actor, BattleCatalogData.SkillRow skill, int enhancementBp)
            {
                if (skill.casterDefenseBarrierBp.HasValue) return ScaleHealth(actor.Defense, skill.casterDefenseBarrierBp.Value + enhancementBp);
                if (skill.casterAttackBarrierBp.HasValue) return ScaleHealth(actor.Attack, skill.casterAttackBarrierBp.Value + enhancementBp);
                return ScaleHealth(actor.MaximumHealth, skill.casterHealthBarrierBp.Value + enhancementBp);
            }

            private bool CheckEffect(Unit source, Unit target, BattleStatusKind? kind, int chance, BattleHitKind hit,
                bool hostile, bool checksImmunity, bool ignoresResistance)
            {
                BattleEffectRejection? reason = null;
                if (hostile && hit == BattleHitKind.Miss)
                    reason = BattleEffectRejection.Missed;
                else if (hostile && checksImmunity && target.Has(BattleStatusKind.Immunity))
                    reason = BattleEffectRejection.Blocked;
                else if (!Roll(random, chance))
                    reason = BattleEffectRejection.ChanceFailed;
                else if (hostile && !ignoresResistance && !Roll(random, EffectSuccessBp(source.Effectiveness, target.EffectResistance)))
                    reason = BattleEffectRejection.Resisted;
                if (!reason.HasValue) return true;
                if (reason != BattleEffectRejection.ChanceFailed)
                    Changes.Add(BattleChange.EffectRejected(source.Id, target.Id, kind, reason.Value));
                return false;
            }

            public void ChangeResource(Unit source, Unit target, int delta)
            {
                if (delta == 0 || target.Source.Resource == null) return;
                int next = (int)Math.Clamp((long)target.ResourceAmount + delta, 0L, target.Source.Resource.Maximum);
                if (next == target.ResourceAmount) return;
                Changes.Add(BattleChange.Resource(source.Id, target.Id, next - target.ResourceAmount));
                target.ResourceAmount = next;
            }

            private void ProjectDamage(Unit source, Unit target, int damage, BattleHitKind hit,
                Dictionary<string, int> limitedDamage = null, BattleStatusSnapshot periodic = null)
            {
                if (target.Has(BattleStatusKind.Invincible))
                {
                    EmitDamage(0);
                    return;
                }
                var barrier = target.Status(BattleStatusKind.Barrier);
                var limit = target.Status(BattleStatusKind.DamageLimit);
                if (limit != null && limitedDamage != null)
                {
                    int alreadyApplied = limitedDamage.GetValueOrDefault(target.Id);
                    damage = Math.Min(damage, Math.Max(0, ScaleHealth(target.MaximumHealth, limit.Strength) - alreadyApplied));
                    limitedDamage[target.Id] = alreadyApplied + damage;
                }
                int floor = target.Has(BattleStatusKind.Immortality) ? 1 : 0;
                var lethal = damage - (barrier?.Strength ?? 0) >= target.Health && floor == 0
                    ? LethalProtection(target) : null;
                if (lethal != null)
                    floor = Math.Min(target.Health, Math.Max(1, ScaleHealth(target.MaximumHealth,
                        lethal.Effects.Single(effect => effect.phase == "on_lethal_damage" && effect.operation == "life_floor").value)));
                damage = (int)Math.Min(damage, Math.Max(0, target.Health - floor) + (long)(barrier?.Strength ?? 0));
                int absorb = Math.Min(damage, barrier?.Strength ?? 0);
                if (absorb > 0)
                {
                    if (absorb == barrier.Strength) target.Statuses.Remove(barrier);
                    else SetStatus(target, barrier.Kind, barrier.RemainingTurns, barrier.Strength - absorb, barrier.SourceId);
                }
                target.Health = Math.Max(0, target.Health - (damage - absorb));
                EmitDamage(damage);
                if (lethal != null) ResolveLethalProtection(target, lethal);
                if (!target.Alive && target.Has(BattleStatusKind.SpiritlordProtection))
                {
                    var protection = target.Status(BattleStatusKind.SpiritlordProtection);
                    Revive(target, target, protection.Strength);
                    ChangeReadiness(target, target, 5000);
                }
                if (damage > absorb)
                {
                    var stealth = target.Status(BattleStatusKind.Stealth);
                    if (stealth != null)
                    {
                        target.Statuses.Remove(stealth);
                        if (target.Alive) Changes.Add(BattleChange.RemoveStatus(source.Id, target.Id, BattleStatusKind.Stealth));
                    }
                }

                void EmitDamage(int amount) => Changes.Add(periodic == null
                    ? BattleChange.Damage(source.Id, target.Id, amount, hit)
                    : BattleChange.PeriodicDamage(periodic, target.Id, amount));
            }

            private Program FindProgram(Unit actor, int slot) => actor.Source.Form.SkillIds
                .Select(id => owner.skills.GetValueOrDefault(id)).Single(program => program?.Row.slot == slot);

            private bool IsCooldownImmune(string skillId) =>
                owner.skills.TryGetValue(skillId, out var program) && program.Effects.Any(effect => effect.operation == "cooldown_immune");

            private void TriggerSkill(Unit actor, BattleCatalogData.EffectRow effect, Unit previousTarget)
            {
                if (!CanReact(actor)) return;
                var program = FindProgram(actor, effect.value);
                Unit target = previousTarget?.Alive == true ? previousTarget :
                    units.FirstOrDefault(unit => unit.Alive && unit.Side != actor.Side);
                if (target == null) return;
                Cast(actor, program, target, false, BattleActionOrigin.ExtraAttack);
                int soul = program.Row.soulGain + owner.ModifiersFor(actor, program).SoulGain;
                if (effect.operation != "trigger_skill_no_soul" && soul > 0)
                    Changes.Add(BattleChange.Soul(actor.Id, actor.Side, soul));
            }

            private void Revive(Unit actor, Unit target, int rate)
            {
                if (target.Alive) return;
                target.Health = Math.Max(1, ScaleHealth(target.MaximumHealth, rate));
                target.Readiness = 0;
                target.Statuses.Clear();
                Changes.Add(BattleChange.Revive(actor.Id, target.Id, target.Health));
            }

            private void UpdateStatusDuration(Unit actor, Unit target, BattleStatusSnapshot status, int extension)
            {
                var updated = new BattleStatusSnapshot(status.Kind, Math.Min(60, status.RemainingTurns + extension),
                    status.Strength, status.SourceId, status.InstanceId);
                target.Statuses[target.Statuses.IndexOf(status)] = updated;
                Changes.Add(BattleChange.UpdateStatusDuration(actor.Id, target.Id, updated));
            }

            private Unit[] Recipients(string selector, Unit actor, Unit[] targets)
            {
                switch (selector)
                {
                    case "self": return new[] { actor };
                    case "allies": return Allies(actor);
                    case "other_allies": return Allies(actor).Where(unit => unit != actor).ToArray();
                    case "targets": return targets;
                    case "target_and_lowest_ally":
                        var selected = targets[0];
                        var lowest = Allies(actor).OrderBy(unit => (decimal)unit.Health / unit.MaximumHealth).First();
                        return lowest == selected ? new[] { selected } : new[] { selected, lowest };
                    default: throw new InvalidOperationException($"Unknown effect recipient '{selector}'.");
                }
            }

            private void ForceDual(Unit actor, Unit target)
            {
                var ally = Allies(actor).Where(unit => unit != actor && CanReact(unit))
                    .OrderByDescending(unit => unit.Attack).FirstOrDefault();
                if (ally != null) CastBasic(ally, target, BattleActionOrigin.DualAttack);
            }

            private void CastBasic(Unit actor, Unit target, BattleActionOrigin origin)
            {
                var skillId = actor.Source.Form.SkillIds.FirstOrDefault();
                if (skillId != null && owner.skills.TryGetValue(skillId, out var program) &&
                    (program.Row.target == "one_enemy" || program.Row.target == "two_enemies" || program.Row.target == "all_enemies"))
                    Cast(actor, program, target, false, origin);
            }

            private Unit Choose(Unit[] candidates) => candidates.Length == 1 ? candidates[0]
                : candidates[(int)(random.NextUInt64() % (ulong)candidates.Length)];
            private Unit[] Allies(Unit actor) => units.Where(unit => unit.Alive && unit.Side == actor.Side).ToArray();
            private static bool HasBuff(Unit unit) => unit.Statuses.Any(status => !IsHarmful(status.Kind));
            private static bool CanReact(Unit unit) => unit.Alive && !unit.Has(BattleStatusKind.Stun);
            private static BattleStatusKind ParseStatus(string value) => Enum.Parse<BattleStatusKind>(value);
            private static void SetStatus(Unit target, BattleStatusKind kind, int turns, int strength, string sourceId)
            {
                int index = target.Statuses.FindIndex(item => item.Kind == kind);
                var status = new BattleStatusSnapshot(kind, turns, strength, sourceId);
                if (index < 0) target.Statuses.Add(status);
                else target.Statuses[index] = status;
            }
        }
    }
}
