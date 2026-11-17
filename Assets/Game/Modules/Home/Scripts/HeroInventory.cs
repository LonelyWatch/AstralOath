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
        internal GameObject Bag => Bindings.Require<GameObject>("bag");
        internal GameObject Roster => Bindings.Require<GameObject>("roster");
        internal GameButton Back => Bindings.Require<GameButton>("back");
        internal GameText Title => Bindings.Require<GameText>("title");
        internal GameText Capacity => Bindings.Require<GameText>("capacity");
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
        internal TMP_InputField Search => Bindings.Require<TMP_InputField>("search");
        internal GameText SearchHint => Bindings.Require<GameText>("searchHint");
        internal GameButton Expand => Bindings.Require<GameButton>("expand");
        internal GameText ExpandLabel => Bindings.Require<GameText>("expandLabel");
        internal GameText Status => Bindings.Require<GameText>("status");
        internal GameButton Retry => Bindings.Require<GameButton>("retry");
        internal GameText RetryLabel => Bindings.Require<GameText>("retryLabel");
        internal GameText DetailName => Bindings.Require<GameText>("detailName");
        internal GameText DetailLevel => Bindings.Require<GameText>("detailLevel");
        internal GameText DetailStars => Bindings.Require<GameText>("detailStars");
        internal Image DetailIllustration => Bindings.Require<Image>("detailIllustration");
        internal RawImage DetailPortrait => Bindings.Require<RawImage>("detailPortrait");
        internal GameObject BagFilter => Bindings.Require<GameObject>("bagFilter");
        internal static readonly string[] ClassNames = { "all", "knight", "warrior", "mage", "ranger", "thief", "soul_weaver" };
        internal GameButton ClassFilter(string key) => Bindings.Require<GameButton>("filter_" + key);
        internal Image ClassSelection(string key) => Bindings.Require<Image>("filterActive_" + key);
        internal Image TabSelection(string key) => Bindings.Require<Image>("tabActive_" + key);
        internal Image DetailElement => Bindings.Require<Image>("detailElement");
        internal Image DetailRole => Bindings.Require<Image>("detailRole");

        public override void Validate()
        {
            _ = Bag; _ = Roster; _ = Back; _ = Title; _ = Capacity;
            _ = TabHeroes; _ = TabEquipment; _ = TabArtifacts; _ = TabHeroesLabel;
            _ = TabEquipmentLabel; _ = TabArtifactsLabel; _ = BagScroll; _ = RosterScroll;
            _ = BagRow; _ = RosterRow; _ = RosterDetail; _ = RosterSidebar;
            _ = Search; _ = SearchHint; _ = Expand; _ = ExpandLabel; _ = Status; _ = Retry; _ = RetryLabel;
            _ = DetailName; _ = DetailLevel; _ = DetailStars; _ = DetailIllustration; _ = DetailElement; _ = DetailRole;
            _ = DetailPortrait; _ = BagFilter;
            foreach (string name in ClassNames)
            {
                _ = ClassFilter(name);
                _ = ClassSelection(name);
            }
            _ = TabSelection("heroes"); _ = TabSelection("equipment"); _ = TabSelection("artifacts");
        }
    }
}
