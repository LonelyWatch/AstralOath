using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class ArtifactInventoryCard : UIBaseView
    {
        public override string PrefabPath => "home/artifact-inventory-card";
        internal GameButton Select { get; private set; }
        internal Image Emblem, Banner, Role, Lock, Selection, Wearer, MaterialExp;
        internal GameObject WearerFrame;
        internal GameText Name, SkillLevel, MaterialCount;
        internal BindingMap Enhancement;
        internal readonly Image[] Stars = new Image[5];
        internal readonly Image[] Breaks = new Image[5];
        internal readonly Image[] BreakSlots = new Image[5];

        public ArtifactInventoryCard() { }
        internal ArtifactInventoryCard(GameObject instance) => BindTo(instance);

        public override void Validate()
        {
            Select = Bindings.Require<GameButton>("artifactCardButton");
            Emblem = Bindings.Require<Image>("artifactCardPortrait");
            Banner = Bindings.Require<Image>("artifactCardBanner");
            Role = Bindings.Require<Image>("artifactCardRole");
            Lock = Bindings.Require<Image>("artifactCardLock");
            Selection = Bindings.Require<Image>("artifactCardSelection");
            Name = Bindings.Require<GameText>("artifactCardName");
            SkillLevel = Bindings.Require<GameText>("artifactCardLimit");
            MaterialCount = Bindings.Require<GameText>("materialCount");
            MaterialExp = Bindings.Require<Image>("materialExp");
            Enhancement = Bindings.Require<BindingMap>("artifactCardBadge");
            var avatar = Bindings.Require<BindingMap>("artifactCardWearer");
            WearerFrame = avatar.gameObject;
            Wearer = avatar.Require<Image>("portrait");
            for (int i = 0; i < Stars.Length; i++)
            {
                Stars[i] = Bindings.Require<Image>("star" + i);
                Breaks[i] = Bindings.Require<Image>("break" + i);
                BreakSlots[i] = Bindings.Require<Image>("breakSlot" + i);
            }
        }
    }
}
