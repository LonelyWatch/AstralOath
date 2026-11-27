using System.Collections.Generic;
namespace AstralOath.Client.PlayerProfile
{
    public static class PlayerProfileLabels
    {
        private static readonly Dictionary<string,string> Zh=new Dictionary<string,string>
        {
            ["close"]="点击关闭",["nickname"]="变更昵称",["check"]="重复检查",["cancel"]="取消",["change"]="变更",["input"]="点击此处并输入",
            ["nicknameRules"]="昵称可输入4-12个英文字母、2-8个包含中文的其他字符，无法输入空格或特殊字符。",
            ["season"]="竞技场赛季信息",["best"]="竞技场最佳成绩",["arena"]="竞技场",["worldArena"]="世界竞技场",["notParticipated"]="未参加",["private"]="不公开",
            ["friends"]="好友人数：—",["clan"]="骑士团\n未开放",["support"]="变更\n辅助英雄",["representative"]="设置代表英雄",["frame"]="变更边框",["customCard"]="编辑个性小卡",
            ["greetingTitle"]="修改问候语",["greetingDefault"]="您好，请多多指教。",["greetingLimit"]="限50个字",["loading"]="正在读取资料……",["saved"]="已保存。",["copied"]="已复制玩家编号。",
            ["invalid_nickname"]="昵称不符合输入规则。",["invalid_greeting"]="问候语最多50个字，不能包含控制字符。",["nickname_available"]="该昵称可以使用。",["nickname_unavailable"]="该昵称已被使用。",["check_first"]="请先重复检查昵称。",
            ["profile_version_conflict"]="资料已更新，请确认后重新操作。",["request_conflict"]="请求信息发生冲突，请重新打开资料页。",["error"]="资料请求失败，请重试。",["pending"]="请求结果待确认，请重新打开资料页重试。"
        };
        public static string Text(string key)=>Zh.TryGetValue(key,out var value)?value:Zh["error"];
    }
}
