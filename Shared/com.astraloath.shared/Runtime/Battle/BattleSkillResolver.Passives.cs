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
                        if (effect.operation != "apply_status" || effect.recipient != "self" || effect.chanceBp != Basis)
                            throw new InvalidOperationException($"Unsupported lifecycle effect {effect.id}.");
                        if (ConditionMet(effect, actor, actor, false))
                            ApplyEffect(actor, actor, passive.Row, effect, owner.ModifiersFor(actor, passive), false,
                                BattleHitKind.None, null, false);
                    }
                }
                return Changes;
            }

            private void ResolveAttackedPassives(Unit attacker, Unit[] targets, HashSet<string> attacked)
            {
                foreach (var target in targets)
                {
                    if (!target.Alive || !attacked.Contains(target.Id)) continue;
                    foreach (var passive in Passives(target))
                    foreach (var effect in passive.Effects)
                    {
                        if (effect.phase != "on_attacked" || !ConditionMet(effect, target, target, false)) continue;
                        var modifiers = owner.ModifiersFor(target, passive);

                        if (effect.operation == "counterattack") continue;
                        ApplyEffect(target, target, passive.Row, effect, modifiers, false,
                            BattleHitKind.None, null, false);
                    }
                    ChangeResource(attacker, target, -(target.Source.ResourceRules?.AttackedSpend ?? 0));
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
                        if (trigger == null || ally.Source.GetCooldown(passive.Row.id) > 0) continue;
                        var target = targets.FirstOrDefault(candidate => candidate.Alive && attacked.Contains(candidate.Id) &&
                            ConditionMet(trigger, ally, candidate, false));
                        if (target == null || !ally.TriggeredPassives.Add(passive.Row.id)) continue;
                        if (passive.Row.cooldownTurns > 0)
                            Changes.Add(BattleChange.Cooldown(ally.Id, ally.Id, passive.Row.id, passive.Row.cooldownTurns));
                        Cast(ally, passive, target, false, BattleActionOrigin.Passive, phase);
                    }
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

            private static int SkillModifier(Program program, string operation, Unit caster, Unit recipient, bool allTargets)
            {
                if (program == null) return 0;
                int amount = 0;
                foreach (var effect in program.Effects)
                    if (effect.operation == operation && ConditionMet(effect, caster, recipient, allTargets))
                        amount += effect.value;
                return amount;
            }

            private static bool ConditionMet(BattleCatalogData.EffectRow effect, Unit caster, Unit recipient, bool allTargets)
            {
                return effect.condition switch
                {
                    null or "" or "none" => true,
                    "caster_health_below_half" => 2L * caster.Health < caster.MaximumHealth,
                    "caster_health_at_least_half" => 2L * caster.Health >= caster.MaximumHealth,
                    "caster_health_at_most_half" => 2L * caster.Health <= caster.MaximumHealth,
                    "caster_health_above_half" => 2L * caster.Health > caster.MaximumHealth,
                    "caster_has_resource" => caster.ResourceAmount >= Math.Max(1, effect.turns),
                    "caster_buffed" => HasBuff(caster),
                    "recipient_buffed" => HasBuff(recipient),
                    "recipient_health_at_most_half" => 2L * recipient.Health <= recipient.MaximumHealth,
                    "single_target_attack" => !allTargets,
                    _ => throw new InvalidOperationException($"Unknown skill condition '{effect.condition}'.")
                };
            }
        }
    }
}
