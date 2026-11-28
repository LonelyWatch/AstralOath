using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatVisualTests
    {
        private const string Res="Assets/Game/Modules/ChannelChat/Res/";
        private static BindingMap View()=>AssetDatabase.LoadAssetAtPath<GameObject>(Res+"Prefab/ChannelChat.prefab").GetComponent<BindingMap>();
        private static Rect Bounds(RectTransform child,RectTransform parent)
        {
            var corners=new Vector3[4];child.GetWorldCorners(corners);var points=corners.Select(parent.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));
        }
        [Test] public void FavoriteUsesSourceVignetteAndBottomGroupCoordinates()
        {
            var map=View();var modal=map.Require<GameObject>("favoriteLayer");
            Assert.That(((Image)map.Require<GameButton>("favoriteClose").targetGraphic).sprite?.name,Is.EqualTo("FavoriteDim"));
            var frame=modal.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="favorite original frame");
            Assert.That(frame.sizeDelta,Is.EqualTo(new Vector2(837,579)),"558x386 source frame already has the correct height.");
            var input=map.Require<TMPro.TMP_InputField>("favoriteInput").GetComponent<RectTransform>();
            Assert.That(input.anchoredPosition.y,Is.EqualTo(-246*1.5f).Within(.01f));
            var save=map.Require<GameButton>("favoriteSave").GetComponent<RectTransform>();
            Assert.That(save.anchoredPosition.y,Is.EqualTo(-246*1.5f).Within(.01f));
            Assert.That(modal.GetComponentsInChildren<Image>(true).Any(i=>i.sprite&&i.sprite.name=="FavoriteShadow"),Is.True);
        }
        [Test] public void FavoriteTitleRulesMeetAtCenterAndRangeRemainsInsideVisibleFrame()
        {
            var modal=View().Require<GameObject>("favoriteLayer");
            var frame=modal.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="favorite original frame");
            var left=modal.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Title rule left");
            var right=modal.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Title rule right");
            Rect l=Bounds(left,frame),r=Bounds(right,frame);
            Assert.That(l.xMax,Is.EqualTo(frame.rect.xMin+276.998f*1.5f).Within(.02f));
            Assert.That(r.xMin,Is.EqualTo(frame.rect.xMin+275.998f*1.5f).Within(.02f));
            var caption=modal.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Allowed channel range");
            Assert.That(Bounds(caption,frame).yMin,Is.GreaterThan(frame.rect.yMin+48),"Range caption must stay above the PNG's visible lower border.");
        }
        [Test] public void DrawerKeepsSourceTopAndTranslucentNeutralPixels()
        {
            var map=View();Assert.That(map.Require<Image>("drawerSurface").rectTransform.anchoredPosition.y,Is.EqualTo(85.348f*1.5f).Within(.02f));
            Assert.That(((Image)map.Require<GameButton>("close").targetGraphic).sprite?.name,Is.EqualTo("ChatDim"));
            var pixels=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                Assert.That(ImageConversion.LoadImage(pixels,File.ReadAllBytes(Res+"Textures/DrawerFrameTransparent.png")),Is.True);
                var center=pixels.GetPixel(pixels.width/2,pixels.height/2);
                float alpha=230f/255f*127f/255f,gray=18f/255f;
                float expected=(1-Mathf.GammaToLinearSpace(1-alpha+alpha*gray))/(1-Mathf.GammaToLinearSpace(gray));
                Assert.That(center.a,Is.EqualTo(expected).Within(1.5f/255),"Transparency changes source opacity; it must not erase neutral backdrop pixels.");
            }
            finally{Object.DestroyImmediate(pixels);}
        }
        [Test] public void NoticeArrowRotatesItsOriginalUnstretchedSourceAspect()
        {
            var map=View();var arrow=map.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Notice collapse arrow");
            Assert.That(arrow.sizeDelta,Is.EqualTo(new Vector2(17,26)*1.5f));
            Assert.That(arrow.pivot,Is.EqualTo(Vector2.one*.5f));
            var bounds=Bounds(arrow,map.Require<RectTransform>("drawer"));
            Assert.That(bounds.width/bounds.height,Is.EqualTo(26f/17f).Within(.001f),"Rotate the 17x26 source once; do not swap dimensions before rotation.");
        }
    }
}
