using AstralOath.Client.Common;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    public sealed class BattleHud : UIBaseView
    {
        public override string PrefabPath => "battle/battle-hud";

        public override void Validate()
        {
            foreach (string key in new[] { "skill1", "skill2", "skill3", "pause", "speed", "auto", "soulBurn" })
                Bindings.Require<GameButton>(key);
            foreach (string key in new[] { "wave", "activeName", "activeHp", "soulCount" })
                Bindings.Require<GameText>(key);
            Bindings.Require<Image>("orderHourglass");
            Bindings.Require<Image>("activePortrait");
            Bindings.Require<Image>("activeHpFill");
            Bindings.Require<Image>("soulFill");
        }
    }
}
