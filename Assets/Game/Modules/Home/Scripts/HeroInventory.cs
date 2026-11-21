using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class HeroInventory : UIBaseView
    {
        public const string Address = "home/hero-inventory";
        public override string PrefabPath => Address;
        private Vector2 previousWindow;
        private bool rosterSized;
        private RectTransform inventoryHeader, rosterComposition, rosterHeader;

        internal void FitInventoryLayout(bool roster)
        {
            if (!roster)
            {
                if (!rosterSized) return;
                inventoryHeader.anchorMin = Vector2.zero;
                inventoryHeader.anchorMax = Vector2.one;
                inventoryHeader.pivot = new Vector2(.5f, .5f);
                inventoryHeader.localScale = Vector3.one;
                inventoryHeader.offsetMin = inventoryHeader.offsetMax = Vector2.zero;
                rosterSized = false;
                return;
            }
            Vector2 window = ((RectTransform)Root.transform).rect.size;
            if (window.x <= 0f || window.y <= 0f || (rosterSized && previousWindow == window)) return;
            previousWindow = window;
            rosterSized = true;
            // Keep the original height-based density on landscape screens. On narrow
            // screens width limits the whole composition, including fonts and card pitch.
            float scale = Mathf.Min(window.y / 1080f, window.x / 1728f);
            Vector2 size = new Vector2(Mathf.Min(window.x / scale, 2430f), 1080f);
            // Spread the detail sections and their vignette over the available height.
            // Their fixed-size controls retain the same uniform scale as the top bar.
            FitFrame(rosterComposition, new Vector2(size.x, window.y / scale), scale, .5f);
            FitFrame(rosterHeader, size, scale, 1f);
            FitFrame(inventoryHeader, size, scale, 1f);
        }

        private static void FitFrame(RectTransform frame, Vector2 size, float scale, float verticalAnchor)
        {
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(.5f, verticalAnchor);
            frame.sizeDelta = size;
            frame.anchoredPosition = Vector2.zero;
            frame.localScale = new Vector3(scale, scale, 1f);
        }
        internal ArtifactInventoryBindings Artifacts => new ArtifactInventoryBindings(Bindings);
        internal GameObject Bag => Bindings.Require<GameObject>("bag");
        internal GameObject Roster => Bindings.Require<GameObject>("roster");
        internal GameButton Back => Bindings.Require<GameButton>("back");
        internal GameText Title => Bindings.Require<GameText>("title");
        internal GameText Capacity => Bindings.Require<GameText>("capacity");
        internal GameText RosterCapacity => Bindings.Require<GameText>("rosterCapacity");
        internal GameObject RosterExpandIcon => Bindings.Require<GameObject>("rosterExpandIcon");
        internal GameObject RosterCollapseIcon => Bindings.Require<GameObject>("rosterCollapseIcon");
        internal GameObject RosterExpandedHint => Bindings.Require<GameObject>("rosterExpandedHint");
        internal RectTransform RosterCollapsedFooter => Bindings.Require<RectTransform>("rosterCollapsedFooter");
        internal GameText RosterSortCaption => Bindings.Require<GameText>("rosterSortCaption");
        internal GameObject EmptyEquipment => Bindings.Require<GameObject>("emptyEquipmentChrome");
        internal GameObject EmptyArtifacts => Bindings.Require<GameObject>("emptyArtifactChrome");
        internal GameText BagCaption(string key) => Bindings.Require<GameText>("bag" + key + "Label");
        internal GameText DetailElementName => Bindings.Require<GameText>("detailElementName");
        internal GameText DetailRoleName => Bindings.Require<GameText>("detailRoleName");
        internal Image DetailZodiac => Bindings.Require<Image>("detailZodiac");
        internal GameText DetailZodiacName => Bindings.Require<GameText>("detailZodiacName");
        internal Image DetailStar(int index) => Bindings.Require<Image>("detailStar" + index);
        internal GameText DetailStat(string key) => Bindings.Require<GameText>("detailStat_" + key);
        internal GameButton TabHeroes => Bindings.Require<GameButton>("tabHeroes");
        internal GameButton TabEquipment => Bindings.Require<GameButton>("tabEquipment");
        internal GameButton TabArtifacts => Bindings.Require<GameButton>("tabArtifacts");
        internal GameText TabHeroesLabel => Bindings.Require<GameText>("tabHeroesLabel");
        internal GameText TabEquipmentLabel => Bindings.Require<GameText>("tabEquipmentLabel");
        internal GameText TabArtifactsLabel => Bindings.Require<GameText>("tabArtifactsLabel");
        internal ScrollRect BagScroll => Bindings.Require<ScrollRect>("bagScroll");
        internal ScrollRect RosterScroll => Bindings.Require<ScrollRect>("rosterScroll");
        internal GameObject BagRow => Bindings.Require<GameObject>("bagRow");
        internal GameObject RosterRow => Bindings.Require<GameObject>("rosterRow");
        internal RectTransform RosterDetail => Bindings.Require<RectTransform>("rosterDetail");
        internal RectTransform RosterSidebar => Bindings.Require<RectTransform>("rosterSidebar");
        internal RectTransform RosterShading => Bindings.Require<RectTransform>("rosterShading");
        internal TMP_InputField Search => Bindings.Require<TMP_InputField>("search");
        internal GameText SearchHint => Bindings.Require<GameText>("searchHint");
        internal GameButton Expand => Bindings.Require<GameButton>("expand");
        internal GameText ExpandLabel => Bindings.Require<GameText>("expandLabel");
        internal GameText Status => Bindings.Require<GameText>("status");
        internal GameButton Retry => Bindings.Require<GameButton>("retry");
        internal GameText RetryLabel => Bindings.Require<GameText>("retryLabel");
        internal GameButton ArtifactTrial => Bindings.Require<GameButton>("artifactTrial");
        internal GameText ArtifactTrialLabel => Bindings.Require<GameText>("artifactTrialLabel");
        internal GameText ArtifactTrialStatus => Bindings.Require<GameText>("artifactTrialStatus");
        internal GameText DetailName => Bindings.Require<GameText>("detailName");
        internal GameText DetailLevel => Bindings.Require<GameText>("detailLevel");
        internal GameText DetailStars => Bindings.Require<GameText>("detailStars");
        internal Image DetailIllustration => Bindings.Require<Image>("detailIllustration");
        internal RawImage DetailPortrait => Bindings.Require<RawImage>("detailPortrait");
        internal GameObject BagFilter => Bindings.Require<GameObject>("bagFilter");
        internal BindingMap WornFilters => Bindings.Require<BindingMap>("wornFilters");
        internal GameButton WornSort => Bindings.Require<GameButton>("bagSort");
        internal Image WornSortAttack => Bindings.Require<Image>("bagSortAttack");
        internal static readonly string[] ClassNames = { "all", "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" };
        internal GameButton ClassFilter(string key) => Bindings.Require<GameButton>("filter_" + key);
        internal Image ClassSelection(string key) => Bindings.Require<Image>("filterActive_" + key);
        internal Image TabSelection(string key) => Bindings.Require<Image>("tabActive_" + key);
        internal Image DetailElement => Bindings.Require<Image>("detailElement");
        internal Image DetailRole => Bindings.Require<Image>("detailRole");

        public override void Validate()
        {
            inventoryHeader = Bindings.Require<RectTransform>("inventoryHeader");
            _ = RosterShading;
            rosterComposition = Bindings.Require<RectTransform>("rosterComposition");
            rosterHeader = Bindings.Require<RectTransform>("rosterHeaderComposition");
            _ = Bag; _ = Roster; _ = Back; _ = Title; _ = Capacity;
            _ = TabHeroes; _ = TabEquipment; _ = TabArtifacts; _ = TabHeroesLabel;
            _ = TabEquipmentLabel; _ = TabArtifactsLabel; _ = BagScroll; _ = RosterScroll;
            _ = BagRow; _ = RosterRow; _ = RosterDetail; _ = RosterSidebar;
            _ = Search; _ = SearchHint; _ = Expand; _ = ExpandLabel; _ = Status; _ = Retry; _ = RetryLabel;
            _ = DetailName; _ = DetailLevel; _ = DetailStars; _ = DetailIllustration; _ = DetailElement; _ = DetailRole;
            _ = DetailPortrait; _ = BagFilter;
            _ = RosterCapacity; _ = RosterExpandIcon; _ = RosterCollapseIcon; _ = RosterExpandedHint;
            _ = RosterCollapsedFooter; _ = RosterSortCaption; _ = EmptyEquipment; _ = EmptyArtifacts;
            _ = DetailElementName; _ = DetailRoleName; _ = DetailZodiac; _ = DetailZodiacName;
            for (int index = 0; index < 6; ++index)
                _ = DetailStar(index);
            foreach (string name in ClassNames)
            {
                _ = ClassFilter(name);
                _ = ClassSelection(name);
            }
            _ = TabSelection("heroes"); _ = TabSelection("equipment"); _ = TabSelection("artifacts");
            _ = ArtifactTrial; _ = ArtifactTrialLabel; _ = ArtifactTrialStatus;
            Artifacts.Validate();
        }
    }
}
