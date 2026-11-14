using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class ChoicePopup : UIBaseView
    {
        public override string PrefabPath => "common/choice-popup";
        public RectTransform Body { get; private set; }
        internal GameButton Dismiss { get; private set; }
        internal GameButton ChoiceTemplate { get; private set; }

        public static ChoicePopup FromInstance(GameObject root)
        {
            var view = new ChoicePopup();
            view.Attach(root);
            return view;
        }

        public override void Validate()
        {
            Body = Bindings.Require<RectTransform>("body");
            Dismiss = Bindings.Require<GameButton>("dismiss");
            ChoiceTemplate = Bindings.Require<GameButton>("choiceTemplate");
        }
    }
}