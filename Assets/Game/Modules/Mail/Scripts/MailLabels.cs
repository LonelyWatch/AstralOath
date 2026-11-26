using System;
using System.Collections.Generic;
namespace AstralOath.Client.Mail
{
    // Stable localization keys. Kept with the module, separate from player-supplied plain text.
    public static class MailLabels
    {
        private static readonly Dictionary<string,string> Zh = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["entryCurrency"]="入场券", ["currency"]="货币", ["enhancementStone"]="强化石", ["title"]="邮件", ["claim"]="领取", ["claimAll"]="全部领取", ["receive"]="收信", ["replay"]="再次查看", ["claimed"]="已领取", ["deleted"]="已删除", ["expired"]="已过期", ["remaining"]="剩余时间", ["readTime"]="读取时间", ["days"]="剩余{0}天", ["hours"]="剩余{0}小时", ["minutes"]="剩余{0}分钟", ["agoDays"]="{0}天前", ["agoHours"]="{0}小时前", ["agoMinutes"]="{0}分钟前", ["now"]="刚刚", ["capacity"]="超出上限将无法接收新邮件 {0}/{1}", ["historyHint"]="自保存至记录之日起\n内容最多可显示90天。（最多100条）", ["empty"]="没有可领取的邮件。", ["emptyHistory"]="没有邮件记录。", ["sort"]="接收时间", ["confirmTitle"]="确定要领取所有信件吗?", ["confirmBody"]="- 行动力等入场货币可超过持有上限领取。\n- 在背包已满的情况下，英雄或装备将不会包含在全部领取中。\n- GM信件将不会包含在全部领取中。", ["cancel"]="取消", ["confirm"]="确认", ["rewardTitle"]="获得奖励", ["bundleResult"]="已成功领取礼包内的所有道具。", ["bulkResult"]="已领取所有邮件。", ["clickClose"]="点击关闭", ["already"]="您已领取该邮件。", ["noEligible"]="无法使用全部领取的邮件。", ["loading"]="正在读取邮件……", ["retry"]="请求结果待确认，请重试。", ["error"]="邮件请求失败，请重试。", ["attachment"]="附件", ["bundle"]="礼包", ["energy"]="行动力", ["energyValue"]="{0}/{1}", ["sortHint"]="按接收时间从新到旧显示。", ["mail_expired"]="该邮件已过期。", ["mail_already_claimed"]="您已领取该邮件。", ["hero_inventory_full"]="英雄背包已满。", ["artifact_inventory_full"]="神器背包已满。", ["equipment_inventory_full"]="装备背包已满。", ["resource_overflow"]="资源数量已达到上限。"
        };
        public static string Text(string key) => Zh.TryGetValue(key, out var value) ? value : Zh["error"];
        public static string Format(string key, params object[] values) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Text(key), values);
    }
}
