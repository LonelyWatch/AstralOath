using System;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class MailPrefabTests
    {
        [Test]
        public void RewardAtlasAndCanonicalSourceSlicesSurviveActualImport()
        {
            const string art="Assets/Game/Modules/Mail/Res/Textures/";
            AssetDatabase.ImportAsset(art+"RewardTitleFrames.png",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(art+"SingleRewardFrames.png",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(art+"RewardTitleFrames.png");
            var single=AssetDatabase.LoadAssetAtPath<Texture2D>(art+"SingleRewardFrames.png");Assert.That(single.width,Is.EqualTo(3072));Assert.That(single.height,Is.EqualTo(3072));Assert.That(single.format,Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(atlas.width,Is.EqualTo(4096));Assert.That(atlas.height,Is.EqualTo(1152));Assert.That(atlas.format,Is.EqualTo(TextureFormat.RGBA32));
            var importer=(TextureImporter)AssetImporter.GetAtPath(art+"RewardTitleFrames.png");Assert.That(importer.sRGBTexture,Is.False);Assert.That(importer.GetPlatformTextureSettings("Standalone").maxTextureSize,Is.EqualTo(4096));
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(art+"_btn_green_r.png").border,Is.EqualTo(new Vector4(30,14,30,12)));
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(art+"_box_popup.png").border,Is.EqualTo(new Vector4(44,48,48,44)));
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(art+"_bar_24.png").border,Is.EqualTo(new Vector4(22,4,38,4)));
        }
        [Test]
        public void MailWindowHasBindingMapAndOrdinaryViewContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab");
            Assert.That(prefab, Is.Not.Null, "邮件页面必须有真实预制体");
            Assert.That(prefab.GetComponent<BindingMap>(), Is.Not.Null);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("AstralOath.Client.Mail.MailWindow")).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null, "邮件页面必须有普通C# View");
            Assert.That(typeof(UIBaseView).IsAssignableFrom(type), Is.True);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.GetType() == type), Is.False);
            var map = prefab.GetComponent<BindingMap>();
            foreach (string key in new[] { "back", "inbox", "history", "claimAll" }) Assert.That(map.Require<GameButton>(key), Is.Not.Null);
            foreach (string key in new[] { "capacity", "empty", "message" }) Assert.That(map.Require<GameText>(key), Is.Not.Null);
        }
        [Test]
        public void MailDetailAndTooltipAreSeparateScopedLayers()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab");
            Assert.That(prefab, Is.Not.Null, "缺少邮件页面");
            var map = prefab.GetComponent<BindingMap>();
            Assert.That(map.Require<GameObject>("detail"), Is.Not.SameAs(map.Require<GameObject>("tooltip")));
            Assert.That(map.Require<GameText>("detailBody").richText, Is.False, "GM正文是纯文本");
            Assert.That(map.Require<GameButton>("detailClaim"), Is.Not.Null);
            Assert.That(map.Require<GameButton>("confirmCancel"), Is.Not.Null);
        }
    }
}
