using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public sealed class EntryThemeAddressRule : IAddressRule
    {
        internal const string BackgroundDirectory = "Assets/Game/Modules/Home/Res/Textures/Entry/";
        internal const string ConfigPath = "Assets/Game/Modules/Home/Configs/entry-theme.json";

        public string GetAssetAddress(AddressRuleData data) => AddressFor(data.AssetPath);

        internal static string AddressFor(string path) => path == ConfigPath
            ? "entry/theme" : "entry/backgrounds/" + Path.GetFileNameWithoutExtension(path);

        internal static void Validate(IReadOnlyDictionary<string, string> addresses)
        {
            var config = AssetDatabase.LoadAssetAtPath<TextAsset>(ConfigPath);
            var theme = config ? JsonUtility.FromJson<Theme>(config.text) : null;
            if (!addresses.TryGetValue("entry/theme", out var configPath) || configPath != ConfigPath ||
                string.IsNullOrEmpty(theme?.backgroundAddress) ||
                !addresses.TryGetValue(theme.backgroundAddress, out var background) ||
                !AssetDatabase.LoadAssetAtPath<Sprite>(background))
                throw new BuildFailedException("启动界面的配表或主题图未被正确收集。请导表并检查 Entry 资源。");

            const string builtIn = "Assets/Game/Bootstrap/Resources/Entry/";
            if (!AssetDatabase.LoadAssetAtPath<Sprite>(builtIn + "TitleBackground.jpg"))
                throw new BuildFailedException("启动界面缺少母包背景：TitleBackground.jpg");
            foreach (var item in new[] { "TitleLogo", "LoginButton", "Key", "PatchPanel", "PanelFrame",
                         "Divider", "ProgressTrack", "ProgressFill", "ButtonGreenRound" })
                if (!AssetDatabase.LoadAssetAtPath<Sprite>(builtIn + item + ".png"))
                    throw new BuildFailedException("启动界面缺少母包 Sprite：" + item);
        }

        [Serializable] private sealed class Theme { public string backgroundAddress; }
    }

    public sealed class EntryThemeFilterRule : IFilterRule
    {
        public string FindAssetType => "Texture2D";

        public bool IsCollectAsset(FilterRuleData data)
        {
            var path = data.AssetPath;
            return path.StartsWith(EntryThemeAddressRule.BackgroundDirectory, StringComparison.Ordinal) &&
                (path.EndsWith(".jpg", StringComparison.Ordinal) || path.EndsWith(".png", StringComparison.Ordinal));
        }
    }
}
