using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.CharacterLab
{
    public sealed class CharacterLabWindow : UIBaseView
    {
        public const string PrefabAddress = "characterlab/character-lab-window";
        public override string PrefabPath => PrefabAddress;
        public GameText Text(string key) => Bindings.Require<GameText>(key);
        public GameButton Button(string key) => Bindings.Require<GameButton>(key);
        public Transform List(string key) => Bindings.Require<RectTransform>(key);
        public RawImage Preview => Bindings.Require<RawImage>("preview");
        public TMP_InputField Search => Bindings.Require<TMP_InputField>("search");
        public GameButton RowTemplate => Bindings.Require<GameButton>("rowTemplate");
        public override void Validate()
        {
            _ = Search; _ = RowTemplate; _ = Preview;
            foreach (string key in new[] {"title","subtitle","status","transport","diagnostics","targets"}) _ = Text(key);
            foreach (string key in new[] {"characters","animations","voices","skills"}) _ = List(key);
            foreach (string key in new[] {"battle","portrait","play","pause","stop","step","loop","slower","faster","zoomIn","zoomOut","flip","background","reset","skin","experimental","seekBack","seekForward"}) _ = Button(key);
        }
    }
}
