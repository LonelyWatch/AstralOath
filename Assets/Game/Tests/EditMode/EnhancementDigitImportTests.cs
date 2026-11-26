using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class EnhancementDigitImportTests
    {
        [TestCase("0")]
        [TestCase("1")]
        [TestCase("2")]
        [TestCase("3")]
        [TestCase("4")]
        [TestCase("5")]
        [TestCase("6")]
        [TestCase("7")]
        [TestCase("8")]
        [TestCase("9")]
        [TestCase("plus")]
        public void OriginalNumeralRemainsNativeRgbaAfterTheRealImportPipeline(string token)
        {
            string path = "Assets/Game/Common/Res/Textures/EnhancementDigits/itxt_num" + token + "_b.png";
            string guid = AssetDatabase.AssetPathToGUID(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            TestContext.WriteLine($"{path}: imported={texture.format} {texture.width}x{texture.height}, userData={importer.userData}");
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "Android" })
            {
                var setting = importer.GetPlatformTextureSettings(platform);
                TestContext.WriteLine($"  {platform}: format={setting.format}, compression={setting.textureCompression}, overridden={setting.overridden}");
            }
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32),
                "The source numeral and its dark alpha edge must not pass through block compression.");
            Assert.That(new Vector2(texture.width, texture.height), Is.EqualTo(new Vector2(26, 34)), path);
            Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(26, 34)), path);
            Assert.That(importer.userData, Is.EqualTo("AstralOath.ImportOverride"), path);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(texture.mipmapCount, Is.EqualTo(1));
            foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "Android" })
            {
                var setting = importer.GetPlatformTextureSettings(platform);
                Assert.That(setting.format, Is.EqualTo(TextureImporterFormat.RGBA32), platform);
                Assert.That(setting.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), platform);
                Assert.That(setting.crunchedCompression, Is.False, platform);
                if (platform != "DefaultTexturePlatform") Assert.That(setting.overridden, Is.True, platform);
            }
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid), "Reimport must retain the existing source asset GUID.");
        }
    }
}
