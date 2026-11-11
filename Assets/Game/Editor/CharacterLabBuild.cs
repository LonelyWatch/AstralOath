using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset.Editor;
namespace AstralOath.Editor
{
    public static class CharacterLabBuild
    {
        [MenuItem("Tools/角色实验室/构建 Windows 测试包")]
        public static void BuildWindows() => HotUpdateBuilder.Build(CharacterLabBuildProfile.CharacterLab,
            BuildTarget.StandaloneWindows64, 0, true);

        [MenuItem("Tools/角色实验室/验证构建输入")]
        public static void Validate()
        {
            CharacterLabAssets.Validate();
            var profile = CharacterLabBuildProfile.CharacterLab;
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(profile.ScenePath))
                throw new BuildFailedException("先生成角色实验室场景：" + profile.ScenePath);
            if (!File.Exists(profile.AssemblyConfigPath))
                throw new BuildFailedException("缺少角色实验室热更清单：" + profile.AssemblyConfigPath);
            var package = AssetBundleCollectorSettingData.Setting.Packages.SingleOrDefault(item => item.PackageName == profile.PackageName);
            if (package == null || !package.EnableAddressable || package.Groups.Sum(group => group.Collectors.Count) == 0)
                throw new BuildFailedException("缺少完整的 CharacterLab YooAsset 收集配置。");
            var collected = AssetBundleCollectorSettingData.Setting.BeginCollect(profile.PackageName,
                simulateBuild: true, useAssetDependencyDB: false);
            foreach (string address in new[] { "characterlab/catalog", "characterlab/character-lab-window" })
                if (!collected.CollectAssets.Any(asset => asset.Address == address))
                    throw new BuildFailedException("CharacterLab 包缺少必要地址：" + address);
            Debug.Log("ASTRAL_CHARACTERLAB_BUILD_INPUTS_READY");
        }
    }
}
