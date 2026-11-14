using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDirectory : UIBaseView
    {
        public const string Address = "arena/directory";
        public override string PrefabPath => Address;
        public GameButton Back => Bindings.Require<GameButton>("back");
        public GameButton Create => Bindings.Require<GameButton>("create");
        public GameButton Refresh => Bindings.Require<GameButton>("refresh");
        public GameButton Filter => Bindings.Require<GameButton>("filter");
        public GameButton Search => Bindings.Require<GameButton>("search");
        public GameButton SubmitSearch => Bindings.Require<GameButton>("submitSearch");
        public GameText FilterLabel => Bindings.Require<GameText>("filterLabel");
        public Image SearchSelected => Bindings.Require<Image>("searchSelected");
        public Image SearchIcon => Bindings.Require<Image>("searchIcon");
        public GameText SearchLabel => Bindings.Require<GameText>("searchLabel");
        public GameText Status => Bindings.Require<GameText>("status");
        public GameObject SearchPanel => Bindings.Require<GameObject>("searchPanel");
        public TMP_InputField Query => Bindings.Require<TMP_InputField>("query");
        public ScrollRect List => Bindings.Require<ScrollRect>("list");
        public GameObject Row => Bindings.Require<GameObject>("row");
        public GameObject Choice => Bindings.Require<GameObject>("choice");
        public GameObject Password => Bindings.Require<GameObject>("password");
        public override void Validate()
        {
            _ = Back; _ = Create; _ = Refresh; _ = Filter; _ = Search; _ = SubmitSearch;
            _ = FilterLabel; _ = SearchSelected; _ = SearchIcon; _ = SearchLabel; _ = Status; _ = SearchPanel; _ = Query; _ = List; _ = Row; _ = Choice; _ = Password;
        }
    }
}
