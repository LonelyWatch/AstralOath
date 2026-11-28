using System;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class MailPrefabTests
    {
        [TestCase("confirmTitle")]
        [TestCase("confirmBody")]
        public void ClaimAllPromptUsesSourceGrayInkRatherThanPureWhite(string key)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab");
            var label=prefab.GetComponent<BindingMap>().Require<GameText>(key);
            // The leading question and details are both content in the reference, not a white dialog heading.
            Color expected=new Color32(180,180,180,255);
            Assert.That(label.color.r,Is.EqualTo(expected.r).Within(.003f),key+" must author source gray once; Canvas owns color-space conversion");
            Assert.That(label.color.g,Is.EqualTo(expected.g).Within(.003f));
            Assert.That(label.color.b,Is.EqualTo(expected.b).Within(.003f));
            Assert.That(label.color.a,Is.EqualTo(1),"Use gray ink, not a translucent white approximation");
            Assert.That(prefab.GetComponent<BindingMap>().Require<GameText>("confirmBody").fontSize,
                Is.EqualTo(prefab.GetComponent<BindingMap>().Require<GameText>("confirmTitle").fontSize),
                "The leading question and the details belong to the same source content style");
        }

        [TestCase("Styles/RoundedBlue/ConfirmButton")]
        [TestCase("Styles/RoundedGold/CancelButton")]
        public void SharedMessageButtonsDoNotInflateRegularGlyphsWithThickWhiteContours(string path)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/"+path+".prefab");
            var label=prefab.GetComponent<BindingMap>().Require<GameText>("label");
            Assert.That(label.fontStyle,Is.EqualTo(TMPro.FontStyles.Normal));
            Assert.That(label.fontWeight,Is.EqualTo(TMPro.FontWeight.Regular));
            Assert.That(label.fontSharedMaterial.GetFloat("_FaceDilate"),Is.Zero);
            Assert.That(label.fontSharedMaterial.GetFloat("_OutlineWidth"),Is.InRange(0f,.10f),
                "A regular face with the old .28 white contour still looks artificially bold in the actual Player");
        }

        [Test]
        public void MailConfirmationReloadsSharedPrefabWithItsLabelsAndSourceAudio()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab");
            var map=prefab.GetComponent<BindingMap>();
            var panel=map.Require<GameButton>("confirmOk").transform.parent.GetComponent<BindingMap>();
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(panel)),
                Is.EqualTo("Assets/Game/Common/Res/Prefab/SourceConfirmation.prefab"));
            Assert.That(map.Require<GameText>("confirmTitle").text,Is.EqualTo(Mail.MailLabels.Text("confirmTitle")));
            Assert.That(map.Require<GameText>("confirmBody").text,Is.EqualTo(Mail.MailLabels.Text("confirmBody")));
            Assert.That(map.Require<GameText>("confirmCancelLabel").text,Is.EqualTo("取消"));
            Assert.That(map.Require<GameText>("confirmOkLabel").text,Is.EqualTo("确认"));
            var confirm=map.Require<GameButton>("confirmOk");
            Assert.That(confirm.GetComponent<AudioSource>().clip.name,Is.EqualTo("UiAccept"));
            Assert.That(confirm.FeedbackSound,Is.EqualTo(GameUiFeedback.Sound.Normal));
            Assert.That(((Image)confirm.targetGraphic).pixelsPerUnitMultiplier,Is.GreaterThan(1),"源缩放后的端帽不能占满按钮宽度");
            var toast=map.Require<GameObject>("messageChrome");
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(toast)),
                Is.EqualTo("Assets/Game/Common/Res/Prefab/GameToast.prefab"));
        }
        [Test]
        public void RewardAtlasAndCanonicalSourceSlicesSurviveActualImport()
        {
            const string art="Assets/Game/Modules/Mail/Res/Textures/";
            AssetDatabase.ImportAsset(art+"RewardTitleFrames.png",ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            const string singlePath="Assets/Game/Common/Res/SingleItemReward/SingleRewardFrames.png";
            AssetDatabase.ImportAsset(singlePath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var atlas=AssetDatabase.LoadAssetAtPath<Texture2D>(art+"RewardTitleFrames.png");
            var single=AssetDatabase.LoadAssetAtPath<Texture2D>(singlePath);Assert.That(single.width,Is.EqualTo(3072));Assert.That(single.height,Is.EqualTo(3072));Assert.That(single.format,Is.EqualTo(TextureFormat.RGBA32));
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
