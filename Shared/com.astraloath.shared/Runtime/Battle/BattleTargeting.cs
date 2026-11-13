using System.Linq;

namespace AstralOath.Shared.Battle
{
    // Validates explicit targets. Recipients of area attacks and reactions are resolved by the skill executor.
    internal static class BattleTargeting
    {
        internal static string PrimaryRule(BattleCatalogData.SkillRow skill, BattleUnitSnapshot actor) =>
            skill.aoeWhenBuffed && actor.Statuses.Any(status => !BattleSkillResolver.IsHarmful(status.Kind))
                ? "all_enemies" : skill.target;

        internal static BattleUnitSnapshot ActiveProvoker(BattleSnapshot snapshot, BattleUnitSnapshot actor)
        {
            var sourceId = actor.GetStatus(BattleStatusKind.Provoke)?.SourceId;
            var source = sourceId == null ? null : snapshot.GetUnit(sourceId);
            return source?.IsAlive == true && source.Slot.Side != actor.Slot.Side ? source : null;
        }

        internal static bool CanSelect(BattleSnapshot snapshot, BattleUnitSnapshot actor, string targetRule, string targetId)
        {
            var selected = string.IsNullOrEmpty(targetId) ? null : snapshot.GetUnit(targetId);
            if (selected == null && !string.IsNullOrEmpty(targetId)) return false;
            return CanSelectUnit(snapshot, actor, targetRule, selected);
        }

        internal static bool CanSelectUnit(BattleSnapshot snapshot, BattleUnitSnapshot actor, string targetRule,
            BattleUnitSnapshot selected)
        {
            var provoker = ActiveProvoker(snapshot, actor);
            if (provoker != null && selected != provoker) return false;

            bool allies = targetRule == "one_ally" || targetRule == "all_allies";
            bool area = targetRule == "all_enemies" || targetRule == "all_allies";
            if (selected == null) return area;
            if (!selected.IsAlive || (selected.Slot.Side == actor.Slot.Side) != allies) return false;
            if (area || allies || provoker != null) return true;
            if (targetRule != "one_enemy" && targetRule != "two_enemies") return false;
            if (selected.GetStatus(BattleStatusKind.Stealth) == null) return true;

            return !snapshot.Units.Any(unit => unit.IsAlive && unit.Slot.Side == selected.Slot.Side &&
                unit.GetStatus(BattleStatusKind.Stealth) == null);
        }
    }
}
