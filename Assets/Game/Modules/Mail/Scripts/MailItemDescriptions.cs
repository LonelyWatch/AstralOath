using System;
using System.Collections.Generic;

namespace AstralOath.Client.Mail
{
    // Display-only text, kept separate from authoritative rewards. Unknown items have no invented description.
    public static class MailItemDescriptions
    {
        private static readonly Dictionary<string, string> Zh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["item.energy.kind"] = "入场券",
            ["item.energy.description"] = "冒险时必不可少的行动力。",
            ["item.gmGift.kind"] = "特别礼物",
            ["item.gmGift.description"] = "内含邮件奖励的礼物箱。在邮箱内领取时可立即获得箱内所有道具。",
            ["item.bookmarks.kind"] = "召唤",
            // Chinese project translation of the authorized original Japanese ticketrare_de.
            // The exact Simplified Chinese client wording has not been supplied or verified.
            ["item.bookmarks.description"] = "继承者可打开特殊书页，以此召唤英雄的书签。",
            ["item.owned"] = "目前持有{0}个"
        };

        public static string GetKind(MailReward reward)
        {
            if (reward == null) return string.Empty;
            if (reward.kind == "energy") return Zh["item.energy.kind"];
            if (IsGmGift(reward)) return Zh["item.gmGift.kind"];
            if (IsBookmark(reward)) return Zh["item.bookmarks.kind"];
            // Unverified classifications are intentionally left blank.
            return string.Empty;
        }

        public static string GetDescription(MailReward reward)
        {
            if (reward == null) return string.Empty;
            if (reward.kind == "energy") return Zh["item.energy.description"];
            if (IsGmGift(reward)) return Zh["item.gmGift.description"];
            if (IsBookmark(reward)) return Zh["item.bookmarks.description"];
            return string.Empty;
        }

        public static bool IsGmGift(MailReward reward) => reward != null && reward.kind == "bundle" && reward.key == "gm_gift";
        private static bool IsBookmark(MailReward reward) => reward != null && reward.kind == "currency" &&
            (reward.key == "bookmarks" || reward.key == "epicBookmarks");
        public static float DesignHeight(MailReward reward) => IsGmGift(reward) ? 285f : 375f;
        public static string Owned(long balance) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Zh["item.owned"], balance.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
    }
}
