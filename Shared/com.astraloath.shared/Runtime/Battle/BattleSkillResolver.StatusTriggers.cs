using System.Linq;
using static AstralOath.Shared.Battle.EpicSevenCombatMath;

namespace AstralOath.Shared.Battle
{
    public sealed partial class BattleSkillResolver
    {
        internal static bool IsImmobilizing(BattleStatusKind kind) =>
            kind == BattleStatusKind.Stun || kind == BattleStatusKind.Sleep;

        private sealed partial class Execution
        {
            private void ApplyRandomStatuses(Unit actor, Unit target, Program program, int count, bool soulBurn,
                BattleHitKind hit, bool ignoresResistance)
            {
                var options = program.Effects.Where(row => row.operation == "random_status_option").ToList();
                for (int i = 0; i < count && options.Count > 0; i++)
                {
                    int pick = (int)(random.NextUInt64() % (ulong)options.Count);
                    var option = options[pick];
                    options.RemoveAt(pick);
                    var kind = ParseStatus(option.status);
                    bool hostile = IsHarmful(kind);
                    if (!CheckEffect(actor, target, kind, Basis, hit, hostile, hostile, ignoresResistance)) continue;
                    int duration = option.turns + (soulBurn && program.Row.soulBurnKind == "extend_granted_buffs" ? program.Row.soulBurnTurns : 0);
                    ApplyStatus(actor, target, kind, duration, option.value);
                }
            }

            private void WakeOnAttackDamage(Unit actor, Unit target)
            {
                var sleep = target.Status(BattleStatusKind.Sleep);
                if (sleep == null) return;
                target.Statuses.Remove(sleep);
                if (target.Alive) Changes.Add(BattleChange.RemoveStatus(actor.Id, target.Id, sleep));
            }

            private void TriggerSkillOnOwnTurn(Unit actor, Unit selected,
                BattleCatalogData.EffectRow effect, bool multiTargetAttack, bool soulBurn)
            {
                if (actor.Id != activeActorId || !CanReact(actor) ||
                    !ConditionMet(effect, actor, actor, multiTargetAttack, soulBurn: soulBurn) ||
                    !actor.TriggeredTurnEffects.Add(effect.id)) return;
                Changes.Add(BattleChange.MarkTurnEffect(actor.Id, effect.id));
                TriggerSkill(actor, effect, selected);
            }
        }
    }
}
