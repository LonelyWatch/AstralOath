using AstralOath.Client.Common;
using TMPro;
using UnityEngine;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaPasswordEntry : UIBaseView
    {
        public override string PrefabPath => "arena/password-entry";
        public TMP_InputField Input => Bindings.Require<TMP_InputField>("input");
        public GameButton Confirm => Bindings.Require<GameButton>("confirm");
        public GameButton Dismiss => Bindings.Require<GameButton>("dismiss");

        public static ArenaPasswordEntry FromInstance(GameObject root)
        {
            var view = new ArenaPasswordEntry();
            view.BindTo(root);
            return view;
        }

        public override void Validate()
        {
            _ = Input;
            _ = Confirm;
            _ = Dismiss;
        }
    }
}
