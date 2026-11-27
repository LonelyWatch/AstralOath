using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindow : UIBaseView
    {
        public const string Address = "home/main";
        public override string PrefabPath => Address;
        internal RawImage LobbyPortrait => Bindings.Require<RawImage>("lobbyPortrait");
        public GameButton More => Bindings.Require<GameButton>("more");
        public GameButton Bag => Bindings.Require<GameButton>("bag");
        public GameButton Arena => Bindings.Require<GameButton>("arena");
        public GameButton Summon => Bindings.Require<GameButton>("summon");
        public GameButton ProfileOpen => Bindings.Require<GameButton>("profileOpen");
        public GameButton Feature(string id)=>Bindings.Require<GameButton>("feature."+id);
        public Image ProfileAvatar=>Bindings.Require<Image>("profileAvatar");
        public Image ProfileFrame=>Bindings.Require<Image>("profileFrame");
        public GameText ProfileRank=>Bindings.Require<GameText>("profileRank");
        public GameText ProfileExperiencePercent=>Bindings.Require<GameText>("profileExperiencePercent");
        public Image ProfileExperienceFill=>Bindings.Require<Image>("profileExperienceFill");
        internal RectTransform ArenaNavigation => Bindings.Require<RectTransform>("arenaNavigation");
        public GameText ArenaLabel => Bindings.Require<GameText>("arenaLabel");
        public GameText Profile => Bindings.Require<GameText>("profile");
        public GameButton DisplayMode => Bindings.Require<GameButton>("displayMode");
        public GameText DisplayModeLabel => Bindings.Require<GameText>("displayModeLabel");
        public override void Validate() { _ = Arena; _ = ArenaLabel; _ = ArenaNavigation; _ = Profile; _ = DisplayMode; _ = DisplayModeLabel; _ = More; _ = Bag; _ = LobbyPortrait;_ = Summon;_ = ProfileOpen;_ = ProfileAvatar;_ = ProfileFrame;_ = ProfileRank;_ = ProfileExperiencePercent;_ = ProfileExperienceFill;foreach(var id in HomeFeatureLabels.Main)_=Feature(id); }
    }
}
