using System;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleSkillPresentation
    {
        private bool TryGetCasterFormationCenter(Func<BattlePreviewActor, Vector3> position, out Vector3 center)
        {
            center = Vector3.zero;
            if (!caster) return false;
            int members = 0;
            foreach (var actor in participants)
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.IsDefeatPoseActive ||
                    actor.FacingDirection != caster.FacingDirection) continue;
                center += position(actor);
                members++;
            }
            if (members == 0) return false;
            center /= members;
            return true;
        }
    }
}
