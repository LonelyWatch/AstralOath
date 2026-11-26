using AstralOath.Client.Common;
using UnityEngine;
namespace AstralOath.Client.Mail
{
    public sealed class MailEntry : UIBaseView
    {
        public const string Address = "mail/header-entry";
        public override string PrefabPath => Address;
        public GameButton Button => Bindings.Require<GameButton>("button");
        public GameText Count => Bindings.Require<GameText>("count");
        public GameObject Counter => Bindings.Require<GameObject>("counter");
        public override void Validate() { _ = Button; _ = Count; _ = Counter; }
    }
}
