using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Mail
{
    public sealed class MailWindow : UIBaseView
    {
        public const string Address = "mail/window";
        public override string PrefabPath => Address;
        public BindingMap Map => Bindings;
        public override void Validate()
        {
            foreach (string key in new[] { "back", "inbox", "history", "claimAll", "detailClaim", "detailClose", "tooltipClose", "confirmCancel", "confirmOk", "rewardClose" }) Bindings.Require<GameButton>(key);
            foreach (string key in new[] { "capacity", "empty", "message", "detailBody" }) Bindings.Require<GameText>(key);
            Bindings.Require<ScrollRect>("scroll"); Bindings.Require<GameObject>("row");
            foreach (string key in new[] { "detail", "tooltip", "confirmation", "reward" }) Bindings.Require<GameObject>(key);
        }
    }
}
