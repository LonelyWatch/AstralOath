using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ProfileView=AstralOath.Client.PlayerProfile.PlayerProfile;
namespace AstralOath.Client.Tests
{
    public sealed class PlayerProfilePrefabTests
    {
        [Test] public void UsesOriginalFixedProfileCardAndPlainTextContract()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            Assert.That(prefab,Is.Not.Null);UIViewRegistry.ValidatePrefab<ProfileView>(prefab);var map=prefab.GetComponent<BindingMap>();
            Assert.That(map.Require<RawImage>("portrait").rectTransform.rect.width,Is.EqualTo(map.Require<RawImage>("portrait").rectTransform.rect.height).Within(.1));
            Assert.That(prefab.GetComponentsInChildren<GameText>(true).All(t=>!t.richText),Is.True);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c&&c.GetType()==typeof(ProfileView)),Is.False);
            var frame=prefab.GetComponentsInChildren<Image>(true).Single(x=>x.name=="Original profile frame");
            Assert.That(frame.sprite.name,Is.EqualTo("ProfileFrame"));Assert.That(frame.sprite.border,Is.EqualTo(new Vector4(54,54,54,54)));Assert.That(frame.type,Is.EqualTo(Image.Type.Sliced));
            Assert.That(frame.rectTransform.sizeDelta,Is.EqualTo(new Vector2(1470,946.5f)));
            Assert.That(map.Require<TMP_InputField>("greetingInput").richText,Is.False);
            foreach(string key in new[]{"nicknameSave","greetingSave"})Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(map.Require<GameButton>(key)),Is.Not.Null,"复用Common按钮嵌套");
        }

        [Test] public void NicknameContentsUseCsbCoordinatesInsideTheVisibleSourceFrame()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            var map=prefab.GetComponent<BindingMap>();
            var frame=prefab.GetComponentsInChildren<Image>(true).Single(i=>i.name=="nickname source frame");
            Assert.That(frame.sprite.border,Is.EqualTo(new Vector4(44,48,48,44)),"保留原框capInsets；白线在源PNG内约32px处");
            var title=frame.transform.Find("Title").GetComponent<RectTransform>();
            Assert.That(title.anchorMin,Is.EqualTo(new Vector2(0,1)),"按CSB的像素坐标摆放，不以包括源透明边的全框百分比代替");
            Assert.That(title.anchoredPosition.y,Is.EqualTo(-82.83f).Within(.1f));
            var cancel=(RectTransform)map.Require<GameButton>("nicknameCancel").transform;
            var save=(RectTransform)map.Require<GameButton>("nicknameSave").transform;
            Assert.That(cancel.anchoredPosition,Is.EqualTo(new Vector2(127.5f,-417f)));
            Assert.That(cancel.sizeDelta,Is.EqualTo(new Vector2(229.5f,97.5f)));
            Assert.That(save.anchoredPosition,Is.EqualTo(new Vector2(357f,-417f)));
            Assert.That(save.sizeDelta,Is.EqualTo(new Vector2(372f,97.5f)));
            // Source opaque white lower edge is ~544.5 within the 600-high sliced frame.
            Assert.That(-cancel.anchoredPosition.y+cancel.sizeDelta.y,Is.LessThan(544.5f));
            Assert.That(-save.anchoredPosition.y+save.sizeDelta.y,Is.LessThan(544.5f));
        }

        [Test] public void PortraitUsesRecordingCropAndRetainsSquareRenderSurface()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            var portrait=prefab.GetComponent<BindingMap>().Require<RawImage>("portrait").rectTransform;
            var card=(RectTransform)portrait.parent;
            Assert.That(card.GetComponent<RectMask2D>(),Is.Not.Null);
            Assert.That(portrait.rect.width,Is.EqualTo(portrait.rect.height).Within(.01));
            Assert.That(portrait.rect.height/card.rect.height,Is.EqualTo(1.3f).Within(.001),"原13s人物下半身越过森林视口，由窗口裁切；不把整个人物缩入卡片");
            Assert.That(portrait.anchoredPosition.y,Is.EqualTo(-31f).Within(.01));
            Assert.That(portrait.anchoredPosition.x+portrait.rect.width*.5f,Is.EqualTo(card.rect.width*.5f).Within(.01));
        }

        [Test] public void GreetingEditorUsesZInputFrameEqualButtonsAndTopAlignedText()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            var map=prefab.GetComponent<BindingMap>();
            var frame=prefab.GetComponentsInChildren<Image>(true).Single(i=>i.name=="greeting source frame").rectTransform;
            Assert.That(frame.sizeDelta,Is.EqualTo(new Vector2(834,609)),"_z_input.csb原frame556×406乘1.5");
            Assert.That(frame.anchoredPosition,Is.EqualTo(new Vector2(321,-265.5f)));
            foreach(string key in new[]{"greetingCancel","greetingSave"})
            {var rect=(RectTransform)map.Require<GameButton>(key).transform;Assert.That(rect.sizeDelta,Is.EqualTo(new Vector2(288,91.5f)));Assert.That(rect.anchoredPosition.y,Is.EqualTo(-472.5f));}
            Assert.That(map.Require<TMP_InputField>("greetingInput").textComponent.alignment,Is.EqualTo(TextAlignmentOptions.TopLeft));
        }

        [Test] public void RecordRegionRestoresOriginalWhiteBackplateShadowAndRotatedSourceRule()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            var images=prefab.GetComponentsInChildren<Image>(true);
            var backing=images.SingleOrDefault(i=>i.name=="Original record backing");
            Assert.That(backing,Is.Not.Null,"user_profile.csb bg 使用原_white tint黑 opacity63，不能遗漏记录区共享底");
            Assert.That(backing.sprite.name,Is.EqualTo("RecordWhite"));
            Assert.That(backing.color,Is.EqualTo((Color)new Color32(0,0,0,63)));
            Assert.That(backing.rectTransform.anchoredPosition,Is.EqualTo(new Vector2(653.8785f,-289.5f)));
            Assert.That(backing.rectTransform.sizeDelta,Is.EqualTo(new Vector2(805.5f,307.5f)));
            var lower=images.Single(i=>i.name=="Original record lower rule");
            Assert.That(lower.sprite.name,Is.EqualTo("RecordWhite"));
            Assert.That(lower.color.a,Is.EqualTo(Mathf.GammaToLinearSpace(25f/255f)).Within(.00001f),"原白线25先映射线性覆盖率，不变成亮白边");
            Assert.That(lower.rectTransform.anchoredPosition,Is.EqualTo(new Vector2(653.25f,-596.25f)));
            var column=images.Single(i=>i.name=="Original record column rule");
            Assert.That(column.sprite.name,Is.EqualTo("RecordColumnRule"));
            Assert.That(column.sprite.border,Is.EqualTo(new Vector4(32,1,31,1)),"本页CSB的专用cap，不能挪用装备管理页5/1/5/1");
            Assert.That(column.rectTransform.sizeDelta,Is.EqualTo(new Vector2(277.5f,6)));
            Assert.That(column.rectTransform.anchoredPosition,Is.EqualTo(new Vector2(1053,-303)));
            Assert.That(Mathf.DeltaAngle(column.rectTransform.localEulerAngles.z,-90),Is.EqualTo(0).Within(.01f));
            var shade=images.Single(i=>i.name=="Original right-side source shade");
            Assert.That(shade.sprite.name,Is.EqualTo("RecordShade"));
            Assert.That(shade.color,Is.EqualTo((Color)new Color32(8,14,23,76)));
        }

        [Test] public void SourceWhiteDividersBakeNodeOpacityBeforeLinearCoverageConversion()
        {
            // PNG alpha is not sRGB decoded by the GPU. Fold the authored node127 first:
            // opaque star255 ->54 linear alpha; source line128 ->13. Original file stays read only.
            var title=ReadPixels("Divider");
            Assert.That(title.Max(p=>p.a),Is.EqualTo(54),"白色星点 source255*node127 不能仍是255或只用普通UI alpha127");
            Assert.That(title.Any(p=>p.a==13),Is.True,"源线128*node127折合sRGB约64，线性alpha约13");
            var greeting=ReadPixels("GreetingDivider");Assert.That(greeting.Max(p=>p.a),Is.EqualTo(54));
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/PlayerProfile/Res/Prefab/PlayerProfile.prefab");
            var frame=prefab.GetComponentsInChildren<Image>(true).Single(i=>i.name=="greeting source frame");
            var rule=frame.GetComponentsInChildren<Image>().Single(i=>i.name=="Original title rule");
            Assert.That(rule.sprite.name,Is.EqualTo("GreetingDivider"));Assert.That(rule.sprite.border,Is.EqualTo(new Vector4(32,4,32,4)));
            Assert.That(rule.color.a,Is.EqualTo(1),"节点127已烘入用途纹理，不得二次乘透明度");
            var column=ReadPixels("RecordColumnRule");
            Assert.That(column.Where(p=>p.r==255&&p.g==255&&p.b==255).Max(p=>p.a),Is.EqualTo(18),"z_tile_bar源白点255*node76后的线性覆盖率；黑色阴影保留自己的有效alpha");
        }

        private static Color32[] ReadPixels(string name)
        {
            string path="Assets/Game/Modules/PlayerProfile/Res/Textures/"+name+".png";
            Assert.That(File.Exists(path),Is.True,"缺少最小源纹理用途副本："+name);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            try{Assert.That(texture.LoadImage(File.ReadAllBytes(path)),Is.True);return texture.GetPixels32();}
            finally{Object.DestroyImmediate(texture);}
        }
    }
}
