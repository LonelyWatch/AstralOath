using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArtifactEnhancementTests
    {
        [Test]
        public void EnhancementHasASeparatePageWithRemovableMaterialsAndScrollList()
        {
            var inventory = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var page = inventory.GetComponent<BindingMap>().Require<BindingMap>("artifactEnhancement");
            Assert.That(page.gameObject.activeSelf, Is.False);
            Assert.That(page.Require<GameButton>("back").IsInteractable(), Is.True);
            Assert.That(page.Require<GameButton>("submit").IsInteractable(), Is.True);
            Assert.That(page.Require<GameText>("cost").transform.IsChildOf(page.Require<GameButton>("submit").transform));
            Assert.That(page.Require<ScrollRect>("candidates").horizontal, Is.False);
            var tray = page.Require<ScrollRect>("tray");
            Assert.That(tray.horizontal, Is.False);
            Assert.That(page.Require<Image>("portrait").preserveAspect, Is.True);
            for (int i = 0; i < 20; ++i)
            {
                Assert.That(page.Require<Image>("material" + i).transform.IsChildOf(page.Require<GameButton>("remove" + i).transform));
                Assert.That(page.Require<GameButton>("remove" + i).transform.parent, Is.SameAs(tray.content));
            }
        }
    }
}
