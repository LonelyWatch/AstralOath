using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    internal static class BattleArtifactStats
    {
        internal static int SpeedBonus(ArtifactTemplate artifact, int level, int health, int maximum, int stacks) =>
            artifact?.Effect switch
            {
                "attack_speed_stack" => artifact.EffectAt(level) * stacks,
                "low_health_attack_speed" when 2L * health <= maximum => artifact.EffectAt(level),
                "el_fist" => MissingHealthBonus(artifact.EffectAt(level), health, maximum),
                _ => 0
            };

        internal static int MissingHealthBonus(int value, int health, int maximum) =>
            (int)(value * (long)(maximum - health) / maximum);
    }

    public sealed partial class BattleSkillResolver
    {
        private sealed partial class Execution
        {
            private void ApplyArtifactLifecycle(string phase, string actorId)
            {
                if (phase == "battle_start")
                {
                    foreach (var holder in units.Where(unit => unit.Alive && unit.Source.Artifact != null))
                    {
                        switch (holder.ArtifactEffect)
                        {
                            case "opening_soul":
                                ShowArtifact(holder);
                                Changes.Add(BattleChange.Soul(holder.Id, holder.Side, holder.ArtifactValue));
                                break;
                            case "opening_effectiveness":
                                ShowArtifact(holder);
                                ApplyStatus(holder, holder, BattleStatusKind.EffectivenessUp, 1, holder.ArtifactValue);
                                break;
                            case "opening_team_cooldown":
                                if (!Roll(random, holder.ArtifactValue)) break;
                                foreach (var target in Allies(holder)) ReduceArtifactCooldowns(holder, target);
                                break;
                        }
                    }
                    return;
                }
                if (phase != "turn_end") return;
                var actor = Get(actorId);
                if (actor == null || !actor.Alive) return;
                if (actor.ArtifactEffect == "turn_end_extra_turn")
                {
                    if (actor.Counter(ArtifactCounter.Cooldown) > 0)
                        SetArtifactCounter(actor, ArtifactCounter.Cooldown, 0);
                    else if (Roll(random, actor.ArtifactValue))
                    {
                        SetArtifactCounter(actor, ArtifactCounter.Cooldown, 1);
                        AwardArtifactTurn(actor);
                    }
                }
                if (actor.ArtifactEffect == "received_attack_delayed_turn" && actor.Counter(ArtifactCounter.DelayedTurn) == 1)
                {
                    SetArtifactCounter(actor, ArtifactCounter.DelayedTurn, 0);
                    AwardArtifactTurn(actor);
                }
                foreach (var holder in units.Where(unit => unit.Alive && unit.Side != actor.Side &&
                    unit.ArtifactEffect == "enemy_turn_end_readiness"))
                {
                    if (!Roll(random, holder.ArtifactValue)) continue;
                    ShowArtifact(holder);
                    ChangeReadiness(holder, holder, 1500);
                }
            }

            private void StartArtifactTurn(Unit actor)
            {
                foreach (var holder in units.Where(unit => unit.Alive && unit.ArtifactEffect == "turn_start_attack_crit_stack"))
                    IncrementArtifactStack(holder, 10);
                switch (actor.ArtifactEffect)
                {
                    case "turn_heal_attack":
                        if (actor.Health < actor.MaximumHealth)
                        {
                            ShowArtifact(actor);
                            int rate = actor.ArtifactValue;
                            Heal(actor, actor, actor.Attack, 10L * actor.Health <= 3L * actor.MaximumHealth ? rate * 3 / 2 : rate, 0);
                        }
                        break;
                    case "start_resistance_stack":
                        IncrementArtifactStack(actor, 5);
                        break;
                    case "turn_start_ally_cleanse":
                        var afflicted = Allies(actor).Where(ally => ally.Statuses.Any(item => IsHarmful(item.Kind))).ToArray();
                        if (afflicted.Length > 0 && Roll(random, actor.ArtifactValue))
                            CleanseArtifact(actor, Choose(afflicted));
                        break;
                    case "turn_start_attack_buffs":
                        bool attack = Roll(random, actor.Source.Artifact.SecondaryEffectAt(actor.Source.ArtifactLevel));
                        bool greater = Roll(random, actor.ArtifactValue);
                        if (attack || greater) ShowArtifact(actor);
                        if (attack) ApplyStatus(actor, actor, BattleStatusKind.AttackUp, 1, 5000);
                        if (greater) ApplyStatus(actor, actor, BattleStatusKind.GreaterAttackUp, 1, 7500);
                        break;
                    case "received_attack_barrier":
                    case "received_attack_continuous_heal":
                        int cooldown = actor.Counter(ArtifactCounter.Cooldown);
                        if (cooldown > 0) SetArtifactCounter(actor, ArtifactCounter.Cooldown, cooldown - 1);
                        break;
                    case "received_attack_delayed_turn":
                        if (actor.Counter(ArtifactCounter.DelayedTurn) == 2)
                            SetArtifactCounter(actor, ArtifactCounter.DelayedTurn, 1);
                        break;
                }
            }

            private void ApplyReceivedAttackArtifact(Unit attacker, Unit target, BattleHitKind hit)
            {
                switch (target.ArtifactEffect)
                {
                    case "received_attack_cooldown":
                        if (!target.TriggeredTurnEffects.Contains(target.Source.Artifact.Id) &&
                            target.Cooldowns.Values.Any(value => value > 0) && Roll(random, target.ArtifactValue))
                        {
                            MarkArtifactForTurn(target);
                            ReduceArtifactCooldowns(target, target);
                        }
                        break;
                    case "received_attack_barrier":
                        if (target.Counter(ArtifactCounter.Cooldown) == 0 && Roll(random, 5000))
                        {
                            SetArtifactCounter(target, ArtifactCounter.Cooldown, 2);
                            ShowArtifact(target);
                            ApplyStatus(target, target, BattleStatusKind.Barrier, 2,
                                ScaleHealth(target.MaximumHealth, target.ArtifactValue));
                        }
                        break;
                    case "received_attack_continuous_heal":
                        if (target.Counter(ArtifactCounter.Cooldown) == 0 && Roll(random, target.ArtifactValue))
                        {
                            SetArtifactCounter(target, ArtifactCounter.Cooldown, 2);
                            ShowArtifact(target);
                            ApplyStatus(target, target, BattleStatusKind.ContinuousHealing, 2, 1500);
                        }
                        break;
                    case "received_attack_delayed_turn":
                        if (target.Counter(ArtifactCounter.DelayedTurn) == 0 && Roll(random, target.ArtifactValue))
                        {
                            SetArtifactCounter(target, ArtifactCounter.DelayedTurn, 2);
                            ShowArtifact(target);
                        }
                        break;
                    case "evasion_attack_buff":
                        if (hit == BattleHitKind.Miss)
                        {
                            ShowArtifact(target);
                            ApplyStatus(target, target, BattleStatusKind.AttackUp, 1, 5000);
                        }
                        break;
                }
            }

            private void ApplyAfterSkillArtifact(Unit actor, Program program, IReadOnlyList<Unit> targets,
                IReadOnlyDictionary<string, BattleHitKind> hits, IReadOnlyDictionary<string, int> damage,
                BattleActionOrigin origin, bool singleTarget)
            {
                if (!actor.Alive || actor.Source.Artifact == null) return;
                string effect = actor.ArtifactEffect;
                if (hits.Count == 0)
                {
                    if (program.Effects.Any(row => row.operation == "damage") || origin != BattleActionOrigin.Primary) return;
                    if (effect == "nonattack_readiness")
                    {
                        ShowArtifact(actor);
                        ChangeReadiness(actor, actor, actor.ArtifactValue);
                    }
                    if (effect == "nonattack_low_ally_heal")
                    {
                        var recipient = LowestHealthAlly(actor);
                        if (recipient.Health < recipient.MaximumHealth)
                        {
                            ShowArtifact(actor);
                            Heal(actor, recipient, recipient.MaximumHealth, actor.ArtifactValue, 0);
                        }
                    }
                    return;
                }
                var target = singleTarget ? targets[0] : null;
                switch (effect)
                {
                    case "attack_speed_stack":
                        IncrementArtifactStack(actor, 10);
                        break;
                    case "turn_start_attack_crit_stack":
                        SetArtifactCounter(actor, ArtifactCounter.Stack, 0);
                        break;
                    case "single_attack_reduce_readiness":
                        if (target?.Alive == true && CheckEffect(actor, target, null, Basis, hits[target.Id], true, true, false))
                        {
                            ShowArtifact(actor);
                            ChangeReadiness(actor, target, -actor.ArtifactValue);
                        }
                        break;
                    case "attack_cleanse":
                        if (actor.Statuses.Any(item => IsHarmful(item.Kind)) && Roll(random, actor.ArtifactValue))
                            CleanseArtifact(actor, actor);
                        break;
                    case "single_attack_target_mark":
                        if (target?.Alive == true)
                            TryArtifactDebuff(actor, target, hits[target.Id], BattleStatusKind.Targeted, 2, 1500);
                        break;
                    case "attack_stun":
                        foreach (var victim in targets.Where(unit => unit.Alive && hits.ContainsKey(unit.Id)))
                            TryArtifactDebuff(actor, victim, hits[victim.Id], BattleStatusKind.Stun, 1, 0);
                        break;
                    case "attack_low_ally_heal":
                        if (damage != null && damage.Values.Any(amount => amount > 0))
                        {
                            ShowArtifact(actor);
                            Heal(actor, LowestHealthAlly(actor), damage.Values.Sum(amount => (decimal)amount), actor.ArtifactValue, 0);
                        }
                        break;
                    case "sigurd":
                        if (2L * actor.Health <= actor.MaximumHealth && damage != null && damage.Values.Any(amount => amount > 0))
                        {
                            ShowArtifact(actor);
                            Heal(actor, actor, damage.Values.Sum(amount => (decimal)amount),
                                actor.Source.Artifact.SecondaryEffectAt(actor.Source.ArtifactLevel), 0);
                        }
                        break;
                    case "basic_repeat":
                        if (target?.Alive == true && program.Row.slot == 1 && origin != BattleActionOrigin.ExtraAttack &&
                            !actor.TriggeredTurnEffects.Contains(actor.Source.Artifact.Id) && CanReact(actor) &&
                            Roll(random, actor.ArtifactValue))
                        {
                            MarkArtifactForTurn(actor);
                            ShowArtifact(actor);
                            CastBasic(actor, target, BattleActionOrigin.ExtraAttack);
                        }
                        break;
                }
            }

            private int ArtifactDamageBonus(Unit actor, Unit target, int slot)
            {
                int bonus = actor.ArtifactEffect switch
                {
                    "basic_skill_damage" when slot == 1 => actor.ArtifactValue,
                    "high_target_health_damage" when 2L * target.Health > target.MaximumHealth => actor.ArtifactValue,
                    "debuffed_target_damage" when target.Statuses.Any(item => IsHarmful(item.Kind)) => actor.ArtifactValue,
                    _ => 0
                };
                return bonus + target.Strength(BattleStatusKind.Targeted);
            }

            private Unit DamageShareHolder(Unit target) => Allies(target)
                .Where(unit => unit != target && ShareStrength(unit) > 0)
                .OrderByDescending(ShareStrength).FirstOrDefault();

            private void ApplyArtifactDamageShare(Unit attacker, Unit holder, int shared)
            {
                if (holder.ArtifactEffect == "aurius" && holder.ArtifactValue == ShareStrength(holder)) ShowArtifact(holder);
                ProjectDamage(attacker, holder, shared, BattleHitKind.None);
                if (!holder.Alive) ResolveEnemyDefeats(attacker, 1);
            }

            private int DefenseAura(Unit target) => Allies(target).Any(ally => ally.ArtifactEffect == "aurius") ? 1000 : 0;
            private int ResistanceAura(Unit target) => Allies(target).Where(ally => ally.ArtifactEffect == "start_resistance_stack")
                .Sum(ally => ally.ArtifactValue * ally.Counter(ArtifactCounter.Stack));
            private int BasketAttackDuringDual(Unit actor) => units.Any(unit => unit.Alive && unit.Side == actor.Side &&
                unit.Id == activeActorId && unit != actor && unit.ArtifactEffect == "allies_dual_attack") ? 2000 : 0;
            private int DualBonus(Unit helper, Unit actor) =>
                (helper.ArtifactEffect == "off_turn_attack_dual" ? helper.ArtifactValue : 0) +
                (actor.ArtifactEffect == "allies_dual_attack" ? actor.ArtifactValue : 0);

            private static Unit LowestHealthAlly(Unit[] allies) =>
                allies.OrderBy(unit => unit.Health / (decimal)unit.MaximumHealth)
                    .ThenBy(unit => unit.Source.Slot.Position).First();
            private Unit LowestHealthAlly(Unit origin) => LowestHealthAlly(Allies(origin));

            private void TryArtifactDebuff(Unit actor, Unit target, BattleHitKind hit, BattleStatusKind kind, int turns, int strength)
            {
                // Abyssal Crown can trigger on a missed attack. Song of Stars observes hit checking.
                var checkedHit = kind == BattleStatusKind.Stun ? BattleHitKind.None : hit;
                if (!CheckEffect(actor, target, kind, actor.ArtifactValue, checkedHit, true, true, false)) return;
                ShowArtifact(actor);
                ApplyStatus(actor, target, kind, turns, strength);
            }

            private void AwardArtifactTurn(Unit actor)
            {
                ShowArtifact(actor);
                Changes.Add(BattleChange.ExtraTurn(actor.Id));
            }

            private void ReduceArtifactCooldowns(Unit holder, Unit target)
            {
                var ids = target.Source.Form.SkillIds
                    .Where(id => target.Cooldowns.GetValueOrDefault(id) > 0 && !IsCooldownImmune(id)).ToArray();
                if (ids.Length == 0) return;
                ShowArtifact(holder);
                foreach (var id in ids) ChangeCooldown(holder, target, id, -1);
            }

            private void MarkArtifactForTurn(Unit actor)
            {
                string key = actor.Source.Artifact.Id;
                actor.TriggeredTurnEffects.Add(key);
                Changes.Add(BattleChange.MarkTurnEffect(actor.Id, key));
            }

            private void CleanseArtifact(Unit actor, Unit target)
            {
                var debuff = target.Statuses.FirstOrDefault(item => IsHarmful(item.Kind));
                if (debuff == null) return;
                ShowArtifact(actor);
                target.Statuses.Remove(debuff);
                Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, debuff));
            }

            private void IncrementArtifactStack(Unit actor, int maximum)
            {
                int current = actor.Counter(ArtifactCounter.Stack);
                if (current >= maximum) return;
                SetArtifactCounter(actor, ArtifactCounter.Stack, current + 1);
                ShowArtifact(actor);
            }

            private void SetArtifactCounter(Unit actor, ArtifactCounter counter, int value)
            {
                actor.ArtifactCounters[counter] = value;
                Changes.Add(BattleChange.SetArtifactCounter(actor.Id, counter, value));
            }

            private void ShowArtifact(Unit actor)
            {
                string key = "artifact:" + actor.Source.Artifact.Id;
                if (actor.TriggeredPassives.Add(key))
                    Changes.Add(BattleChange.TriggerArtifact(actor.Id, actor.Source.Artifact.Id));
            }
        }
    }
}
