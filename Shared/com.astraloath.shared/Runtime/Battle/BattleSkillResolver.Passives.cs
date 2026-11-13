using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    public sealed partial class BattleSkillResolver
    {
        IReadOnlyList<BattleChange> IBattleLifecycleResolver.BattleStart(BattleSnapshot snapshot) =>
            new Execution(this, snapshot, new BattleRandom(0)).ApplyLifecycle("battle_start", null);

        IReadOnlyList<BattleChange> IBattleLifecycleResolver.TurnEnd(BattleSnapshot snapshot, string actorId) =>
            new Execution(this, snapshot, new BattleRandom(0)).ApplyLifecycle("turn_end", actorId);

        private sealed partial class Execution
        {
            private IEnumerable<Program> Passives(Unit unit) => unit.Source.Form.SkillIds
                .Select(id => owner.skills.GetValueOrDefault(id)).Where(program => program?.Row.target == "passive");

            public IReadOnlyList<BattleChange> ApplyLifecycle(string phase, string actorId)
            {
                foreach (var actor in units)
                {
                    if (!actor.Alive || (actorId != null && actorId != actor.Id)) continue;
                    foreach (var passive in Passives(actor))
                    foreach (var effect in passive.Effects)
                    {
                        if (effect.phase != phase) continue;
                        if (effect.recipient != "self" || effect.chanceBp != Basis)
                            throw new InvalidOperationException($"Unsupported lifecycle effect {effect.id}.");
                        if (!ConditionMet(effect, actor, actor, false)) continue;
                        if (phase == "battle_start" && effect.operation == "opening_attack_from_health")
                            Changes.Add(BattleChange.OpeningAttackBonus(actor.Id, ScaleHealth(actor.MaximumHealth, effect.value)));
                        else if (effect.operation == "apply_status")
                            ApplyEffect(actor, actor, passive.Row, effect, owner.ModifiersFor(actor, passive), false,
                                BattleHitKind.None, null, false);
                        else throw new InvalidOperationException($"Unsupported lifecycle effect {effect.id}.");
                    }
                }
                return Changes;
            }

            private void ResolveAttackedPassives(Unit attacker, Unit[] targets, HashSet<string> attacked,
                HashSet<string> criticallyHit)
            {
                foreach (var target in targets)
                {
                    if (!target.Alive || !attacked.Contains(target.Id)) continue;
                    foreach (var passive in Passives(target))
                    foreach (var effect in passive.Effects)
                    {
                        if (effect.phase != "on_attacked" || !ConditionMet(effect, target, target, false)) continue;
                        if (effect.operation == "cleanse_once_on_critical")
                        {
                            if (criticallyHit?.Contains(target.Id) == true &&
                                !target.TriggeredTurnEffects.Contains(effect.id))
                            {
                                var debuff = target.Statuses.FirstOrDefault(item => IsHarmful(item.Kind));
                                if (debuff == null) continue;
                                target.Statuses.Remove(debuff);
                                target.TriggeredTurnEffects.Add(effect.id);
                                Changes.Add(BattleChange.RemoveStatus(target.Id, target.Id, debuff));
                                Changes.Add(BattleChange.MarkTurnEffect(target.Id, effect.id));
                            }
                            continue;
                        }
                        var modifiers = owner.ModifiersFor(target, passive);

                        if (effect.operation == "counterattack" || effect.operation == "readiness_bonus") continue;
                        if (effect.operation == "readiness")
                            modifiers.ReadinessBp += SkillModifier(passive, "readiness_bonus", target, target, false);
                        ApplyEffect(target, target, passive.Row, effect, modifiers, false,
                            BattleHitKind.None, null, false);
                    }
                    var resource = target.Source.ResourceRules;
                    if (resource != null) ChangeResource(attacker, target, resource.AttackedGain - resource.AttackedSpend);
                }
            }

            private void ResolveAllyAttackPassives(Unit attacker, Unit[] targets, HashSet<string> attacked)
            {
                if (attacked.Count == 0) return;
                const string phase = "after_ally_attack";
                foreach (var ally in Allies(attacker))
                {
                    if (!CanReact(ally)) continue;
                    foreach (var passive in Passives(ally))
                    {
                        var trigger = passive.AllyAttackTrigger;
                        if (trigger == null || ally.Cooldowns.GetValueOrDefault(passive.Row.id) > 0) continue;
                        var target = targets.FirstOrDefault(candidate => candidate.Alive && attacked.Contains(candidate.Id) &&
                            ConditionMet(trigger, ally, candidate, false));
                        if (target == null || !ally.TriggeredPassives.Add(passive.Row.id)) continue;
                        if (passive.Row.cooldownTurns > 0)
                            ChangeCooldown(ally, ally, passive.Row.id, passive.Row.cooldownTurns);
                        Cast(ally, passive, target, false, BattleActionOrigin.Passive, phase);
                    }
                }
            }

            private void ResolveAllySkillEffects(Unit caster)
            {
                bool? enemyHasDebuff = null;
                foreach (var listener in Allies(caster))
                {
                    if (listener == caster) continue;
                    foreach (string id in listener.Source.Form.SkillIds)
                    {
                        if (!owner.skills.TryGetValue(id, out var program) || program.AllySkillEffects.Length == 0 ||
                            listener.Cooldowns.GetValueOrDefault(id) > 0) continue;
                        foreach (var effect in program.AllySkillEffects)
                        {
                            if (effect.condition == "enemy_debuffed")
                            {
                                enemyHasDebuff ??= units.Any(unit => unit.Alive && unit.Side != caster.Side &&
                                    unit.Statuses.Any(status => IsHarmful(status.Kind)));
                                if (!enemyHasDebuff.Value) continue;
                            }
                            ApplyEffect(listener, listener, program.Row, effect, owner.ModifiersFor(listener, program),
                                false, BattleHitKind.None, null, false);
                        }
                    }
                }
            }

            private void ResolveCriticalPassives(Unit actor)
            {
                foreach (var passive in Passives(actor))
                foreach (var effect in passive.CriticalEffects)
                {
                    if (!ConditionMet(effect, actor, actor, false)) continue;
                    var modifiers = owner.ModifiersFor(actor, passive);
                    if (effect.operation == "random_buff")
                    {
                        if (actor.TriggeredPassives.Contains(effect.id) ||
                            !Roll(random, Math.Min(Basis, effect.chanceBp + modifiers.ChanceBp))) continue;
                        actor.TriggeredPassives.Add(effect.id);
                        var options = passive.CriticalBuffOptions;
                        var option = options[(int)(random.NextUInt64() % (ulong)options.Length)];
                        ApplyStatus(actor, actor, ParseStatus(option.status), option.turns, option.value);
                    }
                    else ApplyEffect(actor, actor, passive.Row, effect, modifiers, false,
                        BattleHitKind.None, null, false);
                }
            }

            private bool RollsCounter(Unit actor)
            {
                if (Roll(random, actor.Strength(BattleStatusKind.Counter))) return true;
                foreach (var passive in Passives(actor))
                foreach (var effect in passive.Effects)
                    if (effect.phase == "on_attacked" && effect.operation == "counterattack" &&
                        ConditionMet(effect, actor, actor, false) &&
                        Roll(random, Math.Min(Basis, effect.chanceBp + owner.ModifiersFor(actor, passive).CounterChanceBp)))
                        return true;
                return false;
            }

            private int DamageReduction(Unit recipient, bool allTargets)
            {
                int highest = 0;
                foreach (var ally in Allies(recipient))
                foreach (var passive in Passives(ally))
                foreach (var effect in passive.Effects)
                {
                    if (effect.phase != "damage_guard" || effect.operation != "damage_reduction" ||
                        (effect.recipient == "self" && ally != recipient)) continue;
                    if (ConditionMet(effect, ally, recipient, allTargets)) highest = Math.Max(highest, effect.value);
                }
                return Math.Min(highest, Basis);
            }

            private static int SkillModifier(Program program, string operation, Unit caster, Unit recipient, bool allTargets,
                bool speedBuffed = false)
            {
                if (program == null) return 0;
                int amount = 0;
                foreach (var effect in program.Effects)
                    if (effect.operation == operation && ConditionMet(effect, caster, recipient, allTargets, speedBuffed: speedBuffed))
                        amount += effect.value;
                return amount;
            }

            private static bool ConditionMet(BattleCatalogData.EffectRow effect, Unit caster, Unit recipient, bool allTargets,
                BattleHitKind targetHit = BattleHitKind.None, bool attackCritical = false, bool readinessDrained = false,
                bool speedBuffed = false, bool defeatedTarget = false)
            {
                return effect.condition switch
                {
                    null or "" or "none" => true,
                    "caster_health_below_three_quarters" => 4L * caster.Health < 3L * caster.MaximumHealth,
                    "caster_health_below_half" => 2L * caster.Health < caster.MaximumHealth,
                    "caster_health_at_least_half" => 2L * caster.Health >= caster.MaximumHealth,
                    "caster_health_at_most_half" => 2L * caster.Health <= caster.MaximumHealth,
                    "caster_health_above_half" => 2L * caster.Health > caster.MaximumHealth,
                    "caster_has_resource" => caster.ResourceAmount >= Math.Max(1, effect.turns),
                    "caster_buffed" => HasBuff(caster),
                    "caster_speed_buffed" => speedBuffed,
                    "caster_speed_buffed_after_skill_awakening" => speedBuffed && caster.Source.Awakenings >= 3,
                    "defeated_target" => defeatedTarget,
                    "caster_indomitable" => caster.Has(BattleStatusKind.Indomitable),
                    "caster_not_indomitable" => !caster.Has(BattleStatusKind.Indomitable),
                    "caster_skill_awakened" => caster.Source.Awakenings >= 3,
                    "caster_skill_not_awakened" => caster.Source.Awakenings < 3,
                    "target_readiness_drained" => readinessDrained,
                    "recipient_buffed" => HasBuff(recipient),
                    "recipient_debuffed_after_skill_awakening" => caster.Source.Awakenings >= 3 &&
                        recipient.Statuses.Any(status => IsHarmful(status.Kind)),
                    "recipient_health_at_most_half" => 2L * recipient.Health <= recipient.MaximumHealth,
                    "target_hit_critical" => targetHit == BattleHitKind.Critical,
                    "attack_critical" => attackCritical,
                    "attack_critical_after_skill_awakening" => attackCritical && caster.Source.Awakenings >= 3,
                    "single_target_attack" => !allTargets,
                    _ => throw new InvalidOperationException($"Unknown skill condition '{effect.condition}'.")
                };
            }
        }
    }
}
