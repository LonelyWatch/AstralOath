using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using UnityEditor;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public static class UiAudioCollector
    {
        public static void Refresh()
        {
            var settings = AssetBundleCollectorSettingData.Setting;
            var package = settings.Packages.Single(item => item.PackageName == "Foundation");
            var group = package.Groups.SingleOrDefault(item => item.GroupName == "UiAudio")
                ?? AssetBundleCollectorSettingData.CreateGroup(package, "UiAudio");
            group.Collectors.Clear();
            foreach (string path in new[] { "Assets/Game/Common/Res/Sounds", "Assets/Game/Modules/Summon/Res/Presentation",
                "Assets/Game/Modules/BattleScene/Res/Sounds/PvpGate" })
                AssetBundleCollectorSettingData.CreateCollector(group, new AssetBundleCollector
                {
                    CollectPath = path,
                    CollectorGUID = AssetDatabase.AssetPathToGUID(path),
                    FilterRuleName = nameof(UiAudioClipFilter),
                    AddressRuleName = nameof(UiAudioClipAddress),
                    PackRuleName = nameof(PackDirectory)
                });
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }
    }

    public sealed class UiAudioClipFilter : IFilterRule
    {
        public string FindAssetType => EAssetSearchType.AudioClip.ToString();
        public bool IsCollectAsset(FilterRuleData data) => AssetDatabase.GetMainAssetTypeAtPath(data.AssetPath) == typeof(UnityEngine.AudioClip);
    }

    public sealed class UiAudioClipAddress : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data) => UiAudioPlayback.ClipAddress(Path.GetFileNameWithoutExtension(data.AssetPath));
    }
}
