using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    public sealed class BattleResult : UIBaseView
    {
        public override string PrefabPath => "battle/result";
        internal BindingMap Map => Bindings;
        internal void Bind(GameObject instance) => BindTo(instance);

        public override void Validate()
        {
            foreach (string key in new[] { "details", "confirm", "close" })
                Bindings.Require<GameButton>(key);
            foreach (string key in new[] { "detailsLabel", "confirmLabel", "closeLabel", "draw", "turn", "title", "allies", "enemies" })
                Bindings.Require<GameText>(key);
            Bindings.Require<RectTransform>("emblem");
            Bindings.Require<GameObject>("statistics");
            for (int i = 0; i < 4; i++)
            {
                Bindings.Require<GameObject>("hero" + i);
                Bindings.Require<Image>("face" + i);
                Bindings.Require<GameText>("friendship" + i);
            }
            for (int i = 0; i < 8; i++) Bindings.Require<GameText>("unit" + i);
        }
    }
}
