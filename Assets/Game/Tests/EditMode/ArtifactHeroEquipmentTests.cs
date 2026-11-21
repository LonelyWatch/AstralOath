using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArtifactHeroEquipmentTests
    {
        [Test]
        public void WornArtifactHasSeparateDetailSelectionAndPaintingViews()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var owner = root.GetComponent<BindingMap>();
            var page = owner.Require<BindingMap>("artifactHeroEquipment");
            var popup = page.Require<GameObject>("popup");
            var picker = page.Require<GameObject>("picker");
            Assert.That(page.Require<GameButton>("change").transform.IsChildOf(popup.transform), Is.True);
            Assert.That(page.Require<GameButton>("equip").transform.IsChildOf(picker.transform), Is.True);
            Assert.That(page.Require<ScrollRect>("list").transform.IsChildOf(picker.transform), Is.True);
            Assert.That(page.Require<Image>("paintingImage").transform.IsChildOf(page.Require<GameObject>("painting").transform), Is.True);
            Assert.That(page.Require<GameText>("popupBody").richText, Is.True);
            foreach (var key in new[] { "enhance", "training", "retry" })
            {
                var face = (Image)page.Require<GameButton>(key).targetGraphic;
                Assert.That(face.sprite, Is.Not.Null, key);
                Assert.That(face.type, Is.EqualTo(Image.Type.Sliced), key);
            }
            for (int i = 0; i < 5; ++i)
                Assert.That(owner.Require<Image>("artifactSlotStar" + i), Is.Not.Null);
        }
    }
}
