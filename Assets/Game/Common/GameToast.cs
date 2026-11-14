using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class GameToast : UIBaseView
    {
        public override string PrefabPath => "common/game-toast";
        public GameText Message => Bindings.Require<GameText>("message");

        public static GameToast ForInstance(GameObject root)
        {
            var view = new GameToast();
            view.Attach(root);
            return view;
        }

        public override void Validate() => _ = Message;
    }
}
