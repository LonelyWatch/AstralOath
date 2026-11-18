using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    public sealed class SummonWindow : UIBaseView
    {
        public const string Address = "summon/window";
        public override string PrefabPath => Address;

        internal static readonly string[] ButtonKeys =
        {
            "back", "help", "covenant", "iseria", "single", "ten", "free", "epic", "info", "wish", "storage",
            "recovery", "confirmYes", "confirmNo", "infoClose", "wishHero", "wishArtifact", "wishSave",
            "wishClose", "wishUnowned", "storageClose", "resultClose", "resultAgain", "skip", "next"
        };

        internal static readonly string[] LabelKeys =
        {
            "title", "balance", "epicBalance", "singlePrice", "tenPrice", "bannerTitle", "featuredHero", "featuredArtifact", "pity", "status",
            "singleLabel", "tenLabel", "freeLabel", "confirmTitle", "confirmPrompt", "confirmBody", "confirmHeroName", "confirmHeroPity", "confirmCost", "confirmCurrency", "infoTitle", "wishTitle",
            "storageTitle", "resultTitle", "wishCount", "wishButtonLabel", "skipLabel", "nextLabel", "revealName", "revealStars", "revealLabel"
        };

        internal static readonly string[] PanelKeys =
            { "confirmPanel", "infoPanel", "wishPanel", "storagePanel", "resultPanel", "revealPanel" };

        private static readonly string[] ListKeys = { "infoItems", "wishItems", "wishSelectedItems", "storageItems", "resultItems" };
        private static readonly string[] ImageKeys = { "artifactArt", "revealArt", "singleCoin", "tenCoin", "confirmCoin", "confirmHeroPortrait", "featuredElement", "featuredRole" };

        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameText Text(string key) => Bindings.Require<GameText>(key);
        internal GameObject Panel(string key) => Bindings.Require<GameObject>(key);
        internal RectTransform Items(string key) => Bindings.Require<RectTransform>(key);
        internal Image Image(string key) => Bindings.Require<Image>(key);
        internal RawImage Raw(string key) => Bindings.Require<RawImage>(key);
        internal RawImage Effects => Raw("effectCanvas");
        internal AudioSource FeaturedSkillAudio => Bindings.Require<AudioSource>("featuredSkillAudio");
        internal Sprite Coin(bool epic) => Image(epic ? "epicCoin" : "normalCoin").sprite;
        internal RectTransform ConfirmPriceRow => Bindings.Require<RectTransform>("confirmPriceRow");
        internal AudioSource Audio => Bindings.Require<AudioSource>("audio");
        internal AudioSource Music => Bindings.Require<AudioSource>("music");
        internal GameObject WishTemplate => Bindings.Require<GameObject>("wishTemplate");
        internal Sprite WishFrame(bool artifact) => Image(artifact ? "artifactWishFrame" : "heroWishFrame").sprite;
        internal GameObject RowTemplate => Bindings.Require<GameObject>("rowTemplate");

        public override void Validate()
        {
            foreach (string key in ButtonKeys) Button(key);
            foreach (string key in LabelKeys) Text(key);
            foreach (string key in PanelKeys) Panel(key);
            foreach (string key in ListKeys) Items(key);
            foreach (string key in ImageKeys) Image(key);
            foreach (string key in new[] { "covenantBackdrop", "upBackdrop", "wishRoster", "heroCaption", "artifactCaption", "artifactFrame", "featuredBadges", "confirmHeroRow", "covenantSelected", "iseriaSelected" }) _ = Panel(key);
            _ = Raw("featuredSkillCanvas");
            _ = FeaturedSkillAudio;
            _ = ConfirmPriceRow;
            _ = Coin(false);
            _ = Coin(true);
            _ = Raw("upPortrait");
            _ = Raw("revealHero");
            for (int index = 0; index < 7; index++)
            {
                string key = "wishHero" + index;
                _ = Panel(key + "Card");
                _ = Raw(key);
                _ = Text(key + "Owned");
                _ = Text(key + "Name");
                _ = Image(key + "Role");
                _ = Image(key + "Element");
            }
            _ = Effects;
            _ = Audio;
            _ = Music;
            _ = WishFrame(false);
            _ = WishFrame(true);
            var wish = WishTemplate.GetComponent<BindingMap>();
            _ = wish.Require<GameButton>("button");
            _ = wish.Require<Image>("icon");
            _ = wish.Require<Image>("selected");
            _ = wish.Require<GameText>("name");
            var template = RowTemplate.GetComponent<BindingMap>();
            _ = template.Require<GameButton>("button");
            _ = template.Require<GameText>("name");
            _ = template.Require<GameText>("detail");
            _ = template.Require<Image>("icon");
        }
    }
}
