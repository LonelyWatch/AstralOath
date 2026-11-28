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
            "settings", "previewToggle", "settingsClose", "back", "help", "covenant", "iseria", "single", "ten", "free", "epic", "info", "wish", "storage",
            "recovery", "confirmYes", "confirmNo", "infoClose", "wishHero", "wishArtifact", "wishSave",
            "wishDismiss", "wishInfo", "wishFilter", "wishRecommend", "wishClose", "wishUnowned", "storageClose", "resultClose", "resultAgain", "skip", "next"
        };

        internal static readonly string[] LabelKeys =
        {
            "title", "balance", "transmitBalance", "moonBalance", "mysticBalance", "skyBalance", "singlePrice", "tenPrice", "bannerTitle", "featuredHero", "featuredHeroShadow", "featuredArtifact", "pity", "eventCountdown", "status",
            "singleLabel", "tenLabel", "freeLabel", "confirmTitle", "confirmPrompt", "confirmBody", "confirmHeroName", "confirmHeroPity", "confirmCost", "confirmCurrency", "infoTitle", "wishTitle",
            "storageTitle", "resultTitle", "wishCount", "wishButtonLabel", "skipLabel", "nextLabel", "revealName", "revealStars", "revealLabel"
        };

        internal static readonly string[] PanelKeys =
            { "settingsPanel", "confirmPanel", "infoPanel", "wishPanel", "storagePanel", "resultPanel", "revealPanel" };

        private static readonly string[] ListKeys = { "infoItems", "wishItems", "wishSelectedItems", "wishSelectedHeroes", "wishSelectedArtifacts", "storageItems", "resultItems", "wishPortraitStrip", "wishCaptionStrip" };
        private static readonly string[] ImageKeys = { "artifactArt", "artifactEmblem", "artifactRole", "revealArt", "singleCoin", "tenCoin", "confirmCoin", "confirmHeroPortrait", "featuredElement", "featuredRole" };

        internal GameButton Button(string key) => Bindings.Require<GameButton>(key);
        internal GameText Text(string key) => Bindings.Require<GameText>(key);
        internal GameObject Panel(string key) => Bindings.Require<GameObject>(key);
        internal RectTransform Items(string key) => Bindings.Require<RectTransform>(key);
        internal Image Image(string key) => Bindings.Require<Image>(key);
        internal CanvasGroup Group(string key) => Bindings.Require<CanvasGroup>(key);
        internal RawImage Raw(string key) => Bindings.Require<RawImage>(key);
        internal RawImage Effects => Raw("effectCanvas");
        internal AudioSource FeaturedSkillAudio => Bindings.Require<AudioSource>("featuredSkillAudio");
        internal Sprite Coin(bool epic) => Image(epic ? "epicCoin" : "normalCoin").sprite;
        internal void LayoutConfirmation(bool featured)
        {
            const float scale = 1080f / 720f;
            var panel = (RectTransform)Panel("confirmHeroRow").transform.parent;
            panel.sizeDelta = new Vector2(0f, (featured ? 571f : 498f) * scale);
            panel.anchoredPosition = new Vector2(0f, (featured ? 0f : 16f) * scale);
            foreach (string key in new[] { "confirmNo", "confirmYes" })
            {
                var rect = (RectTransform)Button(key).transform;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, (featured ? 57f : 48f) * scale);
            }
            var price = Items("confirmPriceRow");
            price.anchoredPosition = (featured ? new Vector2(0, -356) : new Vector2(124, -266)) * scale;
            Panel("confirmPriceDivider").SetActive(featured);
            var caption = Items("confirmCurrencyCaption");
            caption.anchoredPosition = new Vector2(featured ? 0f : -26f, -3f) * scale;
            var owned = Text("confirmBody");
            var ownedBox = (RectTransform)owned.transform.parent;
            ownedBox.anchoredPosition = (featured ? new Vector2(125, -76) : new Vector2(-124, -132)) * scale;
            owned.alignment = featured ? TMPro.TextAlignmentOptions.MidlineLeft : TMPro.TextAlignmentOptions.Center;
        }
        internal AudioSource Audio => Bindings.Require<AudioSource>("audio");
        internal AudioSource Music => Bindings.Require<AudioSource>("music");
        internal GameObject WishTemplate => Bindings.Require<GameObject>("wishTemplate");
        internal GameObject RowTemplate => Bindings.Require<GameObject>("rowTemplate");

        internal void BindCardLayout(AstralOath.Client.Core.BindingScope scope)
        {
            var wishDialog = (RectTransform)Text("wishTitle").transform.parent;
            var results = Items("resultItems");
            var groups = new[] { Items("wishPortraitStrip"), Items("wishCaptionStrip"), results, wishDialog };
            var designSize = results.sizeDelta;

            var infoSheet = Items("infoSheet");
            var viewport = (RectTransform)Root.transform;
            Vector2 lastSize = Vector2.zero;
            void Resize()
            {
                Vector2 size = viewport.rect.size;
                if (size == lastSize || size.x <= 0f || size.y <= 0f) return;
                lastSize = size;
                float sheetScale = Mathf.Min(size.x / (infoSheet.sizeDelta.x + 24f), size.y / 1080f);
                infoSheet.localScale = new Vector3(sheetScale, sheetScale, 1f);
                float scale = Mathf.Min(size.x / designSize.x, size.y / designSize.y);
                foreach (var group in groups)
                    group.localScale = new Vector3(scale, scale, 1f);
            }
            Resize();
            Canvas.preWillRenderCanvases += Resize;
            scope.Add(() => Canvas.preWillRenderCanvases -= Resize);
        }

        public override void Validate()
        {
            // Dismiss backdrops and blank-space blockers are pointer surfaces, not action buttons.
            // Transition.None alone still permits GameButton's child-graphic press tint.
            foreach(string key in new[]{"infoClose","infoSurfaceInput","wishDismiss","wishSurfaceInput","revealDismiss"})
                Button(key).KeepBrightnessOnPress=true;
            _ = Panel("noticeToast");
            foreach (string key in ButtonKeys) Button(key);
            foreach (string key in LabelKeys) Text(key);
            _ = Panel("mainPage");
            _ = Items("infoSheet");
            _ = Items("infoHeroColumn");
            _ = Items("infoArtifactColumn");
            _ = Panel("infoRowTemplate");
            _ = Button("infoNotesTab");
            _ = Button("infoRatesTab");
            foreach (string key in PanelKeys) Panel(key);
            foreach (string key in ListKeys) Items(key);
            foreach (string key in ImageKeys) Image(key);
            foreach (string key in new[] { "covenantBackdrop", "upBackdrop", "wishRoster", "heroCaption", "artifactCaption", "artifactFrame", "featuredBadges", "confirmHeroRow", "covenantSelected", "iseriaSelected" }) _ = Panel(key);
            _ = Group("featuredActorLayer");
            _ = Group("featuredOfferLayer");
            _ = Group("featuredScenery");
            _ = Raw("featuredSkillCanvas");
            _ = FeaturedSkillAudio;
            _ = Items("confirmPriceRow");
            _ = Items("confirmCurrencyCaption");
            _ = Panel("confirmPriceDivider");
            _ = Text("confirmType");
            _ = Coin(false);
            _ = Coin(true);
            _ = Raw("upPortrait");
            for (int star = 0; star < 6; star++) _ = Panel("artifactStar" + star);
            _ = Raw("revealHero");
            foreach (string key in new[] { "revealVoice", "revealDetails", "revealDismiss" }) _ = Button(key);
            foreach (string key in new[] { "revealElement", "revealRole", "revealZodiac" })
            {
                _ = Image(key);
                _ = Text(key + "Text");
            }
            foreach (string key in new[] { "revealCV", "revealSpeaker", "revealQuote" }) _ = Text(key);
            foreach (string key in new[] { "revealNew", "revealCaption", "revealSpeech" }) _ = Panel(key);
            _ = Items("revealGrade");
            _ = Group("revealCaptionFade");
            for (int star = 0; star < 6; star++) _ = Panel("revealStar" + star);
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
            var wish = WishTemplate.GetComponent<BindingMap>();
            _ = wish.Require<GameButton>("button");
            _ = wish.Require<Image>("icon");
            _ = wish.Require<Image>("selected");
            _ = wish.Require<Image>("role");
            _ = wish.Require<Image>("element");
            _ = wish.Require<GameObject>("remove");
            _ = wish.Require<GameText>("name");
            var template = RowTemplate.GetComponent<BindingMap>();
            _ = template.Require<GameButton>("button");
            _ = template.Require<GameText>("name");
            _ = template.Require<GameText>("detail");
            _ = template.Require<Image>("icon");
        }
    }
}
