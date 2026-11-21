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
            var fitters = slot.GetComponentsInChildren<AspectRatioFitter>(true);
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
