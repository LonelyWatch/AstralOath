using System.Collections.Generic;
namespace AstralOath.Client.ChannelChat
{
    public static class ChannelChatLabels
    {
        private static readonly Dictionary<string,string> Values=new Dictionary<string,string>
        {
            ["reward.hero"]="玩家<color=#3176A5>{0}</color>召唤了<color=#AD510A>{1}</color>英雄。",
            ["reward.artifact"]="玩家<color=#3176A5>{0}</color>召唤了<color=#3B7818>{1}</color>神器。",
            ["channel"]="频道#{0}",["public"]="频道",["guild"]="骑士团",["input"]="点击此处并输入",["send"]="输入",
            ["entered"]="已进入#{0}频道。",["favoriteTitle"]="设置收藏频道",["channelTitle"]="切换频道",
            ["favoriteDescription"]="设置收藏频道后，登录游戏时将优先接入已设置的频道。",["channelDescription"]="输入要进入的频道。",
            ["favoriteValue"]="当前收藏频道：{0}",["none"]="无",["range"]="输入频道1-9999",["close"]="× 点击关闭",
            ["notificationTitle"]="聊天通知选项",["all"]="全部通知",["notification0"]="获得5★英雄通知",["notification1"]="获得5★神器通知",["notification2"]="骑士团宣传通知",["notification3"]="一般聊天通知",
            ["keywordPlaceholder"]="输入关键字",["keywordHelp"]="聊天内容中若出现被设为通知关键字的字眼，无论是否设置通知，都会通知聊天内容。",
            ["self"]="[me]",["justNow"]="刚才",["minutesAgo"]="{0}分钟前",["hoursAgo"]="{0}小时前",["loading"]="正在读取频道…",["saved"]="已保存",["error"]="网络连接失败，请重试。",
            ["pending"]="正在确认上次操作；请重试，不要重复提交。",["invalidChannel"]="请输入1至9999的频道。",["empty"]="请输入聊天内容。",["tooLong"]="聊天内容最多200个字符。",["keywordTooLong"]="每个关键字最多20个字符。",
            ["rate_limited"]="发送过于频繁，请稍后重试。",["chat_rate_limited"]="发送过于频繁，请稍后重试。",["chat_preferences_version_conflict"]="设置已更新，请重新操作。",["chat_version_conflict"]="设置已更新，请重新操作。",["chat_preferences_conflict"]="设置已更新，请重新操作。",["unknownSticker"]="无法使用该表情。"
        };
        public static string Text(string key)=>Values.TryGetValue(key,out var value)?value:Values["error"];
    }
}
