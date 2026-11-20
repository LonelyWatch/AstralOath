using System;
using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    public sealed partial class BattleSkillResolver
    {
        private sealed partial class Execution
        {
            private BattleCatalogData.EffectRow ChooseWeightedAttack(Program skill, bool burn)
            {
                var options = skill.WeightedAttacks;
                if (options.Length == 0) return null;
                if (burn) return options[options.Length - 1];
                int roll = (int)(random.NextUInt64() % Basis);
                foreach (var option in options)
                {
                    roll -= option.chanceBp;
                    if (roll < 0) return option;
                }
                throw new InvalidOperationException($"Invalid attack weights for {skill.Row.id}.");
            }

            private void InitializePassiveStats(Unit unit)
            {
                foreach (var program in Passives(unit))
                foreach (var stat in program.Effects.Where(e => e.phase == "passive"))
                {
                    int bonus = stat.value + owner.ModifiersFor(unit, program).PassiveStatBp;
                    switch (stat.operation)
                    {
                        case "passive_attack": unit.HighHealthAttack += bonus; break;
                        case "passive_defense": unit.LowHealthDefense += bonus; break;
                        case "passive_critical_chance": unit.PassiveCriticalChance += bonus; break;
                        case "passive_critical_resistance": unit.PassiveCriticalResistance += bonus; break;
                    }
                }
            }

            private void CleanseAtTurnStart(Unit actor)
            {
                foreach (var passive in Passives(actor))
                foreach (var row in passive.Effects.Where(e => e.phase == "turn_start" && e.operation == "cleanse"))
                {
                    var candidates = Allies(actor).Where(unit => unit.Statuses.Any(s => IsHarmful(s.Kind))).ToList();
                    int count = Math.Min(2, candidates.Count);
                    for (int i = 0; i < count; ++i)
                    {
                        var target = candidates.Count > count - i ? Choose(candidates.ToArray()) : candidates[0];
                        candidates.Remove(target);
                        ApplyEffect(actor, target, passive.Row, row, owner.ModifiersFor(actor, passive), false,
                            BattleHitKind.None, null, false);
                    }
                }
            }

            private void ResolvePoisonResponse(Unit enemy)
            {
                foreach (var actor in units.Where(u => u.Alive && u.Side != enemy.Side && CanActivateOffTurn(u)))
                foreach (var passive in Passives(actor))
                {
                    if (!passive.RespondsToPoison || actor.Cooldowns.GetValueOrDefault(passive.Row.id) > 0 ||
                        !Allies(actor).Any(u => u.Has(BattleStatusKind.Poison) || u.Has(BattleStatusKind.Venom)) ||
                        !actor.TriggeredPassives.Add(passive.Row.id)) continue;
                    var cleanse = passive.Effects.First(e => e.phase == "after_enemy_skill" && e.operation == "cleanse");
                    ApplyEffect(actor, actor, passive.Row, cleanse, owner.ModifiersFor(actor, passive), false,
                        BattleHitKind.None, null, false);
                    int cd = passive.Row.cooldownTurns - owner.ModifiersFor(actor, passive).CooldownTurns;
                    if (cd > 0) ChangeCooldown(actor, actor, passive.Row.id, cd);
                    Cast(actor, passive, actor, false, BattleActionOrigin.Passive, "after_enemy_skill");
                }
            }
        }
    }
}
