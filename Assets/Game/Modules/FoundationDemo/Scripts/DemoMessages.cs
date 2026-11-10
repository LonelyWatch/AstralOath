using System;
using System.Collections.Generic;

namespace AstralOath.Client.FoundationDemo
{
    public static class DemoMessages
    {
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Text { get; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["zh-Hans"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["foundation.title"] = "星誓 · 客户端地基",
                    ["foundation.message"] = "资源包 · 脚本绑定 · 协议联通",
                    ["foundation.open"] = "打开演示",
                    ["foundation.close"] = "关闭",
                    ["foundation.echo"] = "发送回显",
                    ["foundation.loading"] = "正在加载演示资源…",
                    ["foundation.ready"] = "已加载 {0}，配置时长 {1} 秒",
                    ["foundation.pending"] = "正在请求服务端…",
                    ["foundation.echo.ok"] = "服务端返回 {0} · 序号 {1}",
                    ["foundation.echo.error"] = "回显请求失败，请检查开发服务地址及日志。",
                    ["foundation.update.recovered"] = "更新失败，已使用上个有效版本。",
                    ["foundation.load.error"] = "地基初始化失败，请查看日志。"
                }
            };
    }
}