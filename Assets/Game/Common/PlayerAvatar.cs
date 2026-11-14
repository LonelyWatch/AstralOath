using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    public sealed class PlayerAvatar : UIBaseView
    {
        private Image portraitImage;
        private Image frameImage;
        private Image ownerImage;
        private Image badgeImage;
        private Sprite defaultFrame;

        public override string PrefabPath => "common/player-avatar";

        public static PlayerAvatar FromInstance(GameObject root)
        {
            var view = new PlayerAvatar();
            view.Attach(root);
            return view;
        }

        public override void Validate()
        {
            portraitImage = Bindings.Require<Image>("portrait");
            frameImage = Bindings.Require<Image>("frame");
            ownerImage = Bindings.Require<Image>("owner");
            badgeImage = Bindings.Require<Image>("badge");
            defaultFrame = frameImage.sprite;
        }

        public void Set(Sprite portrait, Sprite frame = null, bool owner = false, Sprite badge = null)
        {
            portraitImage.sprite = portrait;
            portraitImage.enabled = portrait;
            frameImage.sprite = frame ? frame : defaultFrame;
            ownerImage.gameObject.SetActive(owner);
            badgeImage.sprite = badge;
            badgeImage.gameObject.SetActive(badge);
        }
    }
}
