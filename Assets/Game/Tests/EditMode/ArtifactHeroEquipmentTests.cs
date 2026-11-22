using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class ArtifactHeroEquipmentTests
    {
        const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(1120)]
        [TestCase(1600)]
        public void ReusedBagRowCentersOnlyVisibleStarsOnPortraitAndWornArtifact(int width)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var owner = prefab.GetComponent<BindingMap>();
            var instance = UnityEngine.Object.Instantiate(owner.Require<GameObject>("bagRow"));
            try
            {
                ((RectTransform)instance.transform).sizeDelta = new Vector2(width, 145);
                var map = instance.GetComponent<BindingMap>();
                var type = typeof(HeroInventoryBinder).GetNestedType("Card", BindingFlags.NonPublic);
                var card = Activator.CreateInstance(type, Internal, null, new object[] { instance, false }, null);
                var star = owner.Require<Image>("artifactSlotStar0").sprite;
                foreach (int count in new[] { 3, 6, 1, 2, 5, 4, 0, 3 })
                {
                    var item = new HeroInventoryItem { instanceId = "hero", stars = count, level = 1 };
                    type.GetMethod("Display", Internal).Invoke(card, new object[] { item, "", "", null, star, false });
                    CheckRow(map, "star", 6, count, map.Require<Image>("portrait").rectTransform);
                    int artifactCount = Math.Min(5, count);
                    type.GetMethod("DisplayArtifact", Internal).Invoke(card, new object[] { (star, 0, artifactCount, false) });
                    CheckRow(map, "bagArtifactStar", 5, artifactCount, (RectTransform)map.Require<BindingMap>("bagArtifactSlot").transform);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void WornHeroArtifactCentersStarsAfterEquipChangeAndRemoval()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var art = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                ((RectTransform)instance.transform).sizeDelta = new Vector2(1920, 1080);
                var owner = instance.GetComponent<BindingMap>();
                var assembly = typeof(HeroInventoryBinder).Assembly;
                var bindings = Activator.CreateInstance(assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBindings"),
                    Internal, null, new object[] { owner }, null);
                var type = assembly.GetType("AstralOath.Client.Home.ArtifactInventoryBinder");
                var empty = new ArtifactInventorySnapshot();
                Func<CancellationToken, Task<ArtifactInventorySnapshot>> fetch = _ => Task.FromResult(empty);
                var binder = Activator.CreateInstance(type, Internal, null,
                    new object[] { bindings, null, art, null, null, null, null, null, fetch, null, null }, null);
                var hero = new HeroInventoryItem { instanceId = "hero" };
                var item = new ArtifactInventoryItem { instanceId = "artifact", heroInstanceId = hero.instanceId };
                type.GetField("snapshot", Internal).SetValue(binder, new ArtifactInventorySnapshot { artifacts = new[] { item } });
                var target = (RectTransform)owner.Require<GameButton>("artifactSlot").transform;
                foreach (int count in new[] { 3, 5, 4, 0, 3 })
                {
                    if (count > 0) item.templateId = art.Catalog.Templates.First(t => t.Stars == count).Id;
                    type.GetMethod("PaintSlot", Internal).Invoke(binder, new object[] { count > 0 ? hero : null });
                    CheckRow(owner, "artifactSlotStar", 5, count, target);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ReusedArtifactListCardCentersVisibleStarsOnItsEmblem()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/ArtifactInventoryCard.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<ArtifactArtCatalog>("Assets/Game/Modules/Home/Configs/ArtifactArtCatalog.asset");
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var type = typeof(ArtifactInventoryCard).Assembly.GetType("AstralOath.Client.Home.ArtifactInventoryCardBinder");
                var binder = Activator.CreateInstance(type, Internal, null, new object[] { instance }, null);
                var map = instance.GetComponent<BindingMap>();
                foreach (int count in new[] { 3, 5, 4, 3 })
                {
                    var entry = catalog.Find(catalog.Catalog.Templates.First(template => template.Stars == count).Id);
                    var item = new ArtifactInventoryItem { instanceId = "artifact", templateId = entry.template.Id };
                    type.GetMethod("Display", Internal).Invoke(binder, new object[] { item, entry, null, "", false, null });
                    CheckRow(map, "star", 5, count, map.Require<Image>("artifactCardPortrait").rectTransform);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void CheckRow(BindingMap map, string prefix, int capacity, int count, RectTransform target)
        {
            var visible = Enumerable.Range(0, capacity).Select(i => map.Require<Image>(prefix + i))
                .Where(image => image.enabled).ToArray();
            Assert.That(visible.Length, Is.EqualTo(count), prefix);
            if (count == 0) return;
            float CenterX(RectTransform rect) => rect.TransformPoint(rect.rect.center).x;
            var center = (CenterX(visible[0].rectTransform) + CenterX(visible[count - 1].rectTransform)) * .5f;
            Assert.That(center, Is.EqualTo(CenterX(target)).Within(.01f), prefix + " " + count);
        }

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
