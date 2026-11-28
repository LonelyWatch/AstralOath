using System;
using System.Collections.Generic;
namespace AstralOath.Client.Home
{
    public static class HomeFeatureLabels
    {
        public const string Prefix="home.feature.";
        public static readonly string[] Main={"episode","side_story","battle","arena","summon","shop"};
        public static readonly string[] Drawer={"guide","collection","heroes","growth","pets","recruit","friends","guild","forge","achievements","waiting_room","imprint"};
        public static readonly string[] Bottom={"notice","lobby_custom","pub","settings","music","music_prev","music_play","music_next","notifications"};
        public static readonly string[] Rail={"sanctuary","milestones","secret_shop","epic_pass","events","bunny_hideout"};
        private static readonly Dictionary<string,string> Zh=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["episode"]="Episode",["side_story"]="支线故事",["battle"]="战斗",["arena"]="竞技场",["summon"]="召唤",["shop"]="商店",["bag"]="背包",["profile"]="玩家资料",
            ["guide"]="欧勒毕斯指南",["collection"]="图鉴",["heroes"]="英雄",["growth"]="成长庇护",["pets"]="宠物小屋",["recruit"]="招募",["friends"]="好友",["guild"]="骑士团",["forge"]="钢铁工坊",["achievements"]="成就",["waiting_room"]="英雄休息室",["imprint"]="刻印回路",
            ["notice"]="公告",["lobby_custom"]="大厅设置",["pub"]="酒馆",["settings"]="设置",["music"]="音乐播放器",["music_prev"]="上一首",["music_play"]="播放音乐",["music_next"]="下一首",
            ["sanctuary"]="圣域",["milestones"]="光辉里程碑",["secret_shop"]="秘密商店",["epic_pass"]="史诗通行券",["events"]="活动",["bunny_hideout"]="秘密♡狡兔窟",["notifications"]="通知",["boost"]="增益效果"
        };
        public static string Key(string id)=>Prefix+id;
        public static string Text(string key){if(key!=null&&key.StartsWith(Prefix,StringComparison.Ordinal))key=key.Substring(Prefix.Length);return key!=null&&Zh.TryGetValue(key,out var text)?text:"功能尚未完成";}
        public static bool MainAvailable(string id)=>id=="arena"||id=="summon"||id=="shop";
    }
}
