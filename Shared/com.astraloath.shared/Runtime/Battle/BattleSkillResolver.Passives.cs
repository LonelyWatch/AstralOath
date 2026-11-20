using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    public sealed partial class BattleSkillResolver
    {
        IReadOnlyList<BattleChange> IBattleLifecycleResolver.BattleStart(BattleSnapshot snapshot, BattleRandom random) =>
            new Execution(this, snapshot, random).ApplyLifecycle("battle_start", null);

        IReadOnlyList<BattleChange> IBattleLifecycleResolver.StartOfTurn(BattleSnapshot snapshot, string actorId, BattleRandom random) =>
            new Execution(this, snapshot, random).ApplyPeriodicEffects(actorId);

        IReadOnlyList<BattleChange> IBattleLifecycleResolver.TurnEnd(BattleSnapshot snapshot, string actorId, BattleRandom random) =>
            new Execution(this, snapshot, random).ApplyLifecycle("turn_end", actorId);

        private sealed partial class Execution
        {
            private IEnumerable<Program> Passives(Unit unit) => unit.Source.Form.SkillIds
                .Select(id => owner.skills.GetValueOrDefault(id)).Where(program => program?.Row.target == "passive");

            public IReadOnlyList<BattleChange> ApplyLifecycle(string phase, string actorId)
            {
                ApplyArtifactLifecycle(phase, actorId);
                foreach (var actor in units)
                {
                    if (!actor.Alive || (actorId != null && actorId != actor.Id)) continue;
                    foreach (var passive in actor.Source.Form.SkillIds.Select(id => owner.skills.GetValueOrDefault(id)).Where(program => program != null))
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

            public IReadOnlyList<BattleChange> ApplyPeriodicEffects(string actorId)
            {
                var target = Get(actorId);
                foreach (var status in target.Statuses.Where(item => BattleStatusSnapshot.IsStackable(item.Kind)).ToArray())
                {
                    if (!target.Statuses.Contains(status)) break; // A preceding lethal tick may have triggered self-revival.
                    var source = Get(status.SourceId);
                    if (status.Kind == BattleStatusKind.ContinuousHealing)
                    {
                        if (target.Has(BattleStatusKind.Unhealable)) continue;
                        int healing = Math.Min(target.MaximumHealth - target.Health, ScaleHealth(target.MaximumHealth, status.Strength));
                        target.Health += healing;
                        Changes.Add(BattleChange.PeriodicHealing(status, target.Id, healing));
                        continue;
                    }
                    if (status.Kind == BattleStatusKind.Bomb)
                    {
                        if (status.RemainingTurns > 1) continue;
                        ExplodeStatus(source, target, status, 0);
                        if (!target.Alive) break;
                        continue;
                    }
                    int damage = status.Kind == BattleStatusKind.Poison
                        ? ScaleHealth(target.MaximumHealth, status.Strength)
                        : PeriodicDamage(status.Strength, target.Defense);
                    ProjectDamage(source, target, damage, BattleHitKind.None, periodic: status);
                    if (!target.Alive)
                    {
                        ResolveEnemyDefeats(source, 1);
                        break;
                    }
                    if (!target.Statuses.Contains(status)) break; // A self-revive cleared the queue.
                }
                if (target.Alive) StartArtifactTurn(target);
                return Changes;
            }

            private Program LethalProtection(Unit target) =>
                Passives(target).FirstOrDefault(passive => target.Cooldowns.GetValueOrDefault(passive.Row.id) == 0 &&
                    passive.Effects.Any(effect => effect.phase == "on_lethal_damage" && effect.operation == "life_floor"));

            private void ResolveLethalProtection(Unit target, Program program)
            {
                var modifiers = owner.ModifiersFor(target, program);
                ChangeCooldown(target, target, program.Row.id, Math.Max(0, program.Row.cooldownTurns - modifiers.CooldownTurns));
                foreach (var effect in program.Effects.Where(effect => effect.phase == "on_lethal_damage" && effect.operation != "life_floor"))
                    ApplyEffect(target, target, program.Row, effect, modifiers, false, BattleHitKind.None, null, false);
            }

            private void ResolveEnemyDefeats(Unit attacker, int count)
            {
                foreach (var ally in Allies(attacker))
                {
                    if (ally.ArtifactEffect != "enemy_defeated_soul") continue;
                    for (int index = 0; index < count; index++)
                    {
                        if (!Roll(random, ally.ArtifactValue)) continue;
                        ShowArtifact(ally);
                        Changes.Add(BattleChange.Soul(ally.Id, ally.Side, 1));
                    }
                }
                foreach (var ally in Allies(attacker))
                foreach (string id in ally.Source.Form.SkillIds)
                {
                    if (!owner.skills.TryGetValue(id, out var program)) continue;
                    var modifiers = owner.ModifiersFor(ally, program);
                    foreach (var effect in program.Effects.Where(effect => effect.phase == "after_enemy_defeated"))
                        for (int index = 0; index < count; index++)
                            ApplyEffect(ally, ally, program.Row, effect, modifiers, false, BattleHitKind.None, null, false);
                }
            }

            private void ResolveKillPassives(Unit actor, int slot)
            {
                if (!CanReact(actor) || (slot != 1 && slot != 3)) return;
                var target = units.FirstOrDefault(unit => unit.Alive && unit.Side != actor.Side);
                foreach (var passive in Passives(actor))
                {
                    if (!passive.Effects.Any(effect => effect.phase == "after_caster_kill" && effect.operation == "trigger" &&
                        effect.condition == "basic_or_ultimate_kill") ||
                        !actor.TriggeredPassives.Add(passive.Row.id)) continue;
                    Cast(actor, passive, target, false, BattleActionOrigin.Passive, "after_caster_kill");
                }
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
                        bool otherAllyOnly = trigger.condition == "other_ally_attacked_below_thirty_percent";
                        if (otherAllyOnly && ally == attacker) continue;
                        var target = targets.FirstOrDefault(candidate => attacked.Contains(candidate.Id) &&
                            (otherAllyOnly ? 10L * candidate.Health <= 3L * candidate.MaximumHealth :
                                candidate.Alive && ConditionMet(trigger, ally, candidate, false)));
                        if (target != null && !target.Alive)
                            target = units.FirstOrDefault(unit => unit.Alive && unit.Side != attacker.Side);
                        if (target == null || !ally.TriggeredPassives.Add(passive.Row.id)) continue;
                        if (passive.Row.cooldownTurns > 0)
                            ChangeCooldown(ally, ally, passive.Row.id, passive.Row.cooldownTurns);
                        Cast(ally, passive, target, false, BattleActionOrigin.Passive, phase);
                    }
                }
            }

            private void ResolveAllyDamagedPassives(Unit attacker, Unit[] targets, HashSet<string> attacked, HashSet<string> criticallyHit)
            {
                if (attacked.Count == 0) return;
                foreach (var guardian in units.Where(unit => unit.Side != attacker.Side && unit.Alive))
                foreach (var passive in Passives(guardian))
                {
                    var trigger = passive.AllyDamagedTrigger;
                    if (trigger == null || guardian.Cooldowns.GetValueOrDefault(passive.Row.id) > 0) continue;
                    var ward = targets.FirstOrDefault(unit => unit.Side == guardian.Side && attacked.Contains(unit.Id) &&
                        (unit.Alive || trigger.condition == "ally_suffered_critical") && MatchesTrigger(unit));
                    if (ward == null || !Roll(random, trigger.chanceBp)) continue;
                    int awakenedReduction = guardian.Source.Awakenings >= 3
                        ? passive.Effects.Where(e => e.operation == "awakened_cooldown_reduction").Sum(e => e.value) : 0;
                    int cooldown = Math.Max(0, passive.Row.cooldownTurns - owner.ModifiersFor(guardian, passive).CooldownTurns - awakenedReduction);
                    if (cooldown > 0) ChangeCooldown(guardian, guardian, passive.Row.id, cooldown);
                    Cast(guardian, passive, ward, false, BattleActionOrigin.Passive, trigger.phase);

                    bool MatchesTrigger(Unit unit) => trigger.condition switch
                    {
                        "ally_suffered_critical" => criticallyHit?.Contains(unit.Id) == true && CanReact(guardian),
                        "back_row_immobilized" => unit.Source.Slot.Position == 4 && unit.Has(BattleStatusKind.Stun) && CanReact(guardian),
                        _ => ConditionMet(trigger, guardian, unit, false)
                    };
                }
            }

            private int AttackDamageLimit(Unit target)
            {
                int limit = target.Status(BattleStatusKind.DamageLimit)?.Strength ?? Basis;
                foreach (var passive in Passives(target))
                foreach (var effect in passive.Effects)
                    if (effect.operation == "attack_damage_limit")
                        limit = Math.Min(limit, effect.value - owner.ModifiersFor(target, passive).DamageLimitReductionBp);
                return Math.Max(0, limit);
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

            private void ResolveCriticalPassives(Unit actor, int criticalTargets)
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
                    else if (effect.operation == "readiness")
                    {
                        foreach (var target in Recipients(effect.recipient, actor, Array.Empty<Unit>()))
                            ChangeReadiness(actor, target, (effect.value + modifiers.ReadinessBp) * criticalTargets);
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

            private int DamageReduction(Unit recipient, bool allTargets, BattleHitKind hit)
            {
                int highest = recipient.ArtifactEffect == "received_attack_damage_reduction" ? recipient.ArtifactValue : 0;
                if (hit == BattleHitKind.Critical)
                    highest = Math.Max(highest, Allies(recipient).Where(ally => ally.ArtifactEffect == "team_critical_reduction")
                        .Select(ally => ally.ArtifactValue).DefaultIfEmpty(0).Max());
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
                bool speedBuffed = false, bool fullResource = false, bool nonDualBuffed = false, bool soulBurn = false)
            {
                if (program == null) return 0;
                int amount = 0;
                foreach (var effect in program.Effects)
                    if (effect.operation == operation && ConditionMet(effect, caster, recipient, allTargets, speedBuffed: speedBuffed,
                        fullResource: fullResource, nonDualBuffed: nonDualBuffed, soulBurn: soulBurn))
                        amount += effect.value;
                return amount;
            }

            private static bool ConditionMet(BattleCatalogData.EffectRow effect, Unit caster, Unit recipient, bool allTargets,
                BattleHitKind targetHit = BattleHitKind.None, bool attackCritical = false, bool readinessDrained = false,
                bool speedBuffed = false, bool defeatedTarget = false, bool fullResource = false,
                bool nonDualBuffed = false, bool soulBurn = false)
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
                    "caster_buffed_non_dual" => nonDualBuffed,
                    "caster_buffed_non_dual_burn" => nonDualBuffed && soulBurn,
                    "resource_full" => fullResource,
                    "resource_not_full" => !fullResource,
                    "resource_full_after_awakening" => fullResource && caster.Source.Awakenings >= 3,
                    "caster_at_least_three_buffs" => caster.Statuses.Count(status => !IsHarmful(status.Kind)) >= 3,
                    "caster_fewer_than_three_buffs" => caster.Statuses.Count(status => !IsHarmful(status.Kind)) < 3,
                    "recipient_fire" => recipient.Source.Element == BattleElement.Fire,
                    "recipient_bleeding" => recipient.Has(BattleStatusKind.Bleed),
                    "caster_enraged" => caster.Has(BattleStatusKind.Rage),
                    "soul_burn" => soulBurn,
                    "no_soul_burn" => !soulBurn,
                    "caster_speed_buffed" => speedBuffed,
                    "caster_speed_buffed_after_skill_awakening" => speedBuffed && caster.Source.Awakenings >= 3,
                    "defeated_target" => defeatedTarget,
                    "caster_indomitable" => caster.Has(BattleStatusKind.Indomitable),
                    "caster_not_indomitable" => !caster.Has(BattleStatusKind.Indomitable),
                    "caster_skill_awakened" => caster.Source.Awakenings >= 3,
                    "caster_skill_not_awakened" => caster.Source.Awakenings < 3,
                    "target_readiness_drained" => readinessDrained,
                    "recipient_lower_attack" => recipient.Attack < caster.Attack,
                    "recipient_above_thirty_percent" => 10L * recipient.Health > 3L * recipient.MaximumHealth,
                    "recipient_low_thirty_awakened" => 10L * recipient.Health <= 3L * recipient.MaximumHealth && caster.Source.Awakenings >= 3,
                    "recipient_low_thirty_unawakened" => 10L * recipient.Health <= 3L * recipient.MaximumHealth && caster.Source.Awakenings < 3,
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
