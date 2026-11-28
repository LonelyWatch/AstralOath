#if UNITY_EDITOR
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    // Reference: actual recording 61.5s, normalized by frame height, plus
    // msgbox_renewal.csb's two distinct scale-nine ornaments and panel caps.
    public sealed class FeatureNoticeSourceVisualTests
    {
        private const string Prefab="Assets/Game/Common/Res/Prefab/FeatureNotice.prefab";
        private const string Art="Assets/Game/Common/Res/Textures/FeatureNotice/";
        [Test] public void NoticeKeepsItsOwnPanelAndSourceDimInsteadOfTheDecisionDialogSkin()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);Assert.That(prefab,Is.Not.Null);
            var map=prefab.GetComponent<BindingMap>();var panel=map.Require<Image>("surface");
            var authoredPanel=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"renew_popup_bg_2.png");Assert.That(authoredPanel,Is.Not.Null,"Dedicated authored notice panel must be imported.");
            Assert.That(panel.sprite,Is.SameAs(authoredPanel),
                "msgbox_renewal authors renew_popup_bg_2, not the msgbox.csb shared decision surface.");
            Assert.That(panel.type,Is.EqualTo(Image.Type.Sliced));Assert.That(panel.sprite.border,Is.EqualTo(new Vector4(54,54,54,54)));
            Assert.That(panel.color,Is.EqualTo(Color.white),"Node opacity is folded into the purpose copy exactly once.");
            var authoredDim=AssetDatabase.LoadAssetAtPath<Sprite>(Art+"_dim.png");Assert.That(authoredDim,Is.Not.Null);
            var dim=map.Require<Image>("dim");Assert.That(dim.sprite,Is.SameAs(authoredDim));
            Assert.That(dim.type,Is.EqualTo(Image.Type.Sliced));Assert.That(dim.color,Is.EqualTo(Color.white));
        }
        [Test] public void UpperAndLowerRulesPreserveTheirDifferentNativeEndCapsAndHeights()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);Assert.That(prefab,Is.Not.Null);
            var map=prefab.GetComponent<BindingMap>();var upper=map.Require<Image>("upperRule");var lower=map.Require<Image>("lowerRule");
            Assert.That(upper.sprite.name,Is.EqualTo("_bar_23"));Assert.That(lower.sprite.name,Is.EqualTo("_bar_2"));
            Assert.That(upper.type,Is.EqualTo(Image.Type.Sliced));Assert.That(lower.type,Is.EqualTo(Image.Type.Sliced));
            Assert.That(upper.sprite.border,Is.EqualTo(new Vector4(30,4,30,4)));Assert.That(lower.sprite.border,Is.EqualTo(new Vector4(32,4,32,4)));
            Assert.That(upper.rectTransform.sizeDelta,Is.EqualTo(new Vector2(960,18)));Assert.That(lower.rectTransform.sizeDelta,Is.EqualTo(new Vector2(955.5f,15)));
            Assert.That(upper.pixelsPerUnitMultiplier,Is.EqualTo(2f/3).Within(.0001f));Assert.That(lower.pixelsPerUnitMultiplier,Is.EqualTo(2f/3).Within(.0001f));
            Assert.That(upper.color,Is.EqualTo(Color.white));Assert.That(lower.color,Is.EqualTo(Color.white));
        }
        [Test] public void NoticeGeometryUsesMeasuredSteadyFrameAndClosesInsideTheLowerRule()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);var map=prefab.GetComponent<BindingMap>();
            var frame=map.Require<Image>("surface").rectTransform;
            Assert.That(frame.sizeDelta.y,Is.EqualTo(585).Within(.1),"Recorded steady-state banner is 390 native units high; CSB authors380 before runtime sizing.");
            var upper=map.Require<Image>("upperRule").rectTransform;var lower=map.Require<Image>("lowerRule").rectTransform;
            Assert.That(upper.anchoredPosition.y,Is.EqualTo(154.8f).Within(.1f));Assert.That(lower.anchoredPosition.y,Is.EqualTo(-129.6f).Within(.1f));
            Assert.That(map.Require<GameText>("closeLabel").rectTransform.anchoredPosition.y,Is.EqualTo(-178.5f).Within(.1f));
            Assert.That(map.Require<Image>("arrow").rectTransform.anchoredPosition.y,Is.EqualTo(-225).Within(.1f));
        }
    }
}
#endif
