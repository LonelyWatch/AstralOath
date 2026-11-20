using System.Collections.Generic;
using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    public sealed partial class BattleSkillResolver
    {
        private sealed partial class Execution
        {
            private List<(Unit Actor, Unit Target, BattleStatusKind Kind, int Bonus)> detonations;

            private void QueueDetonation(Unit actor, Unit target, BattleStatusKind kind, int bonus)
            {
                detonations ??= new List<(Unit, Unit, BattleStatusKind, int)>();
                detonations.Add((actor, target, kind, bonus));
            }

            private void StealBuffs(Unit actor, Unit target, int count, int chance, BattleHitKind hit, bool ignoresResistance)
            {
                var buffs = target.Statuses.Where(item => IsDispellableBuff(item.Kind)).Take(count).ToArray();
                if (buffs.Length == 0 || !CheckEffect(actor, target, null, chance, hit, true, false, ignoresResistance)) return;
                foreach (var buff in buffs)
                {
                    target.Statuses.Remove(buff);
                    Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, buff));
                    ApplyStatus(actor, actor, buff.Kind, buff.RemainingTurns, buff.Strength);
                }
            }

            private void ShortenBuffs(Unit actor, Unit target, int turns, int chance, BattleHitKind hit, bool ignoresResistance)
            {
                var buffs = target.Statuses.Where(item => IsDispellableBuff(item.Kind)).ToArray();
                if (buffs.Length == 0 || !CheckEffect(actor, target, null, chance, hit, true, false, ignoresResistance)) return;
                foreach (var buff in buffs)
                {
                    if (buff.RemainingTurns > turns) UpdateStatusDuration(actor, target, buff, -turns);
                    else
                    {
                        target.Statuses.Remove(buff);
                        Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, buff));
                    }
                }
            }

            public void ResolveDetonations()
            {
                if (detonations == null) return;
                foreach (var entry in detonations)
                {
                    if (!entry.Target.Alive) continue;
                    foreach (var status in entry.Target.Statuses.Where(s => s.Kind == entry.Kind).ToArray())
                    {
                        if (!entry.Target.Statuses.Contains(status)) continue;
                        ExplodeStatus(entry.Actor, entry.Target, status, entry.Bonus);
                        if (!entry.Target.Alive) break;
                    }
                }
            }

            private void ExplodeStatus(Unit actor, Unit target, BattleStatusSnapshot status, int bonus)
            {
                int ticks = status.Kind == BattleStatusKind.Bomb ? 1 : status.RemainingTurns;
                int damage = PeriodicDamage(status.Strength, target.Defense);
                ProjectDamage(actor, target, ScaleHealth((decimal)damage * ticks, Basis + bonus), BattleHitKind.None,
                    periodic: status);
                if (!target.Alive)
                {
                    ResolveEnemyDefeats(actor, 1);
                    return;
                }
                if (!target.Statuses.Remove(status)) return;
                Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, status));
                if (status.Kind == BattleStatusKind.Bomb &&
                    CheckEffect(actor, target, BattleStatusKind.Stun, Basis, BattleHitKind.None, true, true, true))
                    ApplyStatus(actor, target, BattleStatusKind.Stun, 1, 0);
            }
        }
    }
}
