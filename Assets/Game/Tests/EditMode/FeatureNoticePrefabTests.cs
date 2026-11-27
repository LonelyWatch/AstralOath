using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class FeatureNoticePrefabTests
    {
        [Test]
        public void UnavailableFeaturesUseOneSourceBannerWithPlainTextAndFullScreenDismissal()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/FeatureNotice.prefab");
            Assert.That(prefab,Is.Not.Null,"Unavailable entries need an actual reusable source-art banner");
            var map=prefab.GetComponent<BindingMap>();
            Assert.That(map.Require<GameText>("title").richText,Is.False);
            Assert.That(map.Require<GameText>("message").richText,Is.False);
            Assert.That(map.Require<GameText>("closeLabel").richText,Is.False);
            Assert.That(map.Require<Image>("surface").sprite.name,Is.EqualTo("SourceMessageBox"));
            Assert.That(map.Require<Image>("surface").type,Is.EqualTo(Image.Type.Sliced));
            Assert.That(map.Require<Image>("arrow").sprite.name,Is.EqualTo("cm_icon_etcarrow"));
            Assert.That(map.Require<GameButton>("dismiss").targetGraphic.raycastTarget,Is.True);
            var shade=map.Require<GameButton>("dismiss").GetComponent<RectTransform>();
            Assert.That(shade.anchorMin,Is.EqualTo(Vector2.zero));
            Assert.That(shade.anchorMax,Is.EqualTo(Vector2.one));
        }
    }
}
