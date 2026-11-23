using AstralOath.Client.Common;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class SingleSlotEquipmentTests
    {
        [Test]
        public void EquippedPopupKeepsPortraitProportionsAndSolidReadingSurface()
        {
            var window = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Home/Res/Prefab/SingleSlotEquipment.prefab").GetComponent<BindingMap>();
            var panel = window.Require<GameObject>("popup").transform.Find("Detail safe area/Equipment details");
            var ratio = panel.GetComponent<AspectRatioFitter>();
            Assert.That(ratio, Is.Not.Null);
            Assert.That(ratio.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.FitInParent));
            Assert.That(ratio.aspectRatio, Is.InRange(.64f,.68f));
            var surface = panel.GetComponent<Image>();
            Assert.That(surface.sprite, Is.Null);
            Assert.That(surface.color.a, Is.EqualTo(1f));
        }

        [Test]
        public void ComparisonTextRetainsSolidStrokesAtItsDisplayedSize()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Home/Res/Prefab/SingleSlotEquipment.prefab").GetComponent<BindingMap>();
            var title = page.Require<GameText>("title");
            var ink = title.fontSharedMaterial;
            Assert.That(ink, Is.Not.EqualTo(title.font.material), "The detail page has its own SDF edge response.");
            Assert.That(ink.GetFloat("_Sharpness"), Is.GreaterThan(0f));
            Assert.That(ink.GetFloat("_OutlineWidth"), Is.Zero);
            Assert.That(ink.mainTexture, Is.EqualTo(title.font.atlasTexture));
            void Check(GameText value) => Assert.That(value.fontSharedMaterial, Is.EqualTo(ink), value.name);
            foreach (var key in new[] { "current", "candidate", "popupItem" })
            {
                var column = page.Require<BindingMap>(key);
                foreach (var text in new[] { "name", "grade", "set", "description" })
                    Check(column.Require<GameText>(text));
                foreach (var keyWithRegularStrokes in new[] { "grade", "name", "set" })
                    Assert.That(column.Require<GameText>(keyWithRegularStrokes).fontStyle, Is.EqualTo(TMPro.FontStyles.Normal));
                Assert.That(column.Require<GameText>("set").richText, Is.True, "Only the set name is bold.");
                foreach (var row in new[] { "main", "sub0", "sub1", "sub2", "sub3", "score" })
                {
                    var map = column.Require<BindingMap>(row);
                    Check(map.Require<GameText>("label"));
                    Check(map.Require<GameText>("value"));
                }
                Assert.That(column.Require<BindingMap>("score").Require<GameText>("label").fontStyle, Is.EqualTo(TMPro.FontStyles.Normal));
            }
            var candidate = page.Require<GameObject>("row").GetComponent<BindingMap>();
            Check(candidate.Require<GameText>("stat"));
            Check(candidate.Require<GameText>("name"));
            Check(page.Require<GameButton>("unequip").GetComponentInChildren<GameText>(true));
            Check(page.Require<GameButton>("equip").GetComponentInChildren<GameText>(true));
        }

        [Test]
        public void ComparisonAndEquippedDetailShowDescriptionAndItemScore()
        {
            var view = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/Home/Res/Prefab/SingleSlotEquipment.prefab").GetComponent<BindingMap>();
            foreach (var key in new[] { "current", "candidate", "popupItem" })
            {
                var column = view.Require<BindingMap>(key);
                var description = column.Require<GameText>("description");
                var score = column.Require<BindingMap>("score");
                Assert.That(description.enableWordWrapping, Is.True);
                Assert.That(score.Require<GameText>("value").color.g, Is.GreaterThan(.7f));
                Assert.That(score.Require<GameText>("value").color.b, Is.LessThan(.6f));
                Assert.That(score.transform.parent.GetComponent<VerticalLayoutGroup>(), Is.Not.Null,
                    "The gold score row must follow the last visible substat, including on one-substat training gear.");
            }
            foreach (var key in new[] { "equip", "unequip" })
            {
                var button = view.Require<GameButton>(key);
                var rect = (RectTransform)button.transform;
                Assert.That(rect.anchorMax.y - rect.anchorMin.y, Is.GreaterThan(.107f));
                var caption = button.GetComponentInChildren<GameText>(true);
                Assert.That(caption.fontSize, Is.GreaterThanOrEqualTo(30));
                Assert.That(caption.fontStyle.HasFlag(TMPro.FontStyles.Bold), Is.True);
            }
        }

        [Test]
        public void HeroSlotsHaveAnIndependentComparisonAndAnEquippedItemPopup()
        {
            const string home = "Assets/Game/Modules/Home/Res/Prefab/";
            var owner = AssetDatabase.LoadAssetAtPath<GameObject>(home + "HeroInventory.prefab").GetComponent<BindingMap>();
            var slot = owner.Require<BindingMap>("gearBrowser").Require<BindingMap>("singleSlot");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(slot.gameObject),
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(home + "SingleSlotEquipment.prefab")));
            foreach (var key in new[] { "current", "candidate", "popupItem" })
            {
                var column = slot.Require<BindingMap>(key);
                var card = column.Require<BindingMap>("card");
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(card.gameObject).name,
                    Is.EqualTo("EquipmentItemCard"));
                var main = column.Require<BindingMap>("main");
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(main.gameObject).name,
                    Is.EqualTo("EquipmentStatRow"));
                for (int i = 0; i < 4; i++) Assert.That(column.Require<BindingMap>("sub" + i), Is.Not.Null);
            }
            var current = slot.Require<BindingMap>("current");
            var candidate = slot.Require<BindingMap>("candidate");
            var fitters = new[] { current, candidate, slot.Require<BindingMap>("popupItem"),
                slot.Require<GameObject>("row").GetComponent<BindingMap>() }
                .SelectMany(column => column.Require<BindingMap>("card").GetComponentsInParent<AspectRatioFitter>(true).Take(1)).ToArray();
            Assert.That(fitters.Length, Is.EqualTo(4));
            Assert.That(fitters.All(f => f.aspectMode == AspectRatioFitter.AspectMode.FitInParent && f.aspectRatio == 1f),
                "The square thumbnail ratios must survive serialization off-canvas.");
            var title = slot.Require<GameText>("title").rectTransform;
            var back = (RectTransform)slot.Require<GameButton>("back").transform;
            Assert.That(title.anchorMin.x, Is.EqualTo(back.anchorMin.x));
            Assert.That(title.offsetMin.x, Is.GreaterThanOrEqualTo(back.rect.width),
                "The title must clear the back button when the game window becomes narrow.");
            Assert.That(current.Require<GameObject>("empty"), Is.Not.SameAs(candidate.Require<GameObject>("empty")));
            Assert.That(slot.Require<ScrollRect>("candidates").vertical, Is.True);
            Assert.That(slot.Require<GameButton>("popupRemove").transform.IsChildOf(slot.Require<GameObject>("popup").transform), Is.True);
            Assert.That(slot.Require<GameButton>("equip").transform.IsChildOf(slot.Require<GameObject>("comparison").transform), Is.True);
        }
    }
}
