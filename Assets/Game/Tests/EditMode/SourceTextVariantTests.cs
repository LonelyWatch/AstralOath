using System.Linq;
using AstralOath.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using YooAsset.Editor;

namespace AstralOath.Client.Tests
{
    public sealed class SourceTextVariantTests
    {
        [Test]
        public void SourceCaptionsAndMailCountersExplicitlyCollectTheirFourOutlineVariants()
        {
            const string common = "Assets/Game/Common/Res/";
            const string variantsPath = common + "Materials/SourceTextVariants.shadervariants";
            var variants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(variantsPath);
            Assert.That(variants, Is.Not.Null);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader");
            Assert.That(variants.shaderCount, Is.EqualTo(1));
            Assert.That(variants.variantCount, Is.EqualTo(4));
            foreach (var keywords in new[]
            {
                new[] { "OUTLINE_ON" },
                new[] { "OUTLINE_ON", "UNITY_UI_CLIP_RECT" },
                new[] { "OUTLINE_ON", "UNITY_UI_ALPHACLIP" },
                new[] { "OUTLINE_ON", "UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP" }
            })
                Assert.That(variants.Contains(new ShaderVariantCollection.ShaderVariant(
                    shader, PassType.Normal, keywords)), Is.True, string.Join(" ", keywords));

            var settings = AssetDatabase.LoadAssetAtPath<AssetBundleCollectorSetting>(
                "Assets/Game/Modules/FoundationDemo/AssetBundleCollectorSetting.asset");
            Assert.That(settings, Is.Not.Null);
            var package = settings.Packages.Single(entry => entry.PackageName == "Foundation");
            var group = package.Groups.Single(entry => entry.GroupName == "HomeArena");
            var collector = group.Collectors.Single(entry => entry.CollectPath == variantsPath);
            Assert.That(collector.CollectorType, Is.EqualTo(ECollectorType.MainAssetCollector));
            Assert.That(collector.PackRuleName, Is.EqualTo(nameof(PackShaderVariants)));
            Assert.That(collector.AddressRuleName, Is.EqualTo(nameof(HomeArenaAddressRule)));
            var address = new HomeArenaAddressRule().GetAssetAddress(new AddressRuleData(
                variantsPath, collector.CollectPath, group.GroupName, collector.UserData));
            Assert.That(address, Is.EqualTo("common/source-text-variants"));
        }
    }
}
