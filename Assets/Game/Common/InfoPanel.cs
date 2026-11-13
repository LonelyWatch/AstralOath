using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    public sealed class InfoPanel : UIBaseView
    {
        public const string Address = "common/info-panel";
        public override string PrefabPath => Address;
        internal RectTransform Frame => (RectTransform)Root.transform;
        internal Image Background => Bindings.Require<Image>("background");
        internal Image EffectBackground => Bindings.Require<Image>("effectBackground");
        internal Image Icon => Bindings.Require<Image>("icon");
        internal Image SoulIcon => Bindings.Require<Image>("soulIcon");
        internal Image CooldownIcon => Bindings.Require<Image>("cooldownIcon");
        internal GameText Title => Bindings.Require<GameText>("title");
        internal GameText Metadata => Bindings.Require<GameText>("metadata");
        internal GameText Cooldown => Bindings.Require<GameText>("cooldown");
        internal GameText Description => Bindings.Require<GameText>("description");
        internal GameText BurnTitle => Bindings.Require<GameText>("burnTitle");
        internal GameText BurnCost => Bindings.Require<GameText>("burnCost");
        internal Image BurnDivider => Bindings.Require<Image>("burnDivider");
        internal Image BurnIcon => Bindings.Require<Image>("burnIcon");
        internal GameText BurnDescription => Bindings.Require<GameText>("burnDescription");
        internal Image UpgradeDivider => Bindings.Require<Image>("upgradeDivider");
        internal GameText Upgrades => Bindings.Require<GameText>("upgrades");
        internal ScrollRect Scroll => Bindings.Require<ScrollRect>("scroll");

        public override void Validate()
        {
            _ = (Background, EffectBackground, Icon, SoulIcon, CooldownIcon, Title, Metadata, Cooldown, Description,
                BurnTitle, BurnCost, BurnDivider, BurnIcon, BurnDescription, UpgradeDivider, Upgrades, Scroll);
        }
    }
}
